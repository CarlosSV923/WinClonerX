using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>Identity facts about a disk, as reported by <see cref="IDiskIdentityProbe"/>.</summary>
internal sealed record DiskWriteCandidate(
    int DiskNumber,
    STORAGE_BUS_TYPE BusType,
    bool IsSystemDisk,
    bool ContainsRunningWindows);

/// <summary>Outcome of a <see cref="IDiskWriteRestriction"/>. A restriction can only deny or have no objection.</summary>
internal abstract record DiskWriteDecision
{
    private DiskWriteDecision()
    {
    }

    /// <summary>No objection. It never grants permission on its own.</summary>
    public static DiskWriteDecision Allow { get; } = new AllowDecision();

    public static DiskWriteDecision Deny(string reason) => new DenyDecision(reason);

    public sealed record AllowDecision : DiskWriteDecision;

    public sealed record DenyDecision(string Reason) : DiskWriteDecision;
}

/// <summary>
/// Process-wide restriction on which disks may be written. It can only add prohibitions:
/// <see cref="DiskWriteDecision.Allow"/> does not override the production rule in <see cref="DiskWriteGuard"/>.
/// </summary>
internal interface IDiskWriteRestriction
{
    string Name { get; }

    DiskWriteDecision Evaluate(DiskWriteCandidate candidate);
}

/// <summary>Reads the identity of a physical disk using metadata-only access.</summary>
internal interface IDiskIdentityProbe
{
    DiskWriteCandidate Probe(int diskNumber);
}

/// <summary>Opens a device path for reading and writing. Only <see cref="DiskWriteGuard"/> should call it.</summary>
internal interface IDeviceOpener
{
    SafeDeviceHandle OpenForReadWrite(string devicePath);
}

/// <summary>Thrown when <see cref="DiskWriteGuard"/> refuses to open a disk for writing.</summary>
internal sealed class DiskWriteDeniedException : Exception
{
    public DiskWriteDeniedException(int diskNumber, string reason, Exception? innerException = null)
        : base($"Writing to disk {diskNumber} is denied: {reason}", innerException)
    {
        DiskNumber = diskNumber;
        Reason = reason;
    }

    public int DiskNumber { get; }

    public string Reason { get; }
}

/// <summary>Path helpers shared by the guard and the probe.</summary>
internal static class PhysicalDisk
{
    public static string PathFor(int diskNumber) => $@"\\.\PhysicalDrive{diskNumber}";
}
