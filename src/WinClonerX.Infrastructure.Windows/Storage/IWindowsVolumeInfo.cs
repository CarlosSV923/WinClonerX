namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>Identifies the volume that holds the running Windows folder.</summary>
internal interface IWindowsVolumeInfo
{
    /// <summary>Volume GUID path of the Windows drive, for example <c>\\?\Volume{...}\</c>. Throws when it cannot be resolved.</summary>
    string WindowsVolumeGuidPath();
}
