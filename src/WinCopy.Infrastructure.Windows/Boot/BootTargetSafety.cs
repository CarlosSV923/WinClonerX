using System.ComponentModel;
using System.Runtime.InteropServices;
using WinCopy.Application.Errors;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.Windows.Boot;

/// <summary>
/// OS queries used by <see cref="BootTargetSafety"/>. Tests fake them. <see cref="Win32BootTargetEnvironment"/> is the
/// real implementation.
/// </summary>
internal interface IBootTargetEnvironment
{
    /// <summary>
    /// Volume GUID path of the volume mounted at <paramref name="driveRoot"/>, with its trailing backslash, for example
    /// <c>\\?\Volume{...}\</c>. Throws when there is no such volume.
    /// </summary>
    string VolumeGuidPathOf(string driveRoot);

    /// <summary>Root of the drive that holds the running Windows folder, for example <c>C:\</c>.</summary>
    string RunningWindowsDriveRoot();

    /// <summary>True when the file exists.</summary>
    bool FileExists(string path);
}

/// <summary>
/// Refuses any boot or recovery write whose target could be the running system (R7, T070).
/// </summary>
/// <remarks>
/// <para>Every check must pass. Otherwise the call throws <see cref="PlatformException"/> with the code the caller asks
/// for, and nothing is started. The checks, in order:</para>
/// <list type="number">
/// <item>Both roots are drive roots of the form <c>X:\</c>, with different letters.</item>
/// <item>Neither root uses the drive letter of the running Windows folder.</item>
/// <item>The volume behind each root is on no disk that holds the running Windows volume or the boot partition.</item>
/// <item><c>windowsRoot</c> holds a Windows installation: <c>Windows\System32\config\SYSTEM</c> exists.</item>
/// </list>
/// <para>An OS query that fails, or an answer that cannot be interpreted, refuses the write. The guard fails closed.</para>
/// </remarks>
internal sealed class BootTargetSafety
{
    private const string WindowsSystemHivePath = @"Windows\System32\config\SYSTEM";
    private const string VolumeGuidPrefix = @"\\?\Volume{";
    private const string VolumeGuidSuffix = @"}\";

    private readonly IBootTargetEnvironment _environment;
    private readonly IVolumeExtentsReader _extents;

    public BootTargetSafety(IBootTargetEnvironment environment, IVolumeExtentsReader extents)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(extents);

