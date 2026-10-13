using System.ComponentModel;
using System.Runtime.InteropServices;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// The ONLY allowed way to obtain a write handle to a physical disk (constitution principle I, defense in depth).
/// Do not open <c>\\.\PhysicalDriveN</c> for writing anywhere else.
/// </summary>
/// <remarks>
/// <para>Order of checks in <see cref="OpenDiskForWrite"/>:</para>
/// <list type="number">
/// <item>The disk number must not be negative.</item>
/// <item>The identity probe must succeed and describe the requested disk. Otherwise the write is denied.</item>
/// <item>Production rule, not bypassable: the system disk and the disk that contains the running Windows volume
/// are always denied.</item>
/// <item>Every registered restriction is evaluated. A restriction can only deny. A throwing restriction also denies.</item>
/// <item>Only then is the device opened for read and write.</item>
/// </list>
/// </remarks>
internal sealed class DiskWriteGuard
{
    private static readonly Lazy<DiskWriteGuard> DefaultInstance = new(
        () => new DiskWriteGuard(new WindowsDiskIdentityProbe(), new Win32DeviceOpener()));

    private readonly IDiskIdentityProbe _probe;
    private readonly IDeviceOpener _opener;
    private readonly object _gate = new();
    private readonly List<IDiskWriteRestriction> _restrictions = [];

    public DiskWriteGuard(IDiskIdentityProbe probe, IDeviceOpener opener)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(opener);

        _probe = probe;
        _opener = opener;
    }

    /// <summary>The guard used by production code, with the real Windows probe and opener.</summary>
    public static DiskWriteGuard Default => DefaultInstance.Value;

    /// <summary>Names of the registered restrictions, in registration order.</summary>
    public IReadOnlyList<string> RestrictionNames
    {
        get
        {
            lock (_gate)
            {
                return _restrictions.Select(r => r.Name).ToArray();
            }
        }
    }

    /// <summary>
    /// Adds a process-wide restriction. Restrictions are append-only: there is no way to remove them.
    /// </summary>
    public void AddRestriction(IDiskWriteRestriction restriction)
    {
        ArgumentNullException.ThrowIfNull(restriction);

        lock (_gate)
        {
            _restrictions.Add(restriction);
        }
    }

    /// <summary>
    /// Opens <c>\\.\PhysicalDrive{diskNumber}</c> for read and write, after all checks pass.
    /// Throws <see cref="DiskWriteDeniedException"/> when the write is not allowed. Throws
    /// <see cref="System.ComponentModel.Win32Exception"/> when the OS refuses to open the disk.
    /// </summary>
    public SafeDeviceHandle OpenDiskForWrite(int diskNumber)
    {
        if (diskNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diskNumber), diskNumber, "Disk number cannot be negative.");
        }

        DiskWriteCandidate candidate = ProbeOrDeny(diskNumber);

        if (candidate.IsSystemDisk || candidate.ContainsRunningWindows)
        {
            throw new DiskWriteDeniedException(
                diskNumber, "the disk is the system disk or contains the running Windows volume.");
        }

        CheckRestrictions(candidate);

        string path = PhysicalDisk.PathFor(diskNumber);
        SafeDeviceHandle handle = _opener.OpenForReadWrite(path);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open {path} for writing.");
        }

        return handle;
    }

    private DiskWriteCandidate ProbeOrDeny(int diskNumber)
    {
        DiskWriteCandidate candidate;
        try
        {
            candidate = _probe.Probe(diskNumber);
        }
        catch (Exception ex)
        {
            throw new DiskWriteDeniedException(diskNumber, "identity could not be verified.", ex);
        }

        // A probe that describes a different disk cannot be trusted for this one.
        if (candidate is null || candidate.DiskNumber != diskNumber)
        {
            throw new DiskWriteDeniedException(diskNumber, "identity could not be verified.");
        }

        return candidate;
    }

    /// <summary>
    /// Evaluates every restriction, then denies with the first denial in registration order.
    /// A restriction that throws counts as a denial.
    /// </summary>
    private void CheckRestrictions(DiskWriteCandidate candidate)
    {
        DiskWriteDeniedException? firstDenial = null;

        foreach (IDiskWriteRestriction restriction in Snapshot())
        {
            try
            {
                DiskWriteDecision decision = restriction.Evaluate(candidate);
                if (decision is DiskWriteDecision.DenyDecision deny)
                {
                    firstDenial ??= new DiskWriteDeniedException(
                        candidate.DiskNumber,
                        $"restriction '{restriction.Name}' denied the write: {deny.Reason}");
                }
            }
            catch (Exception ex)
            {
                firstDenial ??= new DiskWriteDeniedException(
                    candidate.DiskNumber,
                    $"restriction '{restriction.Name}' failed and the write is denied.",
                    ex);
            }
        }

        if (firstDenial is not null)
        {
            throw firstDenial;
        }
    }

    private IDiskWriteRestriction[] Snapshot()
    {
        lock (_gate)
        {
            return _restrictions.ToArray();
        }
    }
}
