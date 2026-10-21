using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.App.ViewModels;

/// <summary>
/// One segment of a partition bar. <paramref name="Used"/> is null when the partition has no file system to measure.
/// </summary>
public sealed record PartitionSegment(PartitionRole Role, ByteSize Size, ByteSize? Used, string Label);

/// <summary>Data for a partition bar: its segments and the disk size they are drawn against (the remainder is free space).</summary>
public sealed record PartitionBarData(IReadOnlyList<PartitionSegment> Segments, ByteSize DiskSize)
{
    public string AccessibleText
    {
        get
        {
            var parts = Segments.Select(segment => segment.Used is { } used
                ? Loc.Format("PartitionBar_PartitionUsed", segment.Label, SizeText.Format(used), SizeText.Format(segment.Size))
                : Loc.Format("PartitionBar_Partition", segment.Label, SizeText.Format(segment.Size)))
                .ToList();
            var freeBytes = Math.Max(DiskSize.Bytes - Segments.Sum(segment => segment.Size.Bytes), 0);
            if (freeBytes > 0)
            {
                parts.Add(Loc.Format("PartitionBar_Free", SizeText.Format(new ByteSize(freeBytes))));
            }

            return string.Join(" ", parts);
        }
    }
}

/// <summary>Size change of one partition between the source and the destination.</summary>
/// <param name="Label">Localized partition name.</param>
/// <param name="DeltaBytes">Destination length minus source length.</param>
/// <param name="Text">Localized line such as "Windows: +40 GiB".</param>
public sealed record PartitionChange(string Label, long DeltaBytes, string Text);

/// <summary>Plan summary, already formatted for display.</summary>
public sealed record PlanSummary(string DataToCopy, string EstimatedDuration);
