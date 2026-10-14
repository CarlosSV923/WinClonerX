using System.Buffers.Binary;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.IntegrationTests.Spikes;

namespace WinCopy.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// A VHDX with a Windows-11-like GPT layout, built by <see cref="ReferenceSystemDisk.CreateAsync"/>. The partitions are
/// listed in disk order: ESP, MSR, Windows, Recovery. The drive letters are already removed, so the partitions are
/// reachable only through their volume GUID paths.
/// </summary>
internal sealed class ReferenceDisk
{
    internal ReferenceDisk(
        AttachedVirtualDisk disk,
        IReadOnlyList<PartitionInfo> partitions,
        IReadOnlyDictionary<string, string> manifest)
    {
        Disk = disk;
        Partitions = partitions;
        Manifest = manifest;
    }

    /// <summary>The attached VHDX. The fixture detaches it and deletes the file.</summary>
    public AttachedVirtualDisk Disk { get; }

    /// <summary>ESP, MSR, Windows and Recovery, in disk order, with their state after the letters were removed.</summary>
    public IReadOnlyList<PartitionInfo> Partitions { get; }

    /// <summary>The "Windows" Basic Data partition (NTFS).</summary>
    public PartitionInfo Windows => Partitions[ReferenceSystemDisk.WindowsIndex];

    /// <summary>
    /// SHA-256 (uppercase hex) of each data file, keyed by its path relative to the root of the Windows volume, for example
    /// <c>WinCopyData\file-00.bin</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Manifest { get; }
}

/// <summary>
/// Builds the reference system disk: GPT with ESP (FAT32), MSR, "Windows" (NTFS) and Recovery (NTFS). Every Storage command
/// goes through <see cref="VirtualDiskStorageOps"/>, and the disk is attached only by <see cref="VirtualDiskFixture"/>.
/// </summary>
internal static class ReferenceSystemDisk
{
    /// <summary>Index of the Windows partition in <see cref="ReferenceDisk.Partitions"/>.</summary>
    internal const int WindowsIndex = 2;

    internal const string DataFolderName = "WinCopyData";

    private const long Mib = 1L << 20;
    private const long EspMiB = 100;
    private const long MsrMiB = 16;
    private const long RecoveryMiB = 600;
    private const long SlackMiB = 4;
    private const int DataFileBytes = 1024 * 1024;
    private const int HashBlockBytes = 32;

    internal static readonly Guid EspType = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    internal static readonly Guid MsrType = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
    internal static readonly Guid BasicDataType = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
    internal static readonly Guid RecoveryType = new("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");

