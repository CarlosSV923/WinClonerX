using System.Text.Json;
using Shouldly;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.IntegrationTests.Fixtures;
using WinClonerX.Infrastructure.IntegrationTests.Spikes;
using Xunit;

namespace WinClonerX.Infrastructure.IntegrationTests.Cloning;

/// <summary>
/// Task T044. A full clone from a 2 GiB reference system VHDX to an empty 3 GiB VHDX, run through the real use cases and the
/// real adapters. Only the boot configurator, the recovery configurator and the offline registry editor are fakes (see
/// <see cref="CloneHarness"/>). Needs an elevated process:
/// <c>dotnet test tests/WinClonerX.Infrastructure.IntegrationTests --filter "FullyQualifiedName~CloneToLargerTests"</c>
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationDisks.Name)]
public sealed class CloneToLargerTests
{
    private static readonly ByteSize SourceSize = ByteSize.FromGiB(2);
    private static readonly ByteSize TargetSize = ByteSize.FromGiB(3);
    private const int SourceFileCount = 30;
    private const long Mib = 1L << 20;
    private const long GptBackupBytes = 16384;

    [Fact]
    public async Task CloneVhdxToLargerVhdx()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        VirtualDiskStorageOps ops = VirtualDiskStorageOps.Default;

        await using var fixture = new VirtualDiskFixture();
        ReferenceDisk source = await ReferenceSystemDisk.CreateAsync(
            fixture, ops, SourceSize, SourceFileCount, includeDummyBcd: true);
        AttachedVirtualDisk target = await fixture.CreateAndAttachAsync(TargetSize);
        await using var harness = new CloneHarness(source, Path.Combine(fixture.RootFolder, "logs"));

        // Before the clone: the source layout and the source files, read without the fixture decorator.
        Disk sourceBefore = await ReadDisk(harness, source.Disk.DiskNumber, ct);
        string sourceLayoutBefore = LayoutSignature(sourceBefore);
        IReadOnlyDictionary<string, string> sourceFilesBefore = ManifestOfPartition(
            ops, source.Disk.DiskNumber, source.Windows.PartitionNumber, source.Manifest.Keys);
        AssertSameManifest(source.Manifest, sourceFilesBefore, "the reference source before the clone");

        (ClonePlan plan, CloneResultReport report) = await harness.CloneAsync(source, target, ct);

        string failure = report.Structural is { } result
            ? string.Join("; ", result.Issues)
            : "no structural result";
        report.Outcome.ShouldBe(CloneOutcome.Completed, failure);
        VerificationResult structural = report.Structural.ShouldNotBeNull("the structural verification must run");
        structural.Passed.ShouldBeTrue(string.Join("; ", structural.Issues));

        // The target GPT, read with the real reader.
        Disk targetDisk = await ReadDisk(harness, target.DiskNumber, ct);
        AssertTargetLayoutMatchesPlan(plan, targetDisk, sourceBefore);
        AssertRecoveryIsAtTheEnd(targetDisk);

        // The Windows NTFS volume of the target has been grown to the partition.
        PartitionMapping windowsMapping = plan.Mappings.Single(mapping => mapping.Source.Role == PartitionRole.Windows);
        windowsMapping.Resize.ShouldBe(ResizeKind.GrowFileSystem);
        Partition targetWindows = targetDisk.Partitions.Single(partition => partition.UniqueId == windowsMapping.NewUniqueId);
        Volume targetWindowsVolume = targetWindows.Volume.ShouldNotBeNull("the target Windows partition must have a volume");
        ByteSize clusterSize = targetWindowsVolume.ClusterSize
            ?? throw new InvalidOperationException("The target Windows volume reports no cluster size.");
        long allowedShortfall = targetDisk.LogicalSectorSize.Bytes + clusterSize.Bytes;
        targetWindowsVolume.Capacity.Bytes.ShouldBeGreaterThanOrEqualTo(
            targetWindows.Extent.Length - allowedShortfall,
            "the NTFS volume must cover the partition minus one sector and one cluster");

