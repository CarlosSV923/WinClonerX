namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>One GPT entry of a disk layout, matched to an <c>MSFT_Partition</c> by its number.</summary>
internal sealed record DriveLayoutEntry(uint PartitionNumber, ulong Attributes, string Name);

/// <summary>
/// Reads the GPT entries of a disk, which the Storage API does not expose: the GPT attributes and the partition name.
/// The implementation opens the disk with no access rights and only queries it.
/// </summary>
internal interface IDriveLayoutReader
{
    /// <summary>Entries of the GPT layout of disk <paramref name="diskNumber"/>. Throws when the disk cannot be read.</summary>
    IReadOnlyList<DriveLayoutEntry> ReadLayout(int diskNumber);
}