    /// <summary>
    /// Size of the "Windows" partition for a disk of <paramref name="totalSize"/>: the total minus ESP (100 MiB), MSR
    /// (16 MiB), Recovery (600 MiB) and 4 MiB of slack for the GPT. Pure arithmetic, no disk is touched.
    /// </summary>
    /// <exception cref="ArgumentException">The size is not a whole number of MiB.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The disk is too small for the layout.</exception>
    internal static long WindowsPartitionBytes(ByteSize totalSize)
    {
        if (totalSize.Bytes % Mib != 0)
        {
            throw new ArgumentException("The disk size must be a whole number of MiB.", nameof(totalSize));
        }

        long reserved = (EspMiB + MsrMiB + RecoveryMiB + SlackMiB) * Mib;
        long windows = totalSize.Bytes - reserved;
        if (windows <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalSize), totalSize, "The disk is too small for the reference system layout.");
        }

        return windows;
    }

    /// <summary>
    /// Attaches a new VHDX of <paramref name="size"/> through <paramref name="fx"/> and builds the layout on it.
    /// <paramref name="dataFiles"/> files of 1 MiB of deterministic content are written to the Windows volume.
    /// </summary>
    internal static async Task<ReferenceDisk> CreateAsync(
        VirtualDiskFixture fx,
        VirtualDiskStorageOps ops,
        ByteSize size,
        int dataFiles)
    {
        ArgumentNullException.ThrowIfNull(fx);
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentOutOfRangeException.ThrowIfNegative(dataFiles);

        // Checks the size before any disk is created.
        long windowsBytes = WindowsPartitionBytes(size);

        AttachedVirtualDisk disk = await fx.CreateAndAttachAsync(size);
        return await Task.Run(() => Build(disk, ops, windowsBytes, dataFiles));
    }

    private static ReferenceDisk Build(AttachedVirtualDisk disk, VirtualDiskStorageOps ops, long windowsBytes, int dataFiles)
    {
        int diskNumber = disk.DiskNumber;
        var lettered = new List<(int PartitionNumber, char Letter)>();

        try
        {
            ops.InitializeGpt(diskNumber);

            PartitionInfo esp = ops.CreateGptPartition(diskNumber, EspMiB * Mib, EspType, assignLetter: true);
            char espLetter = Track(lettered, esp);
            ops.FormatFat32(diskNumber, espLetter, "SYSTEM");

            PartitionInfo msr = ops.CreateGptPartition(diskNumber, MsrMiB * Mib, MsrType, assignLetter: false);

            PartitionInfo windows = ops.CreateGptPartition(diskNumber, windowsBytes, BasicDataType, assignLetter: true);
            char windowsLetter = Track(lettered, windows);
            ops.FormatNtfs(diskNumber, windowsLetter, "Windows");

            PartitionInfo recovery = ops.CreateGptPartition(diskNumber, RecoveryMiB * Mib, RecoveryType, assignLetter: true);
            char recoveryLetter = Track(lettered, recovery);
            ops.FormatNtfs(diskNumber, recoveryLetter, "Recovery");

            Dictionary<string, string> manifest = WriteWindowsVolume(ops, diskNumber, windowsLetter, dataFiles);

            foreach ((int partitionNumber, char letter) in lettered)
            {
                ops.RemoveDriveLetter(diskNumber, partitionNumber, letter);
            }

            lettered.Clear();

            // Read the partitions again, so that the recorded letters are the ones that remain (none).
            var partitions = new List<PartitionInfo>(4);
            foreach (PartitionInfo created in new[] { esp, msr, windows, recovery })
            {
                partitions.Add(ops.GetPartition(diskNumber, created.PartitionNumber));
            }

            return new ReferenceDisk(disk, partitions, manifest);
        }
        catch
        {
            // The original failure is the one that matters, so cleanup only logs its own failures.
            RemoveLettersAfterFailure(ops, diskNumber, lettered);
            throw;
        }
    }

    private static char Track(List<(int PartitionNumber, char Letter)> lettered, PartitionInfo partition)
    {
        char letter = partition.DriveLetter ?? throw new StorageCommandException(
            $"Partition {partition.PartitionNumber} on disk {partition.DiskNumber} got no drive letter.");
        lettered.Add((partition.PartitionNumber, letter));
        return letter;
    }

    private static Dictionary<string, string> WriteWindowsVolume(
        VirtualDiskStorageOps ops,
        int diskNumber,
        char windowsLetter,
        int dataFiles)
    {
        ops.AssertLetterOnDisk(windowsLetter, diskNumber);
        string root = $"{windowsLetter}:\\";

        string configFolder = Path.Combine(root, "Windows", "System32", "config");
        Directory.CreateDirectory(configFolder);
        File.WriteAllBytes(
            Path.Combine(configFolder, "SYSTEM"),
            Encoding.ASCII.GetBytes("WinCopy reference SYSTEM hive (test placeholder, not a registry hive)"));

        string dataFolder = Path.Combine(root, DataFolderName);
        Directory.CreateDirectory(dataFolder);

        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < dataFiles; index++)
        {
            string relativePath = Path.Combine(DataFolderName, $"file-{index:D2}.bin");
            byte[] content = DataFileContent(index);
            File.WriteAllBytes(Path.Combine(root, relativePath), content);
            manifest[relativePath] = Convert.ToHexString(SHA256.HashData(content));
        }

        return manifest;
    }

    /// <summary>Deterministic, non-compressible content: a SHA-256 chain seeded with the file index.</summary>
    private static byte[] DataFileContent(int index)
    {
        byte[] content = new byte[DataFileBytes];
        Span<byte> seed = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(seed, index);

        for (int block = 0; block * HashBlockBytes < DataFileBytes; block++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(seed[4..], block);
            SHA256.HashData(seed).CopyTo(content.AsSpan(block * HashBlockBytes));
        }

        return content;
    }

    private static void RemoveLettersAfterFailure(
        VirtualDiskStorageOps ops,
        int diskNumber,
        List<(int PartitionNumber, char Letter)> lettered)
    {
        foreach ((int partitionNumber, char letter) in lettered)
        {
            try
            {
                ops.RemoveDriveLetter(diskNumber, partitionNumber, letter);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or Win32Exception)
            {
                Console.Error.WriteLine($"[ReferenceSystemDisk] Could not remove drive letter {letter}: {ex.Message}");
            }
        }

        lettered.Clear();
    }
}
