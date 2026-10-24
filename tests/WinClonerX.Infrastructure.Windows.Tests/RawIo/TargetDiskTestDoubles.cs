using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// Doubles for the target-disk tests. No double touches a device, a volume or a disk.

/// <summary>Byte sizes used by the target-disk tests.</summary>
internal static class Sizes
{
    public const long Mib = 1024L * 1024;
}

/// <summary>A write the fake device accepted, with a copy of its bytes.</summary>
internal sealed record WrittenRange(long Offset, byte[] Data);

/// <summary>
/// Records every device call in <see cref="Calls"/>, in order. Failures and the offline behaviour are injected.
/// </summary>
internal sealed class FakeTargetDiskDevice(long length, int sectorSize, Guid? gptDiskId) : ITargetDiskDevice
{
    public List<string> Calls { get; } = [];

    public List<WrittenRange> Writes { get; } = [];

    public List<FakeVolumeLock> Locks { get; } = [];

    /// <summary>Thrown by <see cref="LockVolumesOnDisk"/> when set.</summary>
    public Exception? LockFailure { get; set; }

    /// <summary>When set, <see cref="Write"/> fails at this absolute offset.</summary>
    public long? FailWriteAt { get; set; }

    /// <summary>The exception thrown by a failing <see cref="Write"/>. Null means an <see cref="IOException"/>.</summary>
    public Exception? WriteFailure { get; set; }

    /// <summary>When true, <see cref="SetOffline"/> is recorded but has no effect, so the disk never reports offline.</summary>
    public bool IgnoreSetOffline { get; set; }

    public int DisposeCount { get; private set; }

    public bool Offline { get; private set; }

    public long Length { get; } = length;

    public int LogicalSectorSize { get; } = sectorSize;

    public void SetOffline(bool offline)
    {
        Calls.Add($"SetOffline({offline})");
        if (!IgnoreSetOffline)
        {
            Offline = offline;
        }
    }

    public bool IsOffline() => Offline;

    public void Write(long offset, ReadOnlySpan<byte> aligned)
    {
        Calls.Add($"Write({offset},{aligned.Length})");
        if (FailWriteAt == offset)
        {
            throw WriteFailure ?? new IOException("injected write failure");
        }

        Writes.Add(new WrittenRange(offset, aligned.ToArray()));
    }

    public int Read(long offset, Span<byte> aligned)
    {
        Calls.Add($"Read({offset},{aligned.Length})");
        aligned.Clear();
        return aligned.Length;
    }

    public void UpdateProperties() => Calls.Add("UpdateProperties");

    public Guid? GptDiskId() => gptDiskId;

    public IReadOnlyList<IVolumeLock> LockVolumesOnDisk()
    {
        Calls.Add("LockVolumes");
        if (LockFailure is not null)
        {
            throw LockFailure;
        }

        Locks.Add(new FakeVolumeLock(@"\\?\Volume{A}", this));
        Locks.Add(new FakeVolumeLock(@"\\?\Volume{B}", this));
        return Locks.ToArray();
    }

    public void Dispose()
    {
        DisposeCount++;
        Calls.Add("DeviceDispose");
    }
}

/// <summary>Volume lock that records its release once.</summary>
internal sealed class FakeVolumeLock(string volumePath, FakeTargetDiskDevice owner) : IVolumeLock
{
    private bool _released;

    public string VolumePath { get; } = volumePath;

    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        owner.Calls.Add($"Release({VolumePath})");
    }
}

/// <summary>Returns a fixed identity candidate and counts the probes.</summary>
internal sealed class StubIdentityProbe(DiskWriteCandidate candidate) : IDiskIdentityProbe
{
    public int CallCount { get; private set; }

    public DiskWriteCandidate Probe(int diskNumber)
    {
        CallCount++;
        return candidate;
    }
}

/// <summary>Records every path the guard asks to open and returns an inert handle, so no device is opened.</summary>
internal sealed class RecordingOpener : IDeviceOpener
{
    private static readonly MethodInfo SetHandleMethod =
        typeof(SafeHandle).GetMethod("SetHandle", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("SafeHandle.SetHandle not found.");

    public List<string> Paths { get; } = [];

    public SafeDeviceHandle OpenForReadWrite(string devicePath)
    {
        Paths.Add(devicePath);
        return InertHandle();
    }

    /// <summary>Wraps an unnamed kernel event in a SafeDeviceHandle, so disposing the result closes only the event.</summary>
    private static SafeDeviceHandle InertHandle()
    {
        using var waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
        IntPtr eventHandle = waitHandle.SafeWaitHandle.DangerousGetHandle();
        waitHandle.SafeWaitHandle.SetHandleAsInvalid();

        var handle = new SafeDeviceHandle();
        SetHandleMethod.Invoke(handle, [eventHandle]);
        return handle;
    }
}

/// <summary>
/// Builds a <see cref="ClonePlan"/> with a given status and target. <see cref="ClonePlan.Create"/> is internal to the Domain,
/// and this test project cannot see it, so the fields are set by reflection on an uninitialized instance.
/// </summary>
internal static class TestPlans
{
    public static ClonePlan Create(PlanStatus status, DiskId target)
    {
        var plan = (ClonePlan)RuntimeHelpers.GetUninitializedObject(typeof(ClonePlan));
        SetBackingField(plan, nameof(ClonePlan.Target), target);
        SetBackingField(plan, nameof(ClonePlan.Status), status);
        return plan;
    }

    private static void SetBackingField(ClonePlan plan, string property, object value)
    {
        FieldInfo field = typeof(ClonePlan).GetField($"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"ClonePlan.{property} backing field not found.");
        field.SetValue(plan, value);
    }
}

/// <summary>Candidate for <see cref="DiskWriteGuard"/>: a SATA disk that is neither the system disk nor holds Windows.</summary>
internal static class Candidates
{
    public static DiskWriteCandidate Plain(int diskNumber, bool isSystem = false, bool containsWindows = false) =>
        new(diskNumber, STORAGE_BUS_TYPE.BusTypeSata, isSystem, containsWindows);
}
