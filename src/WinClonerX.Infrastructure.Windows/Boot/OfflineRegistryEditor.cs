using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.BootRepair;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;

namespace WinClonerX.Infrastructure.Windows.Boot;

/// <summary>Loads and unloads an offline hive under a mount name in HKLM. Tests fake it; <see cref="Win32OfflineHive"/> is the real one.</summary>
internal interface IOfflineHive
{
    /// <summary>Loads <paramref name="hivePath"/> under HKLM\<paramref name="mountName"/>. Throws when the load fails.</summary>
    void Load(string hivePath, string mountName);

    /// <summary>Opens HKLM\<paramref name="mountName"/>\MountedDevices for writing. Throws when it cannot be opened.</summary>
    IMountedDevicesKey OpenMountedDevices(string mountName);

    /// <summary>Unloads HKLM\<paramref name="mountName"/>. Returns a Win32 error code, 0 on success. Keys must be closed first.</summary>
    int Unload(string mountName);
}

/// <summary>The MountedDevices key of a loaded hive. Disposing it closes the key.</summary>
internal interface IMountedDevicesKey : IDisposable
{
    /// <summary>Names of the values, as a snapshot that stays valid while values are deleted.</summary>
    IReadOnlyList<string> GetValueNames();

    /// <summary>Data of a REG_BINARY value, or null when the value is not binary.</summary>
    byte[]? GetBinary(string name);

    /// <summary>Deletes the value. A missing value is not an error.</summary>
    void Delete(string name);

    /// <summary>Writes a REG_BINARY value.</summary>
    void SetBinary(string name, byte[] data);
}

/// <summary>Enables named privileges on the process token. Throws when a privilege cannot be enabled.</summary>
internal interface IPrivilegeEnabler
{
    void Enable(IReadOnlyList<string> privilegeNames);
}

/// <summary>
/// Writes the MountedDevices entry of a clone so that its Windows volume becomes <c>C:</c> (R5, T068). It edits only the
/// clone's SYSTEM hive, loaded under a unique mount name in HKLM. It never touches the running system's registry.
/// </summary>
/// <remarks>
/// <para>The order of the steps is fixed:</para>
/// <list type="number">
/// <item>The windows root must be a clone: the same checks as <see cref="BootTargetSafety"/> (drive-root format, not the
/// running Windows drive, its volume on no system or boot disk, SYSTEM hive present). Failure means nothing is loaded.</item>
/// <item>SeBackupPrivilege and SeRestorePrivilege are enabled. Failure means nothing is loaded.</item>
/// <item>The hive is loaded. From here on it is always unloaded, even when the edit fails.</item>
/// </list>
/// <para>
/// The edit deletes the stale values (those that point at source partitions) and sets <c>\DosDevices\&lt;letter&gt;:</c>.
/// The key is closed before the unload. An unload that fails with ERROR_ACCESS_DENIED is retried. If the edit also failed,
/// its error is the one reported, with the unload error added to it.
/// </para>
/// <para>Cancellation is honored only before the hive is loaded. A half-done edit must not be cut short.</para>
/// </remarks>
internal sealed class OfflineRegistryEditor : IOfflineRegistryEditor
{
    internal const string WindowsSystemHivePath = @"Windows\System32\config\SYSTEM";
    internal const string MountNamePrefix = "WinClonerXClone_";

    /// <summary>Number of retries after the first failed unload. The total attempts are one more than this.</summary>
    internal const int UnloadRetries = 5;

    private const PlatformErrorCode FailureCode = PlatformErrorCode.RegistryEditFailed;
    private const string DosDevicesPrefix = @"\DosDevices\";
    private const int ErrorAccessDenied = 5;
    private const string VolumeGuidPrefix = @"\\?\Volume{";
    private const string VolumeGuidSuffix = @"}\";

    private static readonly TimeSpan DefaultUnloadRetryDelay = TimeSpan.FromMilliseconds(200);
    private static readonly string[] RequiredPrivileges = ["SeBackupPrivilege", "SeRestorePrivilege"];

