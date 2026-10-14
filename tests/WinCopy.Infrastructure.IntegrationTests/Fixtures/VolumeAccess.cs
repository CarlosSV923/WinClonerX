using System.Security.Cryptography;
using WinCopy.Infrastructure.IntegrationTests.Spikes;

namespace WinCopy.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// Gives a drive letter to a partition of a virtual disk for the duration of a check. The letter is assigned only when the
/// partition has none, and only that letter is removed on <see cref="Dispose"/>. Every command goes through
/// <see cref="VirtualDiskStorageOps"/>, which refuses anything but a registered virtual disk.
/// </summary>
internal sealed class TemporaryDriveLetter : IDisposable
{
    private readonly VirtualDiskStorageOps _ops;
    private readonly int _diskNumber;
    private readonly int _partitionNumber;
    private readonly bool _assignedHere;
    private bool _disposed;

    private TemporaryDriveLetter(VirtualDiskStorageOps ops, int diskNumber, int partitionNumber, char letter, bool assignedHere)
    {
        _ops = ops;
        _diskNumber = diskNumber;
        _partitionNumber = partitionNumber;
        Letter = letter;
        _assignedHere = assignedHere;
    }

    public char Letter { get; }

    /// <summary>Root of the volume, for example <c>E:\</c>.</summary>
    public string Root => $"{Letter}:\\";

    public static TemporaryDriveLetter Acquire(VirtualDiskStorageOps ops, int diskNumber, int partitionNumber)
    {
        ArgumentNullException.ThrowIfNull(ops);

        PartitionInfo partition = ops.GetPartition(diskNumber, partitionNumber);
        bool assignedHere = partition.DriveLetter is null;
        if (assignedHere)
        {
            partition = ops.AssignDriveLetter(diskNumber, partitionNumber);
        }

        char letter = partition.DriveLetter
            ?? throw new StorageCommandException($"Partition {partitionNumber} on disk {diskNumber} has no drive letter.");

        ops.AssertLetterOnDisk(letter, diskNumber);
        return new TemporaryDriveLetter(ops, diskNumber, partitionNumber, letter, assignedHere);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_assignedHere)
        {
            _ops.RemoveDriveLetter(_diskNumber, _partitionNumber, Letter);
        }
    }
}

/// <summary>SHA-256 manifests of files on a mounted volume.</summary>
internal static class VolumeFiles
{
    /// <summary>SHA-256 (uppercase hex) of each file, keyed by its path relative to <paramref name="root"/>.</summary>
    public static Dictionary<string, string> Sha256Manifest(string root, IEnumerable<string> relativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(relativePaths);

        var manifest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string relative in relativePaths)
        {
            using FileStream stream = File.OpenRead(Path.Combine(root, relative));
            manifest[relative] = Convert.ToHexString(SHA256.HashData(stream));
        }

        return manifest;
    }

    /// <summary>Reads a CLEF log that may still be open for writing by the execution log.</summary>
    public static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
