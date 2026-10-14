using WinCopy.Domain.Shared;

namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>
/// Translates a GPT partition into the Windows volume that lives on it. Shared by the snapshot service,
/// the file-system expander and the temporary mounter.
/// </summary>
internal interface IVolumePathResolver
{
    /// <summary>
    /// Returns the volume GUID path of the partition, for example <c>\\?\Volume{...}\</c> (with the trailing
    /// backslash). Throws when the partition has no volume.
    /// </summary>
    string GetVolumeGuidPath(PartitionGuid partition);
}