    private readonly IBootTargetEnvironment _environment;
    private readonly IVolumeExtentsReader _extents;
    private readonly IOfflineHive _hive;
    private readonly IPrivilegeEnabler _privileges;
    private readonly TimeSpan _unloadRetryDelay;

    public OfflineRegistryEditor()
        : this(
            new Win32BootTargetEnvironment(),
            new WindowsVolumeExtentsReader(),
            new Win32OfflineHive(),
            new Win32PrivilegeEnabler(),
            DefaultUnloadRetryDelay)
    {
    }

    internal OfflineRegistryEditor(
        IBootTargetEnvironment environment,
        IVolumeExtentsReader extents,
        IOfflineHive hive,
        IPrivilegeEnabler privileges,
        TimeSpan unloadRetryDelay)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(extents);
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentNullException.ThrowIfNull(privileges);

        _environment = environment;
        _extents = extents;
        _hive = hive;
        _privileges = privileges;
        _unloadRetryDelay = unloadRetryDelay;
    }

    /// <inheritdoc />
    public Task RewriteMountedDevicesAsync(string windowsRoot, MountedDevicesRewrite rewrite, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(windowsRoot);
        ArgumentNullException.ThrowIfNull(rewrite);
        ct.ThrowIfCancellationRequested();

        return Task.Run(() => Rewrite(windowsRoot, rewrite), ct);
    }

    private void Rewrite(string windowsRoot, MountedDevicesRewrite rewrite)
    {
        AssertCloneWindowsRoot(windowsRoot);
        char letter = ValidateRewrite(rewrite);

        EnableRequiredPrivileges();

        string mountName = MountNamePrefix + Guid.NewGuid().ToString("N");
        LoadHive(windowsRoot + WindowsSystemHivePath, mountName);
        EditThenUnload(mountName, letter, rewrite);
    }

    // Same checks as BootTargetSafety, through the same OS queries. Every failure refuses the edit.
    private void AssertCloneWindowsRoot(string windowsRoot)
    {
        char letter = BootTargetSafety.DriveLetterOf(windowsRoot, FailureCode);

        string runningRoot = Query("read the running Windows drive", _environment.RunningWindowsDriveRoot);
        if (letter == BootTargetSafety.DriveLetterOf(runningRoot, FailureCode))
        {
            throw Failure($"'{windowsRoot}' uses the drive of the running Windows.");
        }

        IReadOnlySet<uint> systemDisks = RunningSystemDisks();

        string volumeGuidPath = Query(
            $"resolve the volume of '{windowsRoot}'", () => _environment.VolumeGuidPathOf(windowsRoot));
        if (!volumeGuidPath.StartsWith(VolumeGuidPrefix, StringComparison.Ordinal)
            || !volumeGuidPath.EndsWith(VolumeGuidSuffix, StringComparison.Ordinal))
        {
            throw Failure($"The volume of '{windowsRoot}' has an unexpected GUID path '{volumeGuidPath}'.");
        }

        // The device open takes the path without the trailing backslash.
        string devicePath = volumeGuidPath.TrimEnd('\\');
        IReadOnlySet<uint> disks = Query($"read the disks of '{windowsRoot}'", () => _extents.DiskNumbersOf(devicePath));
        if (disks.Count == 0)
        {
            throw Failure($"The volume of '{windowsRoot}' reports no disks.");
        }

        if (disks.Overlaps(systemDisks))
        {
            throw Failure($"'{windowsRoot}' is on a disk that holds the running Windows or its boot partition.");
        }

        bool hasSystemHive = Query(
            "look for the Windows SYSTEM hive", () => _environment.FileExists(windowsRoot + WindowsSystemHivePath));
        if (!hasSystemHive)
        {
            throw Failure($"No Windows installation found at '{windowsRoot}' (SYSTEM hive missing).");
        }
    }

    private HashSet<uint> RunningSystemDisks()
    {
        var disks = new HashSet<uint>();

        disks.UnionWith(Query(
            "read the disks of the running Windows volume",
            () => _extents.DiskNumbersOf(_extents.WindowsVolumePath())));

        disks.UnionWith(Query(
            "read the disks of the boot partition",
            () => _extents.DiskNumbersOf(_extents.SystemPartitionVolumePath())));

        if (disks.Count == 0)
        {
            throw Failure("The disks of the running system could not be identified.");
        }

        return disks;
    }

    private static char ValidateRewrite(MountedDevicesRewrite rewrite)
    {
        if (!char.IsAsciiLetter(rewrite.DriveLetter))
        {
            throw Failure($"'{rewrite.DriveLetter}' is not a drive letter from A to Z.");
        }

        if (rewrite.NewValue.Length != MountedDevicesRewrite.GptValueLength)
        {
            throw Failure(
                $"The new MountedDevices value has {rewrite.NewValue.Length} bytes; a GPT value has {MountedDevicesRewrite.GptValueLength}.");
        }

        return char.ToUpperInvariant(rewrite.DriveLetter);
    }

    private void EnableRequiredPrivileges()
    {
        try
        {
            _privileges.Enable(RequiredPrivileges);
        }
        catch (Exception ex)
        {
            throw new PlatformException(
                FailureCode,
                $"The privileges {string.Join(" and ", RequiredPrivileges)} could not be enabled: {ex.Message}",
                ex);
        }
    }

    private void LoadHive(string hivePath, string mountName)
    {
        try
        {
            _hive.Load(hivePath, mountName);
        }
        catch (Exception ex)
        {
            throw new PlatformException(FailureCode, $"RegLoadKey of '{hivePath}' failed: {ex.Message}", ex);
        }
    }

    // The edit and the unload are separate steps, so the unload always runs. A failed edit keeps its own error.
    private void EditThenUnload(string mountName, char letter, MountedDevicesRewrite rewrite)
    {
        PlatformException? editFailure = null;
        try
        {
            EditMountedDevices(mountName, letter, rewrite);
        }
        catch (Exception ex)
        {
            editFailure = AsRegistryFailure(ex, "The MountedDevices edit failed");
        }

        try
        {
            UnloadWithRetry(mountName);
        }
        catch (Exception unloadFailure)
        {
            PlatformException unload = AsRegistryFailure(unloadFailure, "The hive could not be unloaded");
            if (editFailure is null)
            {
                throw unload;
            }

            throw new PlatformException(
                FailureCode,
                $"{editFailure.TechnicalDetail} The hive could not be unloaded either: {unload.TechnicalDetail}",
                editFailure);
        }

        if (editFailure is not null)
        {
            ExceptionDispatchInfo.Capture(editFailure).Throw();
        }
    }

    private void EditMountedDevices(string mountName, char letter, MountedDevicesRewrite rewrite)
    {
        string driveValue = DosDevicesPrefix + letter + ":";

        // The key is disposed when this method returns, so it is closed before the unload.
        using IMountedDevicesKey key = _hive.OpenMountedDevices(mountName);

        foreach (string name in key.GetValueNames())
        {
            byte[]? data = key.GetBinary(name);
            if (data is null || !rewrite.IsStale(data))
            {
                continue;
            }

            // The drive letter value is overwritten below, so it is not deleted first.
            if (string.Equals(name, driveValue, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            key.Delete(name);
        }

        key.SetBinary(driveValue, rewrite.NewValue);
    }

    private void UnloadWithRetry(string mountName)
    {
        for (int retry = 0; ; retry++)
        {
            int status = _hive.Unload(mountName);
            if (status == 0)
            {
                return;
            }

            // Only ERROR_ACCESS_DENIED is retried. It is what a key that is still open produces.
            if (status != ErrorAccessDenied || retry == UnloadRetries)
            {
                throw new PlatformException(
                    FailureCode,
                    $"RegUnLoadKey of '{mountName}' failed with Win32 error {status} after {retry + 1} attempt(s).");
            }

            Thread.Sleep(_unloadRetryDelay);
        }
    }

    private static T Query<T>(string action, Func<T> query)
    {
        try
        {
            return query();
        }
        catch (Exception ex)
        {
            throw new PlatformException(FailureCode, $"Registry edit check could not {action}: {ex.Message}", ex);
        }
    }

    private static PlatformException AsRegistryFailure(Exception ex, string context) =>
        ex as PlatformException ?? new PlatformException(FailureCode, $"{context}: {ex.Message}", ex);

    private static PlatformException Failure(string detail) => new(FailureCode, detail);
}

/// <summary>Win32 implementation of <see cref="IOfflineHive"/>, built on RegLoadKeyW, RegUnLoadKeyW and Microsoft.Win32.Registry.</summary>
internal sealed class Win32OfflineHive : IOfflineHive
{
    private const string MountedDevicesSubKey = "MountedDevices";

    public void Load(string hivePath, string mountName)
    {
        int status = Advapi32.RegLoadKeyW(Advapi32.HKEY_LOCAL_MACHINE, mountName, hivePath);
        if (status != 0)
        {
            throw new Win32Exception(status, $"RegLoadKey failed for '{hivePath}'.");
        }
    }

    public IMountedDevicesKey OpenMountedDevices(string mountName)
    {
        RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"{mountName}\{MountedDevicesSubKey}", writable: true);

        return key is null
            ? throw new InvalidOperationException($@"HKLM\{mountName}\{MountedDevicesSubKey} does not exist.")
            : new RegistryMountedDevicesKey(key);
    }

    public int Unload(string mountName) => Advapi32.RegUnLoadKeyW(Advapi32.HKEY_LOCAL_MACHINE, mountName);

    private sealed class RegistryMountedDevicesKey(RegistryKey key) : IMountedDevicesKey
    {
        public IReadOnlyList<string> GetValueNames() => key.GetValueNames();

        public byte[]? GetBinary(string name) =>
            key.GetValueKind(name) == RegistryValueKind.Binary ? key.GetValue(name) as byte[] : null;

        public void Delete(string name) => key.DeleteValue(name, throwOnMissingValue: false);

        public void SetBinary(string name, byte[] data) => key.SetValue(name, data, RegistryValueKind.Binary);

        public void Dispose() => key.Dispose();
    }
}

