namespace WinClonerX.Infrastructure.Windows.Platform;

/// <summary>Checks whether a volume can be locked exclusively right now. Used for the TargetInUse fact.</summary>
internal interface IVolumeLockProbe
{
    /// <summary>
    /// True when the volume accepted an exclusive lock. False when the lock was refused, which means another handle is
    /// using it. Throws when the volume cannot be opened for the probe.
    /// </summary>
    bool CanLockExclusively(string volumeDevicePath);
}
