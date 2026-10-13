using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Security.Cryptography;
using Shouldly;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.IntegrationTests.Fixtures;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinCopy.Infrastructure.IntegrationTests.Spikes;

/// <summary>
/// Spike S3 (task T025, research R9). Clones an NTFS VHDX protected with BitLocker through a VSS snapshot, and checks that
/// the clone mounts as plain NTFS. Works only on VHDX files created by <see cref="VirtualDiskFixture"/>, and every
/// Storage or BitLocker command goes through <see cref="VirtualDiskStorageOps"/>. Needs an elevated process:
/// <c>dotnet test tests/WinCopy.Infrastructure.IntegrationTests --filter "Spike=S3" --logger "console;verbosity=detailed"</c>
/// </summary>
[Trait("Category", "Integration")]
[Trait("Spike", "S3")]
public sealed class BitLockerSnapshotSpike
{
    private const string FolderName = "spike";
    private const int FileCount = 20;
    private const int FileBytes = 1024 * 1024;
    private const int HashBlockBytes = 32;
    private const int BootRegionBytes = 4096;
    private const int BackupSectorBytes = SpikeVolumeCopy.SectorBytes;

    private static readonly ByteSize DiskSize = ByteSize.FromMiB(512);
    private static readonly TimeSpan EncryptionTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EncryptionPollInterval = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task SnapshotOfBitLockerVolumeYieldsPlainNtfsClone()
    {
        var result = new SpikeResult();
        try
        {
            await using var fixture = new VirtualDiskFixture();
            AttachedVirtualDisk source = await fixture.CreateAndAttachAsync(DiskSize);
            AttachedVirtualDisk target = await fixture.CreateAndAttachAsync(DiskSize);

            await CloneAsync(VirtualDiskStorageOps.Default, source, target, result, TestContext.Current.CancellationToken);
        }
        finally
        {
            Log(result.Summary);
        }

        result.CleanupErrors.ShouldBeEmpty();
    }