        // The files on the target are identical to the reference files.
        IReadOnlyDictionary<string, string> targetFiles = ManifestOfPartition(
            ops, target.DiskNumber, targetWindows.Number, source.Manifest.Keys);
        AssertSameManifest(source.Manifest, targetFiles, "the clone");

        // The source is unchanged: same GPT layout and same files. The sector-level hash is not compared, because VSS writes its
        // diff area inside the source volume by design.
        Disk sourceAfter = await ReadDisk(harness, source.Disk.DiskNumber, ct);
        LayoutSignature(sourceAfter).ShouldBe(sourceLayoutBefore, "the source GPT layout must not change");
        IReadOnlyDictionary<string, string> sourceFilesAfter = ManifestOfPartition(
            ops, source.Disk.DiskNumber, source.Windows.PartitionNumber, source.Manifest.Keys);
        AssertSameManifest(sourceFilesBefore, sourceFilesAfter, "the source after the clone");

        // The execution log holds the disk operations of the run.
        List<(string Operation, string Result)> diskOperations = ReadDiskOperations(report.LogFile);
        diskOperations.Any(operation => operation.Operation == "Write" && operation.Result == "Success")
            .ShouldBeTrue("the log must hold a successful DiskOperation Write");
        diskOperations.Any(operation => operation.Operation == "CommitLayout" && operation.Result == "Success")
            .ShouldBeTrue("the log must hold a successful DiskOperation CommitLayout");

        // The boot repair ran against drive roots, with the recording fakes.
        harness.Registry.Calls.Count.ShouldBe(1, "RewriteMountedDevices is called once");
        IsDriveRoot(harness.Registry.Calls[0].WindowsRoot).ShouldBeTrue($"registry root '{harness.Registry.Calls[0].WindowsRoot}'");

        harness.Boot.Calls.Count.ShouldBe(1, "RebuildAsync is called once");
        IsDriveRoot(harness.Boot.Calls[0].WindowsRoot).ShouldBeTrue($"bcd windows root '{harness.Boot.Calls[0].WindowsRoot}'");
        IsDriveRoot(harness.Boot.Calls[0].EspRoot).ShouldBeTrue($"bcd esp root '{harness.Boot.Calls[0].EspRoot}'");

        harness.Recovery.Calls.Count.ShouldBe(1, "ConfigureAsync is called once");
        IsDriveRoot(harness.Recovery.Calls[0].WindowsRoot).ShouldBeTrue($"winre windows root '{harness.Recovery.Calls[0].WindowsRoot}'");
        IsDriveRoot(harness.Recovery.Calls[0].RecoveryRoot).ShouldBeTrue($"winre recovery root '{harness.Recovery.Calls[0].RecoveryRoot}'");

