using System.ComponentModel;
using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;
using WinCopy.Infrastructure.Windows.Verification;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Verification;

// The layout, the volumes and the mount are all fakes. No test opens a disk, a volume or a mount point.
public sealed class StructuralVerifierTests
{
    [Fact]
    public async Task AMatchingCloneVerifiesAndReleasesTheEspMount()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
        harness.Layout.ReadDisks.ShouldBe([harness.Plan.Target.Number]);
        harness.Ntfs.Probed.Count.ShouldBe(2);
        harness.Mounter.Mounted.ShouldBe([harness.Plan.Mappings[VerificationPlan.EfiIndex].NewUniqueId]);
        harness.Files.Checked.ShouldBe([VerifierHarness.BcdPath]);
        harness.Mounter.Released.Count.ShouldBe(1);
    }

    [Fact]
    public async Task NtfsVolumesAreProbedByTheirNewIdsAndNotTheFatOrMsrPartitions()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        var plan = harness.Plan;

        await harness.Verifier.VerifyAsync(plan, CancellationToken.None);

        harness.Ntfs.Probed.ShouldBe(
        [
            FakeVolumeResolver.VolumePathFor(plan.Mappings[VerificationPlan.WindowsIndex].NewUniqueId),
            FakeVolumeResolver.VolumePathFor(plan.Mappings[VerificationPlan.RecoveryIndex].NewUniqueId),
        ]);
    }

    [Fact]
    public async Task ANullPlanThrows()
    {
        var verifier = new VerifierHarness(VerificationPlan.Create()).Verifier;

        await Should.ThrowAsync<ArgumentNullException>(() => verifier.VerifyAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ADifferentDiskGuidIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        DriveLayout good = VerificationPlan.MatchingLayout(harness.Plan);
        harness.Layout.Layout = good with { DiskId = Guid.Parse("99999999-9999-9999-9999-999999999999") };

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("disk GUID");
    }

    [Fact]
    public async Task AMissingPartitionIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        DriveLayout good = VerificationPlan.MatchingLayout(harness.Plan);
        harness.Layout.Layout = good with { Partitions = good.Partitions.Where((_, i) => i != VerificationPlan.WindowsIndex).ToArray() };

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.Issues.Any(issue => issue.Contains("source partition 3") && issue.Contains("is missing")).ShouldBeTrue();
    }

    [Fact]
    public async Task AWrongOffsetIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        DriveLayout good = VerificationPlan.MatchingLayout(harness.Plan);
        harness.Layout.Layout = WithPartition(good, VerificationPlan.WindowsIndex, p => p with { StartingOffset = p.StartingOffset + (1024 * 1024) });

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("starts at byte");
    }

    [Fact]
    public async Task AWrongTypeIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        DriveLayout good = VerificationPlan.MatchingLayout(harness.Plan);
        harness.Layout.Layout = WithPartition(good, VerificationPlan.MsrIndex, p => p with { Type = Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7") });

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("has type");
    }

    [Fact]
    public async Task AWrongLengthIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        DriveLayout good = VerificationPlan.MatchingLayout(harness.Plan);
        harness.Layout.Layout = WithPartition(good, VerificationPlan.RecoveryIndex, p => p with { Length = p.Length - 4096 });

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("bytes long");
    }

    [Fact]
    public async Task ANonGptDiskIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        harness.Layout.Layout = new DriveLayout(DiskPartitionStyle.Mbr, Guid.Empty, []);

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("does not use a GPT");
    }

    [Fact]
    public async Task AnExtraPartitionIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        DriveLayout good = VerificationPlan.MatchingLayout(harness.Plan);
        var extra = new DriveLayoutPartition(Guid.Parse("88888888-8888-8888-8888-888888888888"), Guid.Empty, 0, 4096);
        harness.Layout.Layout = good with { Partitions = [.. good.Partitions, extra] };

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("partitions");
    }

    [Fact]
    public async Task AnUnreadableLayoutIsAnIssueAndTheOtherChecksStillRun()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        harness.Layout.Failure = new Win32Exception(5, "Access is denied.");

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.Issues.Any(issue => issue.Contains("could not be read")).ShouldBeTrue();
        harness.Ntfs.Probed.Count.ShouldBe(2);
        harness.Mounter.Released.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnNtfsProbeFailureIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        string windowsVolume = FakeVolumeResolver.VolumePathFor(harness.Plan.Mappings[VerificationPlan.WindowsIndex].NewUniqueId);
        harness.Ntfs.Unreadable.Add(windowsVolume);

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("source partition 3");
        result.Issues[0].ShouldContain("not readable as NTFS");
    }

    [Fact]
    public async Task AVolumeThatCannotBeResolvedIsAnIssue()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        harness.Resolver.Missing.Add(harness.Plan.Mappings[VerificationPlan.RecoveryIndex].NewUniqueId);

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("source partition 4");
        result.Issues[0].ShouldContain("not readable as NTFS");
    }

    [Fact]
    public async Task AMissingBcdStoreIsAnIssueAndTheMountIsReleased()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        harness.Files.Existing.Clear();

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain(@"EFI\Microsoft\Boot\BCD");
        harness.Mounter.Released.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnExceptionWhileCheckingTheBcdBecomesAnIssueAndTheMountIsStillReleased()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        harness.Files.Failure = new IOException("The device is not ready.");

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("could not be checked");
        harness.Mounter.Released.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AFailedEspMountIsAnIssueAndNothingIsReleased()
    {
        var harness = new VerifierHarness(VerificationPlan.Create());
        harness.Mounter.Failure = new PlatformException(PlatformErrorCode.MountFailed, "No free drive letter.");

        VerificationResult result = await harness.Verifier.VerifyAsync(harness.Plan, CancellationToken.None);

        result.Passed.ShouldBeFalse();
        result.Issues.Count.ShouldBe(1);
        result.Issues[0].ShouldContain("could not be checked");
        harness.Mounter.Released.ShouldBeEmpty();
    }

    private static DriveLayout WithPartition(
        DriveLayout layout,
        int index,
        Func<DriveLayoutPartition, DriveLayoutPartition> change)
    {
        var partitions = layout.Partitions.ToArray();
        partitions[index] = change(partitions[index]);

        return layout with { Partitions = partitions };
    }
}
