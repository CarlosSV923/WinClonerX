using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>Byte placement of the NTFS tail and of the backup boot sector on the source volume.</summary>
/// <param name="TailOffset">First byte after the last whole cluster: <c>TotalClusters * BytesPerCluster</c>.</param>
/// <param name="TailLength">Bytes of the tail, rounded down to whole sectors. Zero when there is no tail.</param>
/// <param name="BackupOffset">Offset of the NTFS backup boot sector: <c>NumberSectors * BytesPerSector</c>.</param>
internal readonly record struct VolumeTailLayout(long TailOffset, long TailLength, long BackupOffset);

/// <summary>
/// Pure planning for the raw copy of an NTFS volume (research R2): which byte ranges of the volume hold data that must be
/// copied. It performs no I/O. The bitmap comes from FSCTL_GET_VOLUME_BITMAP and the device length from
/// IOCTL_DISK_GET_LENGTH_INFO; both are read by the adapter.
/// </summary>
internal static class VolumeAllocationPlanner
{
    /// <summary>Minimum size of the boot region at the start of the volume: sector 0 plus the NTFS boot area.</summary>
    private const long MinimumBootRegionBytes = 8192;

    /// <summary>
    /// Decodes a volume bitmap into runs of used clusters. Bit <c>i</c> of byte <c>j</c> is LCN
    /// <c>startingLcn + j * 8 + i</c>, least significant bit first. Bits for clusters at or above
    /// <paramref name="totalClusters"/> are ignored.
    /// </summary>
    public static IReadOnlyList<(long StartLcn, long Count)> UsedRuns(
        ReadOnlySpan<byte> bitmap, long startingLcn, long totalClusters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startingLcn);
        ArgumentOutOfRangeException.ThrowIfNegative(totalClusters);

        var runs = new List<(long StartLcn, long Count)>();
        long bitCount = (long)bitmap.Length * 8;
        long limit = Math.Min(bitCount, Math.Max(0, totalClusters - startingLcn));

        long runStart = -1;
        for (long bit = 0; bit < limit; bit++)
        {
            bool used = (bitmap[(int)(bit >> 3)] & (1 << (int)(bit & 7))) != 0;
            if (used)
            {
                if (runStart < 0)
                {
                    runStart = startingLcn + bit;
                }
            }
            else if (runStart >= 0)
            {
                runs.Add((runStart, startingLcn + bit - runStart));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            runs.Add((runStart, startingLcn + limit - runStart));
        }

        return runs;
    }

    /// <summary>
    /// Plans the byte ranges to copy from a volume: the boot region, each used run, the tail after the last whole cluster,
    /// and the NTFS backup boot sector. Ranges are sorted and merged where they overlap or touch.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The backup boot sector lies beyond <paramref name="sourceDeviceLength"/>.
    /// </exception>
    public static VolumeAllocation Plan(
        IReadOnlyList<(long StartLcn, long Count)> runs,
        long bytesPerCluster,
        long totalClusters,
        long numberSectors,
        long bytesPerSector,
        long sourceDeviceLength)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerCluster);
        ArgumentOutOfRangeException.ThrowIfNegative(totalClusters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numberSectors);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerSector);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceDeviceLength);

        if (bytesPerCluster % bytesPerSector != 0)
        {
            throw new ArgumentException(
                $"The cluster size {bytesPerCluster} must be a multiple of the sector size {bytesPerSector}.", nameof(bytesPerCluster));
        }

        var ranges = new List<(long Start, long End)>
        {
            (0, Math.Max(bytesPerCluster, MinimumBootRegionBytes)),
        };

        foreach (var (startLcn, count) in runs)
        {
            if (startLcn < 0 || count <= 0 || count > totalClusters - startLcn)
            {
                throw new ArgumentException(
                    $"The run at LCN {startLcn} with {count} clusters lies outside the {totalClusters} clusters of the volume.",
                    nameof(runs));
            }

            long start = checked(startLcn * bytesPerCluster);
            ranges.Add((start, checked(start + count * bytesPerCluster)));
        }

        VolumeTailLayout tail = ComputeTailAndBackup(
            totalClusters, bytesPerCluster, numberSectors, bytesPerSector, sourceDeviceLength);

        if (tail.TailLength > 0)
        {
            ranges.Add((tail.TailOffset, checked(tail.TailOffset + tail.TailLength)));
        }

        ranges.Add((tail.BackupOffset, checked(tail.BackupOffset + bytesPerSector)));

        ranges.Sort();

        var merged = new List<Extent>();
        long currentStart = ranges[0].Start;
        long currentEnd = ranges[0].End;
        for (int i = 1; i < ranges.Count; i++)
        {
            (long start, long end) = ranges[i];
            if (start <= currentEnd)
            {
                currentEnd = Math.Max(currentEnd, end);
            }
            else
            {
                merged.Add(new Extent(currentStart, currentEnd - currentStart));
                currentStart = start;
                currentEnd = end;
            }
        }

        merged.Add(new Extent(currentStart, currentEnd - currentStart));

        return new VolumeAllocation(new ByteSize(bytesPerCluster), merged);
    }

    /// <summary>
    /// Computes the tail and the backup boot sector placement. The tail runs from the end of the last whole cluster to the
    /// end of the volume, limited by the source device length and rounded down to whole sectors. The backup boot sector
    /// sits right after the volume and must be readable from the source device.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The backup boot sector would end beyond <paramref name="sourceDeviceLength"/>.
    /// </exception>
    internal static VolumeTailLayout ComputeTailAndBackup(
        long totalClusters,
        long bytesPerCluster,
        long numberSectors,
        long bytesPerSector,
        long sourceDeviceLength)
    {
        long tailOffset = checked(totalClusters * bytesPerCluster);
        long volumeBytes = checked(numberSectors * bytesPerSector);
        long backupOffset = volumeBytes;
        long backupEnd = checked(backupOffset + bytesPerSector);

        if (backupEnd > sourceDeviceLength)
        {
            throw new InvalidOperationException(
                "backup boot sector beyond source device; enable FSCTL_ALLOW_EXTENDED_DASD_IO or check geometry");
        }

        long usableEnd = Math.Min(volumeBytes, sourceDeviceLength);
        usableEnd -= usableEnd % bytesPerSector;

        long tailLength = Math.Max(0, usableEnd - tailOffset);
        tailLength -= tailLength % bytesPerSector;

        return new VolumeTailLayout(tailOffset, tailLength, backupOffset);
    }
}
