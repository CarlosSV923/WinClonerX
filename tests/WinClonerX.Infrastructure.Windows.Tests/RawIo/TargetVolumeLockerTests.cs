using System.ComponentModel;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// Every volume, extents answer and lock is a fake. No volume is opened.
// The enumerator returns names with the trailing backslash, as Windows does. The locker trims it, so the
// paths below are the trimmed ones.
public sealed class TargetVolumeLockerTests
{
    private const int TargetDisk = 7;
    private const string Target = @"\\?\Volume{TARGET}";
    private const string Iso = @"\\?\Volume{ISO}";
    private const string NotReady = @"\\?\Volume{NOTREADY}";
    private const string Denied = @"\\?\Volume{DENIED}";
    private const string Spanned = @"\\?\Volume{SPANNED}";
    private const string OtherDisk = @"\\?\Volume{OTHER}";

    [Fact]
    public void IsoLikeVolumeIsSkippedAndTheTargetVolumeIsStillLocked()
    {
        var fx = new Fixture(Iso, Target)
        {
            Answers =
            {
                [Iso] = () => throw new Win32Exception(TargetVolumeLocker.ErrorInvalidFunction, "invalid function"),
                [Target] = () => Disks(TargetDisk),
            },
        };

        IReadOnlyList<IVolumeLock> locks = fx.Locker().LockVolumesOnDisk(TargetDisk);

        locks.Select(l => l.VolumePath).ShouldBe([Target]);
        fx.Locked.ShouldBe([Target]);
        fx.Warnings.Count.ShouldBe(1);
        fx.Warnings[0].ShouldContain(Iso);
    }

    [Theory]
    [InlineData(TargetVolumeLocker.ErrorNotReady)]
    [InlineData(TargetVolumeLocker.ErrorNotSupported)]
    [InlineData(TargetVolumeLocker.ErrorInvalidFunction)]
    public void NonDiskVolumesWithKnownErrorsAreSkipped(int error)
    {
        var fx = new Fixture(NotReady, Target)
        {
            Answers =
            {
                [NotReady] = () => throw new Win32Exception(error, "not a disk volume"),
                [Target] = () => Disks(TargetDisk),
            },
        };

        IReadOnlyList<IVolumeLock> locks = fx.Locker().LockVolumesOnDisk(TargetDisk);

        locks.Select(l => l.VolumePath).ShouldBe([Target]);
        fx.Warnings.Count.ShouldBe(1);
    }

