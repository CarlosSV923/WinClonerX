using Shouldly;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.IntegrationTests.Fixtures;
using WinCopy.Infrastructure.IntegrationTests.Spikes;
using Xunit;

namespace WinCopy.Infrastructure.IntegrationTests.Cloning;

/// <summary>
/// Task T106. Full verification over a real VHDX clone. Needs an elevated process and a VM with virtual disks enabled.
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationDisks.Name)]
public sealed class FullVerificationTests
{
    private static readonly ByteSize SourceSize = ByteSize.FromGiB(2);
    private static readonly ByteSize TargetSize = ByteSize.FromGiB(3);

    [Fact]
    public async Task CleanClonePassesFullVerification()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var fixture = new VirtualDiskFixture();
        ReferenceDisk source = await ReferenceSystemDisk.CreateAsync(
            fixture, VirtualDiskStorageOps.Default, SourceSize, dataFiles: 4, includeDummyBcd: true);
        AttachedVirtualDisk target = await fixture.CreateAndAttachAsync(TargetSize);
        await using var harness = new CloneHarness(
            source,
            Path.Combine(fixture.RootFolder, "logs"),
            verificationMode: VerificationMode.StructuralAndFull);

        var clone = await harness.CloneAsync(source, target, ct);
        CloneResultReport report = clone.Report;

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        VerificationResult full = report.Full.ShouldNotBeNull("full verification must run");
        full.Passed.ShouldBeTrue(string.Join("; ", full.Issues));
    }

    [Fact]
    public async Task AlteredCopiedByteFailsFullVerificationAndLeavesTargetWithoutPartitionTable()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var fixture = new VirtualDiskFixture();
        ReferenceDisk source = await ReferenceSystemDisk.CreateAsync(
            fixture, VirtualDiskStorageOps.Default, SourceSize, dataFiles: 4, includeDummyBcd: true);
        AttachedVirtualDisk target = await fixture.CreateAndAttachAsync(TargetSize);
        await using var harness = new CloneHarness(
            source,
            Path.Combine(fixture.RootFolder, "logs"),
            verificationMode: VerificationMode.StructuralAndFull,
            tamperCopiedData: true);

        var clone = await harness.CloneAsync(source, target, ct);
        CloneResultReport report = clone.Report;

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.Failure!.Code.ShouldBe("VerificationFailed");
        VerificationResult full = report.Full.ShouldNotBeNull("failed full verification must be reported");
        full.Passed.ShouldBeFalse();
        full.DifferingBytes.ShouldBe(1);
        long? recordedOffset = harness.DataTamperingFactory!.FlippedOffset;
        recordedOffset.ShouldNotBeNull();
        long flippedOffset = recordedOffset.Value;
        full.FirstDifferenceOffsets.ShouldNotBeNull();
        full.FirstDifferenceOffsets!.ShouldContain(flippedOffset);
        full.FirstDifferenceOffsets[0].ShouldBe(flippedOffset);

        DiskInventory inventory = await harness.RealReader.ReadAsync(ct);
        Disk targetDisk = inventory.Disks.Single(disk => disk.Id.Number == target.DiskNumber);
        targetDisk.Partitions.ShouldBeEmpty();
        targetDisk.PartitionStyle.ShouldBe(PartitionStyle.Raw);
    }
}
