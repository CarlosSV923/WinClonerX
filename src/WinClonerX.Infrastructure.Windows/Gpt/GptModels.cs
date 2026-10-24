namespace WinClonerX.Infrastructure.Windows.Gpt;

/// <summary>One GPT partition entry. <see cref="Name"/> is at most 36 UTF-16 code units.</summary>
internal sealed record GptPartitionEntry(Guid Type, Guid UniqueId, long FirstLba, long LastLba, ulong Attributes, string Name);

/// <summary>The disk to describe: its GUID, its size, its logical sector size and its partitions.</summary>
internal sealed record GptDiskLayout(Guid DiskGuid, long DiskSizeBytes, int LogicalSectorSize, IReadOnlyList<GptPartitionEntry> Partitions);

/// <summary>A byte run to write at an absolute disk offset.</summary>
internal sealed record GptWrite(long Offset, byte[] Data);

/// <summary>Everything needed to make a disk carry the GPT described by a <see cref="GptDiskLayout"/>.</summary>
internal sealed record GptImage(IReadOnlyList<GptWrite> Writes);
