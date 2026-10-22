using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Shouldly;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// Every test here runs against fakes. No test touches a real device, disk or volume: the opener fake
// returns an unnamed kernel event object wrapped in a SafeDeviceHandle, so disposing it is harmless.
public sealed class DiskWriteGuardTests
{
    private const int NonSystemDisk = 7;

    [Fact]
    public void SystemDiskIsDeniedAndOpenerIsNotCalled()
    {
        var probe = new FakeProbe(Candidate(3, isSystem: true));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);

        var ex = Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(3));

        ex.DiskNumber.ShouldBe(3);
        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void DiskContainingRunningWindowsIsDeniedAndOpenerIsNotCalled()
    {
        var probe = new FakeProbe(Candidate(2, containsWindows: true));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);

        Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(2));

        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void ProbeThatThrowsDeniesAndOpenerIsNotCalled()
    {
        var probe = new FakeProbe(new InvalidOperationException("boom"));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);

        var ex = Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(NonSystemDisk));

        ex.Reason.ShouldContain("identity could not be verified");
        ex.InnerException.ShouldBeOfType<InvalidOperationException>();
        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void ProbeReturningAnotherDiskNumberDeniesAndOpenerIsNotCalled()
    {
        var probe = new FakeProbe(Candidate(5));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);

        Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(NonSystemDisk));

        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void NonSystemDiskWithoutRestrictionsOpensPhysicalDrivePath()
    {
        var probe = new FakeProbe(Candidate(NonSystemDisk));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);

        using SafeDeviceHandle handle = guard.OpenDiskForWrite(NonSystemDisk);

        opener.Paths.ShouldBe([@"\\.\PhysicalDrive7"]);
        handle.IsInvalid.ShouldBeFalse();
    }

    [Fact]
    public void RestrictionThatDeniesThrowsNamingItAndOpenerIsNotCalled()
    {
        var probe = new FakeProbe(Candidate(NonSystemDisk));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);
        guard.AddRestriction(new FakeRestriction("VirtualOnly", DiskWriteDecision.Deny("not a VHDX")));

        var ex = Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(NonSystemDisk));

        ex.Message.ShouldContain("VirtualOnly");
        ex.Reason.ShouldContain("not a VHDX");
        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void RestrictionCannotAllowTheSystemDisk()
    {
        var probe = new FakeProbe(Candidate(0, isSystem: true));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);
        guard.AddRestriction(new FakeRestriction("PermissiveAll", DiskWriteDecision.Allow));

        Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(0));

        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void RestrictionCannotAllowDiskContainingRunningWindows()
    {
        var probe = new FakeProbe(Candidate(0, containsWindows: true));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);
        guard.AddRestriction(new FakeRestriction("PermissiveAll", DiskWriteDecision.Allow));

        Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(0));

        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void RestrictionThatThrowsDeniesAndOpenerIsNotCalled()
    {
        var probe = new FakeProbe(Candidate(NonSystemDisk));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);
        guard.AddRestriction(new ThrowingRestriction("Broken"));

        var ex = Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(NonSystemDisk));

        ex.Message.ShouldContain("Broken");
        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void AllRestrictionsAreEvaluatedAndTheFirstDenyWins()
    {
        var probe = new FakeProbe(Candidate(NonSystemDisk));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);
        var first = new FakeRestriction("First", DiskWriteDecision.Allow);
        var second = new FakeRestriction("Second", DiskWriteDecision.Deny("second reason"));
        var third = new FakeRestriction("Third", DiskWriteDecision.Deny("third reason"));
        guard.AddRestriction(first);
        guard.AddRestriction(second);
        guard.AddRestriction(third);

        var ex = Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(NonSystemDisk));

        first.EvaluationCount.ShouldBe(1);
        second.EvaluationCount.ShouldBe(1);
        third.EvaluationCount.ShouldBe(1);
        ex.Message.ShouldContain("Second");
        ex.Reason.ShouldContain("second reason");
        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void RestrictionsThatAllowLetTheOpenProceed()
    {
        var probe = new FakeProbe(Candidate(NonSystemDisk));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);
        guard.AddRestriction(new FakeRestriction("A", DiskWriteDecision.Allow));
        guard.AddRestriction(new FakeRestriction("B", DiskWriteDecision.Allow));

        using SafeDeviceHandle handle = guard.OpenDiskForWrite(NonSystemDisk);

        opener.Paths.ShouldBe([@"\\.\PhysicalDrive7"]);
        handle.IsInvalid.ShouldBeFalse();
    }

    [Fact]
    public void InvalidHandleFromOpenerThrowsWin32Exception()
    {
        var probe = new FakeProbe(Candidate(NonSystemDisk));
        var opener = new FakeOpener { ReturnInvalidHandle = true };
        var guard = new DiskWriteGuard(probe, opener);

        Should.Throw<Win32Exception>(() => guard.OpenDiskForWrite(NonSystemDisk));
    }

    [Fact]
    public void NegativeDiskNumberThrowsArgumentOutOfRange()
    {
        var probe = new FakeProbe(Candidate(-1));
        var opener = new FakeOpener();
        var guard = new DiskWriteGuard(probe, opener);

        Should.Throw<ArgumentOutOfRangeException>(() => guard.OpenDiskForWrite(-1));

        probe.CallCount.ShouldBe(0);
        opener.Paths.ShouldBeEmpty();
    }

    [Fact]
    public void AddRestrictionIsThreadSafe()
    {
        var guard = new DiskWriteGuard(new FakeProbe(Candidate(NonSystemDisk)), new FakeOpener());
        var names = Enumerable.Range(0, 100).Select(i => $"restriction-{i}").ToArray();

        Parallel.ForEach(names, name => guard.AddRestriction(new FakeRestriction(name, DiskWriteDecision.Allow)));

        guard.RestrictionNames.Count.ShouldBe(100);
        guard.RestrictionNames.ShouldBe(names, ignoreOrder: true);
    }

    [Fact]
    public void AddRestrictionRejectsNull()
    {
        var guard = new DiskWriteGuard(new FakeProbe(Candidate(NonSystemDisk)), new FakeOpener());

        Should.Throw<ArgumentNullException>(() => guard.AddRestriction(null!));
    }

    [Fact]
    public void GuardHasNoRemoveOrClearMethods()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var names = typeof(DiskWriteGuard).GetMethods(all).Select(m => m.Name).ToArray();

        names.Where(n => n.Contains("Remove", StringComparison.Ordinal) ||
                         n.Contains("Clear", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    private static DiskWriteCandidate Candidate(
        int diskNumber, bool isSystem = false, bool containsWindows = false) =>
        new(diskNumber, STORAGE_BUS_TYPE.BusTypeSata, isSystem, containsWindows);

    /// <summary>Returns a fixed candidate, or throws a fixed exception, and counts calls.</summary>
    private sealed class FakeProbe : IDiskIdentityProbe
    {
        private readonly DiskWriteCandidate? _candidate;
        private readonly Exception? _exception;

        public FakeProbe(DiskWriteCandidate candidate) => _candidate = candidate;

        public FakeProbe(Exception exception) => _exception = exception;

        public int CallCount { get; private set; }

        public DiskWriteCandidate Probe(int diskNumber)
        {
            CallCount++;
            if (_exception is not null)
            {
                throw _exception;
            }

            return _candidate!;
        }
    }

    /// <summary>Records the paths it was asked to open and returns an inert handle.</summary>
    private sealed class FakeOpener : IDeviceOpener
    {
        public List<string> Paths { get; } = [];

        public bool ReturnInvalidHandle { get; init; }

        public SafeDeviceHandle OpenForReadWrite(string devicePath)
        {
            Paths.Add(devicePath);
            return ReturnInvalidHandle ? new SafeDeviceHandle() : InertHandle();
        }

        /// <summary>
        /// Wraps an unnamed kernel event in a SafeDeviceHandle. Ownership moves from the EventWaitHandle to the
        /// returned handle, so disposing the result closes the event and nothing else.
        /// </summary>
        private static SafeDeviceHandle InertHandle()
        {
            using var waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
            IntPtr eventHandle = waitHandle.SafeWaitHandle.DangerousGetHandle();
            waitHandle.SafeWaitHandle.SetHandleAsInvalid();

            var handle = new SafeDeviceHandle();
            SetHandleMethod.Invoke(handle, [eventHandle]);
            return handle;
        }

        // SafeHandle.SetHandle is protected and SafeDeviceHandle is sealed, so a test can only set the value by reflection.
        private static readonly MethodInfo SetHandleMethod =
            typeof(SafeHandle).GetMethod("SetHandle", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SafeHandle.SetHandle not found.");
    }

    private sealed class FakeRestriction(string name, DiskWriteDecision decision) : IDiskWriteRestriction
    {
        public string Name { get; } = name;

        public int EvaluationCount { get; private set; }

        public DiskWriteDecision Evaluate(DiskWriteCandidate candidate)
        {
            EvaluationCount++;
            return decision;
        }
    }

    private sealed class ThrowingRestriction(string name) : IDiskWriteRestriction
    {
        public string Name { get; } = name;

        public DiskWriteDecision Evaluate(DiskWriteCandidate candidate) =>
            throw new InvalidOperationException("restriction failure");
    }
}
