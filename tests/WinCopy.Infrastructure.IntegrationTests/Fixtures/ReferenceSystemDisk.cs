using System.Buffers.Binary;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.IntegrationTests.Spikes;

namespace WinCopy.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// A VHDX with a Windows-11-like GPT layout, built by <see cref="ReferenceSystemDisk.CreateAsync"/>. The partitions are
/// listed in disk order (by offset). The order is not fixed: <c>Initialize-Disk</c> may create a Microsoft Reserved partition
/// before the ESP, so callers find partitions by their GPT type, never by index. The drive letters are already removed, so the
/// partitions are reachable only through their volume GUID paths.
/// </summary>
internal sealed class ReferenceDisk
{
    private readonly IReadOnlyList<ListedPartition> _layout;

    internal ReferenceDisk(
        AttachedVirtualDisk disk,
        IReadOnlyList<ListedPartition> layout,
        IReadOnlyDictionary<string, string> manifest)
    {
        Disk = disk;
        _layout = layout;
        Manifest = manifest;
    }

    /// <summary>The attached VHDX. The fixture detaches it and deletes the file.</summary>
    public AttachedVirtualDisk Disk { get; }

    /// <summary>Every partition in disk order, with its GPT type, after the letters were removed.</summary>
    public IReadOnlyList<ListedPartition> Layout => _layout;

    /// <summary>Every partition in disk order, with its state after the letters were removed.</summary>
    public IReadOnlyList<PartitionInfo> Partitions => _layout.Select(listed => listed.Partition).ToArray();

    /// <summary>The "Windows" Basic Data partition (NTFS). Found by its GPT type.</summary>
    public PartitionInfo Windows => PartitionOfType(ReferenceSystemDisk.BasicDataType, "Basic Data (Windows)");

    /// <summary>The Recovery partition (NTFS). Found by its GPT type.</summary>
    public PartitionInfo Recovery => PartitionOfType(ReferenceSystemDisk.RecoveryType, "Recovery");

    /// <summary>
    /// SHA-256 (uppercase hex) of each data file, keyed by its path relative to the root of the Windows volume, for example
    /// <c>WinCopyData\file-00.bin</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> Manifest { get; }

    private PartitionInfo PartitionOfType(Guid type, string name)
    {
        ListedPartition[] matches = _layout.Where(listed => listed.GptType == type).ToArray();
        return matches.Length == 1
            ? matches[0].Partition
            : throw new InvalidOperationException($"The reference disk must hold exactly one {name} partition; it holds {matches.Length}.");
    }
}

/// <summary>
/// Builds the reference system disk: GPT with ESP (FAT32), MSR, "Windows" (NTFS) and Recovery (NTFS). Every Storage command
/// goes through <see cref="VirtualDiskStorageOps"/>, and the disk is attached only by <see cref="VirtualDiskFixture"/>.
/// </summary>
internal static class ReferenceSystemDisk
{
    internal const string DataFolderName = "WinCopyData";

    private const long Mib = 1L << 20;
    private const long EspMiB = 100;
    private const long MsrMiB = 16;
    private const long RecoveryMiB = 600;
    private const long SlackMiB = 4;
    private const long WindowsSlackMiB = 2;
    private const int ExpectedPartitionCount = 4;
    private const int DataFileBytes = 1024 * 1024;
    private const int HashBlockBytes = 32;

    internal static readonly Guid EspType = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    internal static readonly Guid MsrType = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
    internal static readonly Guid BasicDataType = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
    internal static readonly Guid RecoveryType = new("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");

    /// <summary>
    /// Lower bound of the "Windows" partition size for a disk of <paramref name="totalSize"/>: the total minus ESP (100 MiB),
    /// MSR (16 MiB), Recovery (600 MiB) and 4 MiB of slack for the GPT. It is only a pre-check that the disk is big enough. It is
    /// not the size that is created: <see cref="Build"/> sizes the partition from the real free space with
    /// <see cref="WindowsBytesFromFreeExtent"/>. Pure arithmetic, no disk is touched.
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
    /// The "Windows" partition size for a disk whose largest free extent is <paramref name="freeExtentBytes"/>, measured after the
    /// ESP and the MSR exist. Recovery takes 600 MiB at the end, and 2 MiB stay free. The result is rounded down to a whole MiB.
    /// Pure arithmetic.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The free extent cannot hold a Windows partition of at least 1 MiB.</exception>
    internal static long WindowsBytesFromFreeExtent(long freeExtentBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(freeExtentBytes);

        long usable = freeExtentBytes - (RecoveryMiB + WindowsSlackMiB) * Mib;
        long windows = usable <= 0 ? 0 : usable / Mib * Mib;
        if (windows <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(freeExtentBytes),
                freeExtentBytes,
                "The free space after the ESP and MSR cannot hold the Windows and Recovery partitions.");
        }

