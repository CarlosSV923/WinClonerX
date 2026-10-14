using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.Logging;
using WinCopy.Infrastructure.Windows.RawIo;
using WinCopy.Infrastructure.Windows.Tests.Fakes;
using WinCopy.Infrastructure.Windows.Tests.Logging;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.RawIo;

// The guard is real, with a fake probe and a recording opener. The device is a fake, so no disk is opened.
public sealed class TargetDiskFactoryTests
{
    private const int DiskNumber = 7;
    private static readonly Guid TargetGuid = new("6A1F2B3C-4D5E-4F60-8192-A3B4C5D6E7F8");

    [Theory]
    [InlineData(PlanStatus.Draft)]
    [InlineData(PlanStatus.Invalidated)]
    public void NonConfirmedPlanIsRejectedBeforeTheGuardIsCalled(PlanStatus status)
    {
        var probe = new StubIdentityProbe(Candidates.Plain(DiskNumber));
        var opener = new RecordingOpener();
        TargetDiskFactory factory = CreateFactory(probe, opener, (_, _) => throw new InvalidOperationException("device must not be created"));
        ClonePlan plan = TestPlans.Create(status, new DiskId(DiskNumber, TargetGuid));

        Should.Throw<InvalidOperationException>(() => factory.Open(plan));

        probe.CallCount.ShouldBe(0);
        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfirmedPlanOpensThroughTheGuardAndReturnsTheTargetDisk()
    {
        var probe = new StubIdentityProbe(Candidates.Plain(DiskNumber));
        var opener = new RecordingOpener();
        var device = new FakeTargetDiskDevice(256 * Sizes.Mib, 512, TargetGuid);
        int createdFor = -1;
        TargetDiskFactory factory = CreateFactory(probe, opener, (_, number) =>
        {
            createdFor = number;
            return device;
        });
        ClonePlan plan = TestPlans.Create(PlanStatus.Confirmed, new DiskId(DiskNumber, TargetGuid));

        await using (ITargetDisk disk = factory.Open(plan))
        {
            disk.ShouldNotBeNull();
            probe.CallCount.ShouldBe(1);
            opener.Paths.ShouldBe([@"\\.\PhysicalDrive7"]);
            createdFor.ShouldBe(DiskNumber);
            device.DisposeCount.ShouldBe(0);
        }

        device.DisposeCount.ShouldBe(1, "disposing the target disk disposes its device");
    }

    [Fact]
    public void GptIdentityThatDiffersFromThePlanThrowsAndDisposesTheDevice()
    {
        var device = new FakeTargetDiskDevice(256 * Sizes.Mib, 512, Guid.NewGuid());
        TargetDiskFactory factory = CreateFactory(
            new StubIdentityProbe(Candidates.Plain(DiskNumber)),
            new RecordingOpener(),
            (_, _) => device);
        ClonePlan plan = TestPlans.Create(PlanStatus.Confirmed, new DiskId(DiskNumber, TargetGuid));

        PlatformException ex = Should.Throw<PlatformException>(() => factory.Open(plan));

        ex.Code.ShouldBe(PlatformErrorCode.DeviceNotFound);
        ex.TechnicalDetail.ShouldContain("target disk identity changed");
        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task RawTargetWithEmptyGuidIsAccepted()
    {
        var device = new FakeTargetDiskDevice(256 * Sizes.Mib, 512, gptDiskId: null);
        TargetDiskFactory factory = CreateFactory(
            new StubIdentityProbe(Candidates.Plain(DiskNumber)),
            new RecordingOpener(),
            (_, _) => device);
        ClonePlan plan = TestPlans.Create(PlanStatus.Confirmed, new DiskId(DiskNumber, Guid.Empty));

        await using (ITargetDisk disk = factory.Open(plan))
        {
            disk.ShouldNotBeNull();
            device.DisposeCount.ShouldBe(0);
        }
    }

    [Fact]
    public void RawTargetWithANonEmptyGuidIsRejected()
    {
        var device = new FakeTargetDiskDevice(256 * Sizes.Mib, 512, gptDiskId: null);
        TargetDiskFactory factory = CreateFactory(
            new StubIdentityProbe(Candidates.Plain(DiskNumber)),
            new RecordingOpener(),
            (_, _) => device);
        ClonePlan plan = TestPlans.Create(PlanStatus.Confirmed, new DiskId(DiskNumber, TargetGuid));

        PlatformException ex = Should.Throw<PlatformException>(() => factory.Open(plan));

        ex.Code.ShouldBe(PlatformErrorCode.DeviceNotFound);
        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void GptTargetWithAnEmptyPlanGuidIsRejected()
    {
        var device = new FakeTargetDiskDevice(256 * Sizes.Mib, 512, TargetGuid);
        TargetDiskFactory factory = CreateFactory(
            new StubIdentityProbe(Candidates.Plain(DiskNumber)),
            new RecordingOpener(),
            (_, _) => device);
        ClonePlan plan = TestPlans.Create(PlanStatus.Confirmed, new DiskId(DiskNumber, Guid.Empty));

        PlatformException ex = Should.Throw<PlatformException>(() => factory.Open(plan));

        ex.Code.ShouldBe(PlatformErrorCode.DeviceNotFound);
        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void GuardDenialPropagatesAndNoDeviceIsCreated()
    {
        var probe = new StubIdentityProbe(Candidates.Plain(DiskNumber, isSystem: true));
        var opener = new RecordingOpener();
        int created = 0;
        TargetDiskFactory factory = CreateFactory(probe, opener, (_, _) =>
        {
            created++;
            return new FakeTargetDiskDevice(256 * Sizes.Mib, 512, TargetGuid);
        });
        ClonePlan plan = TestPlans.Create(PlanStatus.Confirmed, new DiskId(DiskNumber, TargetGuid));

        Should.Throw<DiskWriteDeniedException>(() => factory.Open(plan));

        created.ShouldBe(0);
        opener.Paths.ShouldBeEmpty();
    }

    private static TargetDiskFactory CreateFactory(
        StubIdentityProbe probe,
        RecordingOpener opener,
        Func<SafeDeviceHandle, int, ITargetDiskDevice> createDevice)
    {
        var guard = new DiskWriteGuard(probe, opener);
        var recorder = new DiskOperationRecorder(new RecordingExecutionLog(), new ManualClock(DateTimeOffset.UnixEpoch));
        return new TargetDiskFactory(guard, recorder, createDevice);
    }
}