    private static async Task CloneAsync(
        VirtualDiskStorageOps ops,
        AttachedVirtualDisk source,
        AttachedVirtualDisk target,
        SpikeResult result,
        CancellationToken ct)
    {
        PartitionInfo? sourcePartition = null;
        PartitionInfo? targetPartition = null;
        char? sourceLetter = null;
        char? targetLetter = null;
        string? shadowId = null;

        try
        {
            // Step 1. Source VHDX: GPT, one NTFS partition with a letter, test files, BitLocker, wait for 100 %.
            ops.InitializeGpt(source.DiskNumber);
            sourcePartition = ops.CreateMaximumPartitionWithLetter(source.DiskNumber);
            char sourceDrive = RequireLetter(sourcePartition);
            sourceLetter = sourceDrive;
            ops.FormatNtfs(source.DiskNumber, sourceDrive);

            Dictionary<string, string> manifest = WriteTestFiles(sourceDrive);
            Log($"Source: disk {source.DiskNumber}, partition {sourcePartition.PartitionNumber} at {sourceDrive}:, " +
                $"{sourcePartition.SizeBytes} bytes, {manifest.Count} files written.");

            string password = $"Wc-{Guid.NewGuid():N}";
            ops.EnableBitLockerWithPassword(source.DiskNumber, sourceDrive, password);

            BitLockerStatus encrypted = await WaitForFullEncryptionAsync(ops, source.DiskNumber, sourceDrive, ct);
            encrypted.EncryptionPercentage.ShouldBe(100);
            (encrypted.ProtectionStatus == "On" || encrypted.VolumeStatus == "FullyEncrypted").ShouldBeTrue(
                $"Expected protection On or FullyEncrypted, got volume '{encrypted.VolumeStatus}' and protection '{encrypted.ProtectionStatus}'.");

            // Step 2. VSS snapshot of the unlocked source volume, created through WMI.
            ShadowCopy shadow = CreateShadowCopy(sourceDrive);
            shadowId = shadow.Id;
            Log($"Shadow copy {shadow.Id} at {shadow.DeviceObject}.");

            // Step 3. Read the NTFS geometry and the used clusters from the snapshot, read-only.
            using SafeDeviceHandle snapshot = SpikeVolumeCopy.OpenDevice(
                shadow.DeviceObject,
                Kernel32.GENERIC_READ,
                Kernel32.FILE_FLAG_NO_BUFFERING);

            NTFS_VOLUME_DATA_BUFFER ntfs = SpikeVolumeCopy.ReadNtfsVolumeData(snapshot);
            List<ClusterRun> runs = SpikeVolumeCopy.ReadUsedRuns(snapshot, ntfs.TotalClusters, out long usedClusters);
            double usedFraction = ntfs.TotalClusters == 0 ? 0 : usedClusters / (double)ntfs.TotalClusters;
            Log($"NTFS: {ntfs.BytesPerSector} bytes per sector, {ntfs.BytesPerCluster} bytes per cluster, " +
                $"{ntfs.TotalClusters} clusters, {usedClusters} used ({usedFraction.ToString("P1", CultureInfo.InvariantCulture)}), " +
                $"{runs.Count} used runs.");

            // Step 4. Target VHDX: GPT and a partition of the same size, without a drive letter.
            ops.InitializeGpt(target.DiskNumber);
            targetPartition = ops.CreatePartitionWithoutLetter(target.DiskNumber, sourcePartition.SizeBytes);
            if (targetPartition.DriveLetter is char assignedByWindows)
            {
                targetLetter = assignedByWindows;
                Log($"Windows gave the target partition the letter {assignedByWindows}:; it is removed at cleanup.");
            }

            long volumeBytes = ntfs.NumberSectors * ntfs.BytesPerSector;
            if (volumeBytes > targetPartition.SizeBytes)
            {
                throw new InvalidOperationException(
                    $"The NTFS volume ({volumeBytes} bytes) is larger than the target partition ({targetPartition.SizeBytes} bytes).");
            }

            using AlignedBuffer buffer = new(SpikeVolumeCopy.CopyChunkBytes, (nuint)SpikeVolumeCopy.AlignmentBytes);
            CopyToTarget(ops, target.DiskNumber, targetPartition, snapshot, ntfs, runs, buffer);

            // Step 5. Mount the target and check that it is plain NTFS and every file is intact.
            PartitionInfo mounted = ops.AssignDriveLetter(target.DiskNumber, targetPartition.PartitionNumber);
            char targetDrive = RequireLetter(mounted);
            targetLetter = targetDrive;

            BitLockerStatus plain = ops.GetBitLockerStatus(target.DiskNumber, targetDrive);
            bool mountedAsNtfs = IsMountedNtfs(ops, target.DiskNumber, targetDrive);
            int intact = VerifyTestFiles(targetDrive, manifest);

            Log($"Target: {targetDrive}: NTFS={mountedAsNtfs}, volume '{plain.VolumeStatus}', protection '{plain.ProtectionStatus}'.");
            Log($"Files: {intact}/{manifest.Count} match the manifest.");

            result.PlainNtfs = mountedAsNtfs && plain.VolumeStatus == "FullyDecrypted" && plain.ProtectionStatus == "Off";
            result.FilesIntact = intact;

            mountedAsNtfs.ShouldBeTrue("The target volume does not mount as NTFS.");
            plain.VolumeStatus.ShouldBe("FullyDecrypted");
            plain.ProtectionStatus.ShouldBe("Off");
            intact.ShouldBe(FileCount);
        }
        finally
        {
            // Cleanup: the shadow copy first, then the drive letters. The fixture detaches and deletes the VHDX files afterwards.
            if (shadowId is not null)
            {
                string id = shadowId;
                TryCleanUp(result, $"delete shadow copy {id}", () => DeleteShadowCopy(id));
            }

            if (targetLetter is char tl && targetPartition is PartitionInfo tp)
            {
                int partitionNumber = tp.PartitionNumber;
                TryCleanUp(result, $"remove drive letter {tl}:", () =>
                    ops.RemoveDriveLetter(target.DiskNumber, partitionNumber, tl));
            }

            if (sourceLetter is char sl && sourcePartition is PartitionInfo sp)
            {
                int partitionNumber = sp.PartitionNumber;
                TryCleanUp(result, $"remove drive letter {sl}:", () =>
                    ops.RemoveDriveLetter(source.DiskNumber, partitionNumber, sl));
            }
        }
    }

