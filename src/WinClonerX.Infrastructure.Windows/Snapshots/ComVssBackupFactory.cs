using WinClonerX.Application.Errors;
using WinClonerX.Infrastructure.Windows.Interop.Vss;

namespace WinClonerX.Infrastructure.Windows.Snapshots;

/// <summary>Creates a real VSS backup-components object through vssapi.dll.</summary>
internal sealed class ComVssBackupFactory : IVssBackupFactory
{
    /// <inheritdoc />
    public IVssSession Create()
    {
        int hr = VssApi.CreateVssBackupComponentsInternal(out nint backupComponents);
        if (hr < 0)
        {
            throw new PlatformException(
                PlatformErrorCode.SnapshotFailed,
                $"CreateVssBackupComponents failed: 0x{hr:X8}");
        }

        if (backupComponents == 0)
        {
            throw new PlatformException(
                PlatformErrorCode.SnapshotFailed,
                "CreateVssBackupComponents returned no object");
        }

        return new ComVssSession(backupComponents);
    }
}