        // Not checked here: the sleep block is released. PowerManager has no public state to read, so the check is skipped.
        // Cleanup: the harness disposes its container (which closes the log), the drive letters above were removed in their
        // using blocks, and the fixture deletes the VHDX files and the folder.
    }

    private static void AssertTargetLayoutMatchesPlan(ClonePlan plan, Disk targetDisk, Disk sourceBefore)
    {
        targetDisk.PartitionStyle.ShouldBe(PartitionStyle.Gpt);
        targetDisk.Size.Bytes.ShouldBe(TargetSize.Bytes);
        targetDisk.Id.DiskGuid.ShouldBe(plan.NewDiskGuid, "the target disk GUID must be the planned one");
        targetDisk.Id.DiskGuid.ShouldNotBe(sourceBefore.Id.DiskGuid, "the disk GUID must differ from the source");
        targetDisk.Partitions.Count.ShouldBe(plan.Mappings.Count);

        foreach (PartitionMapping mapping in plan.Mappings)
        {
            Partition partition = targetDisk.Partitions
                .SingleOrDefault(candidate => candidate.UniqueId == mapping.NewUniqueId)
                .ShouldNotBeNull($"the target must hold partition {mapping.NewUniqueId.Value:D}");

            partition.UniqueId.ShouldNotBe(mapping.Source.UniqueId, "partition GUIDs must differ from the source");
            partition.Type.ShouldBe(mapping.Source.Type, $"type of source partition {mapping.Source.Number}");
            partition.Extent.Offset.ShouldBe(mapping.TargetExtent.Offset, $"offset of source partition {mapping.Source.Number}");
            partition.Extent.Length.ShouldBe(mapping.TargetExtent.Length, $"length of source partition {mapping.Source.Number}");
        }
    }

    private static void AssertRecoveryIsAtTheEnd(Disk targetDisk)
    {
        Partition recovery = targetDisk.Partitions.Single(partition => partition.Role == PartitionRole.Recovery);

        targetDisk.Partitions[^1].Number.ShouldBe(recovery.Number, "Recovery must be the last partition");

        // The layout puts Recovery against the end of the usable area: 16 KiB of backup GPT, one sector, and at most 1 MiB of alignment.
        long tailBytes = targetDisk.Size.Bytes - recovery.Extent.End;
        tailBytes.ShouldBeLessThan(2 * Mib, "Recovery must end at the end of the usable area");
        tailBytes.ShouldBeGreaterThanOrEqualTo(GptBackupBytes, "Recovery must leave room for the backup GPT");
    }

    private static async Task<Disk> ReadDisk(CloneHarness harness, int diskNumber, CancellationToken ct)
    {
        // The real reader, not the fixture decorator.
        DiskInventory inventory = await harness.RealReader.ReadAsync(ct);
        return inventory.Disks
            .SingleOrDefault(disk => disk.Id.Number == diskNumber)
            .ShouldNotBeNull($"the real inventory must list disk {diskNumber}");
    }

    /// <summary>GPT facts that a clone must not change: disk GUID, then number, offset, length, type and GUID of each partition.</summary>
    private static string LayoutSignature(Disk disk)
    {
        var lines = new List<string> { $"disk|{disk.Id.DiskGuid:D}|{disk.Size.Bytes}|{disk.LogicalSectorSize.Bytes}" };
        lines.AddRange(disk.Partitions.Select(partition =>
            $"{partition.Number}|{partition.Extent.Offset}|{partition.Extent.Length}|{partition.Type.Value:D}|{partition.UniqueId.Value:D}"));
        return string.Join("\n", lines);
    }

    private static Dictionary<string, string> ManifestOfPartition(
        VirtualDiskStorageOps ops,
        int diskNumber,
        int partitionNumber,
        IEnumerable<string> relativePaths)
    {
        using TemporaryDriveLetter letter = TemporaryDriveLetter.Acquire(ops, diskNumber, partitionNumber);
        return VolumeFiles.Sha256Manifest(letter.Root, relativePaths);
    }

    private static void AssertSameManifest(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        string label)
    {
        actual.Count.ShouldBe(expected.Count, $"the file count of {label}");
        foreach ((string path, string hash) in expected)
        {
            actual.ContainsKey(path).ShouldBeTrue($"{path} must exist in {label}");
            actual[path].ShouldBe(hash, $"SHA-256 of {path} in {label}");
        }
    }

    private static bool IsDriveRoot(string root) =>
        root.Length == 3 && char.IsAsciiLetter(root[0]) && root[1] == ':' && root[2] == '\\';

    /// <summary>Reads the DiskOperation events of a CLEF log: operation name and result.</summary>
    private static List<(string Operation, string Result)> ReadDiskOperations(string logFile)
    {
        File.Exists(logFile).ShouldBeTrue($"the execution log must exist: {logFile}");

        var operations = new List<(string Operation, string Result)>();
        foreach (string line in VolumeFiles.ReadLines(logFile))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("Event", out JsonElement eventName) && eventName.GetString() == "DiskOperation")
            {
                operations.Add((
                    root.GetProperty("Operation").GetString() ?? string.Empty,
                    root.GetProperty("Result").GetString() ?? string.Empty));
            }
        }

        return operations;
    }
}