/// <summary>Win32 implementation of <see cref="IPrivilegeEnabler"/>, built on OpenProcessToken and AdjustTokenPrivileges.</summary>
internal sealed class Win32PrivilegeEnabler : IPrivilegeEnabler
{
    public void Enable(IReadOnlyList<string> privilegeNames)
    {
        ArgumentNullException.ThrowIfNull(privilegeNames);

        const uint access = Advapi32.TOKEN_ADJUST_PRIVILEGES | Advapi32.TOKEN_QUERY;
        if (!Advapi32.OpenProcessToken(Advapi32.CurrentProcessPseudoHandle, access, out IntPtr token))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "OpenProcessToken failed.");
        }

        try
        {
            foreach (string name in privilegeNames)
            {
                EnableOne(token, name);
            }
        }
        finally
        {
            Kernel32.CloseHandle(token);
        }
    }

    private static void EnableOne(IntPtr token, string name)
    {
        if (!Advapi32.LookupPrivilegeValueW(null, name, out LUID luid))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"LookupPrivilegeValue failed for {name}.");
        }

        var state = new TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Luid = luid,
            Attributes = Advapi32.SE_PRIVILEGE_ENABLED,
        };

        bool ok = Advapi32.AdjustTokenPrivileges(token, false, in state, 0, IntPtr.Zero, IntPtr.Zero);
        int error = Marshal.GetLastPInvokeError();

        // AdjustTokenPrivileges returns TRUE even when the token does not hold the privilege. GetLastError tells them apart.
        if (!ok || error == Advapi32.ERROR_NOT_ALL_ASSIGNED)
        {
            throw new Win32Exception(error, $"The privilege {name} could not be enabled on the process token.");
        }
    }
}