        _environment = environment;
        _extents = extents;
    }

    /// <summary>
    /// Throws <see cref="PlatformException"/> with <paramref name="failureCode"/> unless the two roots are safe clone
    /// targets. <paramref name="espRoot"/> is the EFI partition for bcdboot, or the recovery partition for reagentc.
    /// </summary>
    public void AssertCloneRoots(string windowsRoot, string espRoot, PlatformErrorCode failureCode = PlatformErrorCode.BootConfigFailed)
    {
        char windowsLetter = DriveLetterOf(windowsRoot, failureCode);
        char espLetter = DriveLetterOf(espRoot, failureCode);

        if (windowsLetter == espLetter)
        {
            throw new PlatformException(
                failureCode, $"The Windows root and the second root both use the drive letter '{windowsLetter}:'.");
        }

        string runningRoot = Query(failureCode, "read the running Windows drive", _environment.RunningWindowsDriveRoot);
        char runningLetter = DriveLetterOf(runningRoot, failureCode);
        if (windowsLetter == runningLetter || espLetter == runningLetter)
        {
            throw new PlatformException(
                failureCode, $"A target root uses '{runningLetter}:', the drive of the running Windows.");
        }

        IReadOnlySet<uint> systemDisks = RunningSystemDisks(failureCode);
        AssertNotOnDisks(windowsRoot, systemDisks, failureCode);
        AssertNotOnDisks(espRoot, systemDisks, failureCode);

        string systemHive = windowsRoot + WindowsSystemHivePath;
        bool hasSystemHive = Query(failureCode, "look for the Windows SYSTEM hive", () => _environment.FileExists(systemHive));
        if (!hasSystemHive)
        {
            throw new PlatformException(failureCode, $"No Windows installation found at '{windowsRoot}' (SYSTEM hive missing).");
        }
    }

    /// <summary>Drive letter of a drive root of the form <c>X:\</c>, in upper case. Throws for any other shape.</summary>
    internal static char DriveLetterOf(string root, PlatformErrorCode failureCode)
    {
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
        {
            throw new PlatformException(failureCode, $"'{root}' is not a drive root of the form X:\\.");
        }

        return char.ToUpperInvariant(root[0]);
    }

    /// <summary>Disks of the running Windows volume and of the boot partition. Empty answers are refused.</summary>
    private HashSet<uint> RunningSystemDisks(PlatformErrorCode failureCode)
    {
        var disks = new HashSet<uint>();

        disks.UnionWith(Query(
            failureCode,
            "read the disks of the running Windows volume",
            () => _extents.DiskNumbersOf(_extents.WindowsVolumePath())));

        disks.UnionWith(Query(
            failureCode,
            "read the disks of the boot partition",
            () => _extents.DiskNumbersOf(_extents.SystemPartitionVolumePath())));

        if (disks.Count == 0)
        {
            throw new PlatformException(failureCode, "The disks of the running system could not be identified.");
        }

        return disks;
    }

    private void AssertNotOnDisks(string root, IReadOnlySet<uint> systemDisks, PlatformErrorCode failureCode)
    {
        string volumeGuidPath = Query(
            failureCode, $"resolve the volume of '{root}'", () => _environment.VolumeGuidPathOf(root));

        if (!volumeGuidPath.StartsWith(VolumeGuidPrefix, StringComparison.Ordinal)
            || !volumeGuidPath.EndsWith(VolumeGuidSuffix, StringComparison.Ordinal))
        {
            throw new PlatformException(
                failureCode, $"The volume of '{root}' has an unexpected GUID path '{volumeGuidPath}'.");
        }

        // The device open takes the path without the trailing backslash, as WindowsVolumeExtentsReader expects.
        string devicePath = volumeGuidPath.TrimEnd('\\');
        IReadOnlySet<uint> disks = Query(
            failureCode, $"read the disks of '{root}'", () => _extents.DiskNumbersOf(devicePath));

        if (disks.Count == 0)
        {
            throw new PlatformException(failureCode, $"The volume of '{root}' reports no disks.");
        }

        if (disks.Overlaps(systemDisks))
        {
            throw new PlatformException(
                failureCode, $"'{root}' is on a disk that holds the running Windows or its boot partition.");
        }
    }

    /// <summary>Runs an OS query. Any failure becomes a <see cref="PlatformException"/> with the caller's code.</summary>
    private static T Query<T>(PlatformErrorCode failureCode, string action, Func<T> query)
    {
        try
        {
            return query();
        }
        catch (Exception ex)
        {
            throw new PlatformException(failureCode, $"Boot target check could not {action}: {ex.Message}", ex);
        }
    }
}

/// <summary>Win32 implementation of <see cref="IBootTargetEnvironment"/>.</summary>
internal sealed class Win32BootTargetEnvironment : IBootTargetEnvironment
{
    // A volume GUID path (\\?\Volume{GUID}\) is 49 characters, plus the terminating NUL.
    private const int VolumeGuidBufferLength = 50;

    public unsafe string VolumeGuidPathOf(string driveRoot)
    {
        char[] buffer = new char[VolumeGuidBufferLength];
        bool ok;
        int error = 0;

        fixed (char* pointer = buffer)
        {
            ok = Kernel32.GetVolumeNameForVolumeMountPointW(driveRoot, pointer, (uint)buffer.Length);
            if (!ok)
            {
                error = Marshal.GetLastPInvokeError();
            }
        }

        if (!ok)
        {
            throw new Win32Exception(error, $"GetVolumeNameForVolumeMountPoint failed for '{driveRoot}'.");
        }

        int length = Array.IndexOf(buffer, '\0');
        if (length <= 0)
        {
            throw new InvalidOperationException($"GetVolumeNameForVolumeMountPoint returned no name for '{driveRoot}'.");
        }

        return new string(buffer, 0, length);
    }

    public string RunningWindowsDriveRoot() =>
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? string.Empty;

    public bool FileExists(string path) => File.Exists(path);
}
