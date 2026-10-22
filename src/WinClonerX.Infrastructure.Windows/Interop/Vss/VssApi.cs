using System.Runtime.InteropServices;

namespace WinClonerX.Infrastructure.Windows.Interop.Vss;

/// <summary>Exports of vssapi.dll used to create the backup components and to free snapshot properties.</summary>
internal static partial class VssApi
{
    /// <summary>
    /// Creates an IVssBackupComponents object. Returns an HRESULT. On success <paramref name="ppBackup"/> is a COM
    /// pointer that the caller owns.
    /// </summary>
    [LibraryImport("vssapi.dll")]
    internal static partial int CreateVssBackupComponentsInternal(out nint ppBackup);

    /// <summary>Frees the string members of a VSS_SNAPSHOT_PROP that VSS filled in.</summary>
    [LibraryImport("vssapi.dll")]
    internal static partial void VssFreeSnapshotProperties(ref VSS_SNAPSHOT_PROP prop);
}