    /// <summary>
    /// Copies the boot sector, the used clusters, the region after the last full cluster and the backup boot sector from the
    /// snapshot to the target partition. The target disk is opened only through <see cref="DiskWriteGuard"/>. Before the
    /// write, the target volume, if Windows mounted one, is locked and dismounted by its volume GUID path.
    /// </summary>
    private static void CopyToTarget(
        VirtualDiskStorageOps ops,
        int targetDiskNumber,
        PartitionInfo targetPartition,
        SafeDeviceHandle snapshot,
        NTFS_VOLUME_DATA_BUFFER ntfs,
        List<ClusterRun> runs,
        AlignedBuffer buffer)
    {
        ops.AssertSafeTarget(targetDiskNumber);

        long partitionOffset = targetPartition.OffsetBytes;
        long clusterBytes = ntfs.BytesPerCluster;
        long volumeBytes = ntfs.NumberSectors * ntfs.BytesPerSector;
        long tailStart = ntfs.TotalClusters * clusterBytes;

        string? volumePath = VolumeGuidPath(targetPartition);
        SafeDeviceHandle? volumeLock = null;
        if (volumePath is null)
        {
            Log("Windows has not mounted a volume on the target partition; no lock or dismount is needed.");
        }
        else
        {
            ops.AssertVolumeOnDisk(volumePath, targetDiskNumber);
            volumeLock = SpikeVolumeCopy.LockAndDismount(volumePath);
            Log($"Locked and dismounted the target volume by its GUID path {volumePath}.");
        }

        try
        {
            using SafeDeviceHandle physical = DiskWriteGuard.Default.OpenDiskForWrite(targetDiskNumber);
            long copiedClusterBytes = 0;

            // Boot sector.
            SpikeVolumeCopy.CopyRange(snapshot, 0, physical, partitionOffset, BootRegionBytes, buffer);

            // Used clusters, run by run.
            foreach (ClusterRun run in runs)
            {
                long sourceOffset = run.StartLcn * clusterBytes;
                long length = run.ClusterCount * clusterBytes;
                SpikeVolumeCopy.CopyRange(snapshot, sourceOffset, physical, partitionOffset + sourceOffset, length, buffer);
                copiedClusterBytes += length;
            }

            // Tail after the last full cluster, which is outside the cluster bitmap.
            if (volumeBytes > tailStart)
            {
                SpikeVolumeCopy.CopyRange(snapshot, tailStart, physical, partitionOffset + tailStart, volumeBytes - tailStart, buffer);
            }

            // NTFS backup boot sector, in the last sector of the volume.
            long backupOffset = volumeBytes - BackupSectorBytes;
            SpikeVolumeCopy.CopyRange(snapshot, backupOffset, physical, partitionOffset + backupOffset, BackupSectorBytes, buffer);

            Log($"Copied {copiedClusterBytes / (1024 * 1024)} MiB of used clusters in {runs.Count} runs, " +
                $"plus the boot sector, the {volumeBytes - tailStart} byte tail and the backup boot sector.");
        }
        finally
        {
            if (volumeLock is not null)
            {
                SpikeVolumeCopy.UnlockAndClose(volumeLock);
            }
        }
    }