        return windows;
    }

    /// <summary>
    /// Attaches a new VHDX of <paramref name="size"/> through <paramref name="fx"/> and builds the layout on it.
    /// <paramref name="dataFiles"/> files of 1 MiB of deterministic content are written to the Windows volume.
    /// </summary>
    /// <param name="includeDummyBcd">
    /// Writes a placeholder <c>EFI\Microsoft\Boot\BCD</c> on the ESP, so that a clone's ESP passes the structural verifier's
    /// BCD check. The placeholder is not a boot store; the boot configurator is faked in the clone tests.
    /// </param>
    internal static async Task<ReferenceDisk> CreateAsync(
        VirtualDiskFixture fx,
        VirtualDiskStorageOps ops,
        ByteSize size,
        int dataFiles,
        bool includeDummyBcd = false)
    {
        ArgumentNullException.ThrowIfNull(fx);
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentOutOfRangeException.ThrowIfNegative(dataFiles);

        // Checks that the disk is big enough before any disk is created.
        _ = WindowsPartitionBytes(size);

        AttachedVirtualDisk disk = await fx.CreateAndAttachAsync(size);
        return await Task.Run(() => Build(disk, ops, dataFiles, includeDummyBcd));
    }

    private static ReferenceDisk Build(AttachedVirtualDisk disk, VirtualDiskStorageOps ops, int dataFiles, bool includeDummyBcd)
    {
        int diskNumber = disk.DiskNumber;
        var lettered = new List<(int PartitionNumber, char Letter)>();

        try
        {
            ops.InitializeGpt(diskNumber);

            // Initialize-Disk may already have created a Microsoft Reserved partition. Reuse it. Create one only when none exists.
            IReadOnlyList<ListedPartition> initial = ops.GetPartitions(diskNumber);
            if (initial.Any(listed => listed.GptType != MsrType))
            {
                throw new InvalidOperationException(
                    "A freshly initialized disk should hold only a Microsoft Reserved partition, but it holds another partition type.");
            }

            if (initial.Count > 1)
            {
                throw new InvalidOperationException($"A freshly initialized disk holds {initial.Count} partitions; at most one is expected.");
            }

            PartitionInfo esp = ops.CreateGptPartition(diskNumber, EspMiB * Mib, EspType, assignLetter: true);
            char espLetter = Track(lettered, esp);
            ops.FormatFat32(diskNumber, espLetter, "SYSTEM");
            if (includeDummyBcd)
            {
                WriteDummyBcd(ops, diskNumber, espLetter);
            }

            PartitionInfo msr = initial.Count == 1
                ? initial[0].Partition
                : ops.CreateGptPartition(diskNumber, MsrMiB * Mib, MsrType, assignLetter: false);

            // The Windows partition takes the room that is left after the ESP and the MSR, minus Recovery and the slack.
            long windowsBytes = WindowsBytesFromFreeExtent(ops.LargestFreeExtent(diskNumber));

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

            // Read the whole table again, so that the layout is what the disk holds now, in disk order.
            List<ListedPartition> layout = ops.GetPartitions(diskNumber)
                .OrderBy(listed => listed.Partition.OffsetBytes)
                .ToList();

            if (layout.Count != ExpectedPartitionCount)
            {
                throw new InvalidOperationException(
                    $"The reference disk should hold {ExpectedPartitionCount} partitions; it holds {layout.Count}.");
            }

            foreach (PartitionInfo created in new[] { esp, msr, windows, recovery })
            {
                if (!layout.Any(listed => listed.Partition.GptGuid == created.GptGuid))
                {
                    throw new InvalidOperationException($"Partition {created.PartitionNumber} is missing from the reference layout.");
                }
            }

            return new ReferenceDisk(disk, layout, manifest);
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

    private static void WriteDummyBcd(VirtualDiskStorageOps ops, int diskNumber, char espLetter)
    {
        ops.AssertLetterOnDisk(espLetter, diskNumber);
        string bootFolder = Path.Combine($"{espLetter}:\\", "EFI", "Microsoft", "Boot");
        Directory.CreateDirectory(bootFolder);
        File.WriteAllBytes(
            Path.Combine(bootFolder, "BCD"),
            Encoding.ASCII.GetBytes("WinCopy reference BCD (test placeholder, not a boot store)"));
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
