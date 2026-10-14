using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.Logging;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// Opens the target disk of a confirmed <see cref="ClonePlan"/>. The disk is opened only through
/// <see cref="DiskWriteGuard.OpenDiskForWrite"/>, and the identity of the disk is checked before the target is returned.
/// </summary>
internal sealed class TargetDiskFactory : ITargetDiskFactory
{
    private readonly DiskWriteGuard _guard;
    private readonly DiskOperationRecorder _recorder;
    private readonly Func<SafeDeviceHandle, int, ITargetDiskDevice> _createDevice;

    /// <summary>
    /// Production constructor. Uses the Win32 device over the handle that the guard returns. <paramref name="onWarning"/>
    /// receives a message for every volume skipped during the lock because it is not on a disk.
    /// </summary>
    public TargetDiskFactory(DiskWriteGuard guard, DiskOperationRecorder recorder, Action<string>? onWarning)
        : this(guard, recorder, (handle, diskNumber) =>
            new Win32TargetDiskDevice(handle, diskNumber, new WindowsVolumeExtentsReader(), onWarning))
    {
    }

    /// <summary>Test constructor. <paramref name="createDevice"/> takes ownership of the handle it receives.</summary>
    internal TargetDiskFactory(
        DiskWriteGuard guard,
        DiskOperationRecorder recorder,
        Func<SafeDeviceHandle, int, ITargetDiskDevice> createDevice)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(createDevice);

        _guard = guard;
        _recorder = recorder;
        _createDevice = createDevice;
    }

    /// <summary>
    /// Opens the destination of <paramref name="confirmedPlan"/>. Throws <see cref="InvalidOperationException"/> if the plan is
    /// not confirmed, and <see cref="PlatformException"/> with <see cref="PlatformErrorCode.DeviceNotFound"/> if the disk
    /// identity no longer matches the plan. The guard's <see cref="DiskWriteDeniedException"/> is not translated.
    /// </summary>
    public ITargetDisk Open(ClonePlan confirmedPlan)
    {
        ArgumentNullException.ThrowIfNull(confirmedPlan);

        if (confirmedPlan.Status != PlanStatus.Confirmed)
        {
            throw new InvalidOperationException(
                $"The clone plan must be Confirmed to open its target disk; it is {confirmedPlan.Status}.");
        }

        DiskId target = confirmedPlan.Target;
        SafeDeviceHandle handle = _guard.OpenDiskForWrite(target.Number);

        ITargetDiskDevice device;
        try
        {
            device = _createDevice(handle, target.Number);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        try
        {
            VerifyIdentity(device, target);
            return new TargetDisk(device, _recorder, DiskLabel(target));
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    private static void VerifyIdentity(ITargetDiskDevice device, DiskId target)
    {
        Guid? found = device.GptDiskId();

        // A GPT disk must carry the GUID the plan was made for. A RAW or MBR disk has no GUID, so the plan must hold Guid.Empty.
        bool matches = found is { } gptId ? gptId == target.DiskGuid : target.DiskGuid == Guid.Empty;
        if (!matches)
        {
            throw new PlatformException(
                PlatformErrorCode.DeviceNotFound,
                $"target disk identity changed: plan expects {target.DiskGuid}, disk {target.Number} reports {found?.ToString() ?? "no GPT"}.");
        }
    }

    private static string DiskLabel(DiskId target) =>
        target.DiskGuid == Guid.Empty ? $"disk {target.Number}" : target.DiskGuid.ToString("D");
}