    private static async Task<BitLockerStatus> WaitForFullEncryptionAsync(
        VirtualDiskStorageOps ops,
        int diskNumber,
        char letter,
        CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            BitLockerStatus status = ops.GetBitLockerStatus(diskNumber, letter);
            Log($"BitLocker {letter}: volume '{status.VolumeStatus}', protection '{status.ProtectionStatus}', " +
                $"{status.EncryptionPercentage} % encrypted.");

            if (status.EncryptionPercentage == 100)
            {
                return status;
            }

            if (clock.Elapsed >= EncryptionTimeout)
            {
                throw new TimeoutException($"BitLocker on {letter}: did not reach 100 % in {EncryptionTimeout}.");
            }

            await Task.Delay(EncryptionPollInterval, ct);
        }
    }

    private static ShadowCopy CreateShadowCopy(char letter)
    {
        using var shadowClass = new ManagementClass(@"root\cimv2:Win32_ShadowCopy");
        using ManagementBaseObject inParameters = shadowClass.GetMethodParameters("Create");
        inParameters["Volume"] = $@"{letter}:\";
        inParameters["Context"] = "ClientAccessible";

        using ManagementBaseObject outParameters = shadowClass.InvokeMethod("Create", inParameters, null);
        uint returnValue = Convert.ToUInt32(outParameters["ReturnValue"], CultureInfo.InvariantCulture);
        if (returnValue != 0)
        {
            throw new InvalidOperationException($"Win32_ShadowCopy.Create returned {returnValue}.");
        }

        string id = outParameters["ShadowID"] as string
            ?? throw new InvalidOperationException("Win32_ShadowCopy.Create did not return a ShadowID.");
        return new ShadowCopy(id, ReadDeviceObject(id));
    }

    private static string ReadDeviceObject(string shadowId)
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\cimv2",
            $"SELECT * FROM Win32_ShadowCopy WHERE ID = '{shadowId}'");
        using ManagementObjectCollection shadows = searcher.Get();

        foreach (ManagementObject shadow in shadows)
        {
            using (shadow)
            {
                return shadow["DeviceObject"] as string
                    ?? throw new InvalidOperationException($"Shadow copy {shadowId} has no DeviceObject.");
            }
        }

        throw new InvalidOperationException($"Shadow copy {shadowId} was not found.");
    }

    private static void DeleteShadowCopy(string shadowId)
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\cimv2",
            $"SELECT * FROM Win32_ShadowCopy WHERE ID = '{shadowId}'");
        using ManagementObjectCollection shadows = searcher.Get();

        int deleted = 0;
        foreach (ManagementObject shadow in shadows)
        {
            using (shadow)
            {
                shadow.Delete();
            }

            deleted++;
        }

        if (deleted == 0)
        {
            throw new InvalidOperationException($"Shadow copy {shadowId} was not found, so it could not be deleted.");
        }
    }

    private static bool IsMountedNtfs(VirtualDiskStorageOps ops, int diskNumber, char letter)
    {
        ops.AssertLetterOnDisk(letter, diskNumber);

        using SafeDeviceHandle volume = SpikeVolumeCopy.OpenDevice(@"\\.\" + letter + ":", 0, 0);
        try
        {
            SpikeVolumeCopy.ReadNtfsVolumeData(volume);
            return true;
        }
        catch (Win32Exception ex)
        {
            Log($"{letter}: does not answer NTFS queries: {ex.Message}");
            return false;
        }
    }

    private static Dictionary<string, string> WriteTestFiles(char letter)
    {
        string folder = Path.Combine($"{letter}:\\", FolderName);
        Directory.CreateDirectory(folder);

        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < FileCount; index++)
        {
            string name = $"file-{index:D2}.bin";
            byte[] content = KnownContent(index);
            File.WriteAllBytes(Path.Combine(folder, name), content);
            manifest[name] = Convert.ToHexString(SHA256.HashData(content));
        }

        return manifest;
    }

    private static int VerifyTestFiles(char letter, IReadOnlyDictionary<string, string> manifest)
    {
        string folder = Path.Combine($"{letter}:\\", FolderName);
        int intact = 0;

        foreach (KeyValuePair<string, string> entry in manifest)
        {
            string path = Path.Combine(folder, entry.Key);
            if (!File.Exists(path))
            {
                Log($"Missing on target: {entry.Key}");
                continue;
            }

            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            if (string.Equals(actual, entry.Value, StringComparison.Ordinal))
            {
                intact++;
            }
            else
            {
                Log($"Hash mismatch: {entry.Key}");
            }
        }

        return intact;
    }

    /// <summary>Deterministic, non-compressible content: a SHA-256 chain seeded with the file index.</summary>
    private static byte[] KnownContent(int index)
    {
        byte[] content = new byte[FileBytes];
        Span<byte> seed = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(seed, index);

        for (int block = 0; block * HashBlockBytes < FileBytes; block++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(seed[4..], block);
            SHA256.HashData(seed).CopyTo(content.AsSpan(block * HashBlockBytes));
        }

        return content;
    }

    private static char RequireLetter(PartitionInfo partition) =>
        partition.DriveLetter ?? throw new InvalidOperationException(
            $"Partition {partition.PartitionNumber} on disk {partition.DiskNumber} has no drive letter.");

    private static string? VolumeGuidPath(PartitionInfo partition) =>
        partition.AccessPaths
            .Where(path => path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
            .Select(path => path.TrimEnd('\\'))
            .FirstOrDefault();

    private static void Log(string message)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(message);
        Console.WriteLine(message);
    }

    /// <summary>Runs one cleanup step. A failure is recorded and logged, so the other cleanup steps still run.</summary>
    private static void TryCleanUp(SpikeResult result, string description, Action action)
    {
        try
        {
            action();
            Log($"Cleanup done: {description}.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception
                                       or ManagementException or TimeoutException)
        {
            result.CleanupErrors.Add($"{description}: {ex.Message}");
            Log($"CLEANUP FAILED: {description}: {ex.Message}");
        }
    }

    private sealed record ShadowCopy(string Id, string DeviceObject);

    /// <summary>Outcome of one run. <see cref="Summary"/> is the line that answers the spike.</summary>
    private sealed class SpikeResult
    {
        public bool PlainNtfs { get; set; }

        public int FilesIntact { get; set; }

        public List<string> CleanupErrors { get; } = [];

        public string Summary =>
            $"S3 RESULT: BitLocker source cloned via snapshot = plain NTFS {(PlainNtfs ? "YES" : "NO")}; " +
            $"files intact {FilesIntact}/{FileCount}";
    }
}