    [Fact]
    public void UnexpectedWin32ErrorIsTargetInUseAndTheEarlierLocksAreReleased()
    {
        var fx = new Fixture(Target, Denied)
        {
            Answers =
            {
                [Target] = () => Disks(TargetDisk),
                [Denied] = () => throw new Win32Exception(5, "access denied"),
            },
        };

        PlatformException ex = Should.Throw<PlatformException>(() => fx.Locker().LockVolumesOnDisk(TargetDisk));

        ex.Code.ShouldBe(PlatformErrorCode.TargetInUse);
        fx.Locked.ShouldBe([Target]);
        fx.Released.ShouldBe([Target], "the lock taken before the failure must be released");
        fx.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void SpannedVolumeIsTargetInUse()
    {
        var fx = new Fixture(Spanned)
        {
            Answers = { [Spanned] = () => Disks(TargetDisk, 3) },
        };

        PlatformException ex = Should.Throw<PlatformException>(() => fx.Locker().LockVolumesOnDisk(TargetDisk));

        ex.Code.ShouldBe(PlatformErrorCode.TargetInUse);
        fx.Locked.ShouldBeEmpty();
    }

    [Fact]
    public void SpannedVolumeAfterAnotherLockIsRefusedAndTheEarlierLockIsReleased()
    {
        var fx = new Fixture(Target, Spanned)
        {
            Answers =
            {
                [Target] = () => Disks(TargetDisk),
                [Spanned] = () => Disks(TargetDisk, 3),
            },
        };

        PlatformException ex = Should.Throw<PlatformException>(() => fx.Locker().LockVolumesOnDisk(TargetDisk));

        ex.Code.ShouldBe(PlatformErrorCode.TargetInUse);
        fx.Released.ShouldBe([Target]);
    }

    [Fact]
    public void VolumeWithoutDiskExtentsIsTargetInUse()
    {
        var fx = new Fixture(Denied)
        {
            Answers = { [Denied] = () => throw new InvalidOperationException("reports no disk extents") },
        };

        PlatformException ex = Should.Throw<PlatformException>(() => fx.Locker().LockVolumesOnDisk(TargetDisk));

        ex.Code.ShouldBe(PlatformErrorCode.TargetInUse);
    }

    [Fact]
    public void VolumeOnAnotherDiskIsSkippedWithoutAWarning()
    {
        var fx = new Fixture(OtherDisk)
        {
            Answers = { [OtherDisk] = () => Disks(3) },
        };

        IReadOnlyList<IVolumeLock> locks = fx.Locker().LockVolumesOnDisk(TargetDisk);

        locks.ShouldBeEmpty();
        fx.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void LockFailureIsRaisedAndTheEarlierLocksAreReleased()
    {
        var fx = new Fixture(Target, Denied)
        {
            Answers =
            {
                [Target] = () => Disks(TargetDisk),
                [Denied] = () => Disks(TargetDisk),
            },
            LockFailsFor = Denied,
        };

        PlatformException ex = Should.Throw<PlatformException>(() => fx.Locker().LockVolumesOnDisk(TargetDisk));

        ex.Code.ShouldBe(PlatformErrorCode.TargetInUse);
        fx.Released.ShouldBe([Target]);
    }

    private static HashSet<uint> Disks(params uint[] numbers) => new(numbers);

    /// <summary>Scripted volumes. Each volume's extents answer is a delegate. Locks and releases are recorded by path.</summary>
    private sealed class Fixture(params string[] volumes)
    {
        public Dictionary<string, Func<IReadOnlySet<uint>>> Answers { get; } = [];

        public List<string> Locked { get; } = [];

        public List<string> Released { get; } = [];

        public List<string> Warnings { get; } = [];

        public string? LockFailsFor { get; init; }

        public TargetVolumeLocker Locker() => new(
            new FixedEnumerator(volumes.Select(v => v + @"\").ToArray()),
            new ScriptedExtents(Answers),
            path =>
            {
                if (path == LockFailsFor)
                {
                    throw new PlatformException(PlatformErrorCode.TargetInUse, "lock refused");
                }

                Locked.Add(path);
                return new RecordingLock(path, Released);
            },
            Warnings.Add);
    }

    private sealed class FixedEnumerator(string[] names) : IVolumeEnumerator
    {
        public IReadOnlyList<string> VolumeNames() => names;
    }

    /// <summary>Answers DiskNumbersOf from a script. The script is keyed by the path without the trailing backslash.</summary>
    private sealed class ScriptedExtents(Dictionary<string, Func<IReadOnlySet<uint>>> answers) : IVolumeExtentsReader
    {
        public IReadOnlySet<uint> DiskNumbersOf(string volumePath) =>
            answers.TryGetValue(volumePath, out Func<IReadOnlySet<uint>>? answer)
                ? answer()
                : throw new InvalidOperationException($"No script for {volumePath}.");

        public string WindowsVolumePath() => throw new NotSupportedException();

        public string SystemPartitionVolumePath() => throw new NotSupportedException();

        public STORAGE_BUS_TYPE BusTypeOf(int diskNumber) => throw new NotSupportedException();
    }

    private sealed class RecordingLock(string path, List<string> released) : IVolumeLock
    {
        public string VolumePath { get; } = path;

        public void Dispose() => released.Add(VolumePath);
    }
}
