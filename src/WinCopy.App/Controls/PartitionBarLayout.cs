namespace WinCopy.App.Controls;

/// <summary>
/// Layout math for a partition bar: segment widths proportional to their sizes, with a minimum visible width for every
/// segment that has any size. Pure functions, so the rules are unit tested without a window.
/// </summary>
public static class PartitionBarLayout
{
    /// <summary>
    /// Width of each segment, in the same units as <paramref name="totalWidth"/>. Segments with size zero get width zero.
    /// The widths add up to <paramref name="totalWidth"/>. Every other segment gets at least <paramref name="minWidth"/>,
    /// and the rest of the width is shared proportionally among the segments that are not at the minimum.
    /// If the minimum cannot be honored for everyone (the bar is too narrow), the segments share the width equally.
    /// </summary>
    public static double[] Widths(IReadOnlyList<long> sizes, double totalWidth, double minWidth)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentOutOfRangeException.ThrowIfNegative(totalWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(minWidth);
        if (sizes.Any(size => size < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sizes), "Segment sizes cannot be negative.");
        }

        var widths = new double[sizes.Count];
        var active = Enumerable.Range(0, sizes.Count).Where(index => sizes[index] > 0).ToArray();
        if (active.Length == 0 || totalWidth <= 0)
        {
            return widths;
        }

        if (active.Length * minWidth >= totalWidth)
        {
            foreach (var index in active)
            {
                widths[index] = totalWidth / active.Length;
            }

            return widths;
        }

        // Segments whose proportional share is below the minimum are fixed at the minimum; the rest is recalculated.
        // Each pass fixes at least one segment, and the minimum-width check above guarantees some segment stays free.
        var fixedAtMinimum = new HashSet<int>();
        while (true)
        {
            var remainingWidth = totalWidth - fixedAtMinimum.Count * minWidth;
            var remainingSize = active.Where(index => !fixedAtMinimum.Contains(index)).Sum(index => (double)sizes[index]);

            var belowMinimum = active
                .Where(index => !fixedAtMinimum.Contains(index) && remainingWidth * sizes[index] / remainingSize < minWidth)
                .ToArray();

            if (belowMinimum.Length == 0)
            {
                foreach (var index in active)
                {
                    widths[index] = fixedAtMinimum.Contains(index)
                        ? minWidth
                        : remainingWidth * sizes[index] / remainingSize;
                }

                return widths;
            }

            foreach (var index in belowMinimum)
            {
                fixedAtMinimum.Add(index);
            }
        }
    }
}
