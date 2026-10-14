using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.Platform;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Platform;

// The native power calls are replaced by a fake. No test changes the power state of the machine.
public sealed class PowerManagerTests
{
    private const EXECUTION_STATE Keep = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED;
    private const EXECUTION_STATE Clear = EXECUTION_STATE.ES_CONTINUOUS;

    [Fact]
    public void PreventSleepSetsContinuousAndSystemRequired()
    {
        var native = new FakePowerNative();
        using var manager = new PowerManager(native);

        using (manager.PreventSleep())
        {
            native.Calls.Count.ShouldBe(1);
            native.Calls[0].State.ShouldBe(Keep);
        }
    }

    [Fact]
    public void NestedCallsShareOneStateAndTheLastReleaseResetsIt()
    {
        var native = new FakePowerNative();
        using var manager = new PowerManager(native);

        IDisposable first = manager.PreventSleep();
        IDisposable second = manager.PreventSleep();
        native.Calls.Count.ShouldBe(1);

        first.Dispose();
        native.Calls.Count.ShouldBe(1);

        second.Dispose();
        native.Calls.Count.ShouldBe(2);
        native.Calls[1].State.ShouldBe(Clear);
    }

    [Fact]
    public void DisposingTheSameLeaseTwiceReleasesItOnlyOnce()
    {
        var native = new FakePowerNative();
        using var manager = new PowerManager(native);

        IDisposable first = manager.PreventSleep();
        IDisposable second = manager.PreventSleep();

        first.Dispose();
        first.Dispose();
        native.Calls.Count.ShouldBe(1);

        second.Dispose();
        native.Calls.Count.ShouldBe(2);
    }

    [Fact]
    public void SetAndResetRunOnTheSameDedicatedThreadNotTheCallersThread()
    {
        var native = new FakePowerNative();
        using var manager = new PowerManager(native);

        IDisposable first = manager.PreventSleep();
        IDisposable second = manager.PreventSleep();
        first.Dispose();
        second.Dispose();

        int[] threads = native.Calls.Select(call => call.ThreadId).Distinct().ToArray();
        threads.Length.ShouldBe(1);
        threads[0].ShouldNotBe(Environment.CurrentManagedThreadId);
    }

    [Fact]
    public void FailedSetThrowsAndHoldsNothing()
    {
        var native = new FakePowerNative { SetSucceeds = false };
        using var manager = new PowerManager(native);

        Should.Throw<PlatformException>(() => manager.PreventSleep());

        native.SetSucceeds = true;
        using (manager.PreventSleep())
        {
        }

        // The failed attempt held nothing, so the next call sets the state again and its release resets it once.
        native.Calls.Select(call => call.State).ToArray().ShouldBe([Keep, Keep, Clear]);
    }

    [Fact]
    public void DisposingTheManagerClearsSleepPreventionStillHeld()
    {
        var native = new FakePowerNative();
        var manager = new PowerManager(native);
        IDisposable lease = manager.PreventSleep();

        manager.Dispose();
        lease.Dispose();

        native.Calls.Count.ShouldBe(2);
        native.Calls[1].State.ShouldBe(Clear);
    }

    [Fact]
    public void PreventSleepAfterDisposeThrows()
    {
        var manager = new PowerManager(new FakePowerNative());
        manager.Dispose();

        Should.Throw<ObjectDisposedException>(() => manager.PreventSleep());
    }

    [Theory]
    [InlineData(0, 73, true, 73)]
    [InlineData(1, 100, false, 100)]
    [InlineData(0, 255, true, null)]
    [InlineData(255, 255, false, null)]
    public async Task MapsTheSystemPowerStatus(int acLine, int percent, bool expectedOnBattery, int? expectedPercent)
    {
        var native = new FakePowerNative
        {
            Status = new SYSTEM_POWER_STATUS { ACLineStatus = (byte)acLine, BatteryLifePercent = (byte)percent },
        };
        using var manager = new PowerManager(native);

        PowerStatus status = await manager.GetStatusAsync(CancellationToken.None);

        status.ShouldBe(new PowerStatus(expectedOnBattery, expectedPercent));
    }

    [Fact]
    public async Task FailedStatusReadThrows()
    {
        var native = new FakePowerNative { StatusSucceeds = false };
        using var manager = new PowerManager(native);

        var ex = await Should.ThrowAsync<PlatformException>(() => manager.GetStatusAsync(CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.Unknown);
    }

    private sealed class FakePowerNative : IPowerNative
    {
        public List<(int ThreadId, EXECUTION_STATE State)> Calls { get; } = [];

        public bool SetSucceeds { get; set; } = true;

        public SYSTEM_POWER_STATUS Status { get; init; }

        public bool StatusSucceeds { get; init; } = true;

        public bool TrySetThreadExecutionState(EXECUTION_STATE state)
        {
            Calls.Add((Environment.CurrentManagedThreadId, state));
            return SetSucceeds;
        }

        public bool TryGetSystemPowerStatus(out SYSTEM_POWER_STATUS status)
        {
            status = Status;
            return StatusSucceeds;
        }
    }
}
