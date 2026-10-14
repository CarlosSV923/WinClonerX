using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.App.ViewModels;
using WinCopy.Domain.Shared;

namespace WinCopy.App.Controls;

/// <summary>
/// Horizontal bar of a disk's partitions, drawn to scale: each segment has its role color, the used part of the segment is
/// shaded, and free space fills what is left of <see cref="DiskSize"/>. Each segment has a tooltip with its size.
/// </summary>
public partial class PartitionBar : UserControl
{
    /// <summary>Narrowest a segment can be drawn, so that small partitions stay visible.</summary>
    private const double MinSegmentWidth = 14;

    private const double SeparatorWidth = 1;

    public static readonly DependencyProperty PartitionsProperty = DependencyProperty.Register(
        nameof(Partitions),
        typeof(IEnumerable),
        typeof(PartitionBar),
        new PropertyMetadata(null, OnLayoutPropertyChanged));

    public static readonly DependencyProperty DiskSizeProperty = DependencyProperty.Register(
        nameof(DiskSize),
        typeof(ByteSize),
        typeof(PartitionBar),
        new PropertyMetadata(default(ByteSize), OnLayoutPropertyChanged));

    public PartitionBar()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
    }

    /// <summary>The partitions to draw, as <see cref="PartitionSegment"/> items.</summary>
    public IEnumerable? Partitions
    {
        get => (IEnumerable?)GetValue(PartitionsProperty);
        set => SetValue(PartitionsProperty, value);
    }

    /// <summary>Size of the disk the bar represents. Space not covered by partitions is drawn as free.</summary>
    public ByteSize DiskSize
    {
        get => (ByteSize)GetValue(DiskSizeProperty);
        set => SetValue(DiskSizeProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((PartitionBar)sender).Redraw();

    private void Redraw()
    {
        Surface.Children.Clear();

        var segments = Partitions is null
            ? Array.Empty<PartitionSegment>()
            : Partitions.OfType<PartitionSegment>().ToArray();
        var usedBytes = segments.Sum(segment => segment.Size.Bytes);
        var scale = Math.Max(DiskSize.Bytes, usedBytes);
        var freeBytes = scale - usedBytes;
        var height = Surface.ActualHeight;
        if (scale <= 0 || height <= 0)
        {
            return;
        }

        // The layout works on every drawn piece: the segments in order, then the free remainder.
        var sizes = segments.Select(segment => segment.Size.Bytes).ToList();
        if (freeBytes > 0)
        {
            sizes.Add(freeBytes);
        }

        var widths = PartitionBarLayout.Widths(sizes, Surface.ActualWidth, MinSegmentWidth);
        var usedBrush = FindBrush(PartitionVisuals.UsedBrushKey);
        double x = 0;

        for (var index = 0; index < sizes.Count; index++)
        {
            var width = widths[index];
            var segment = index < segments.Length ? segments[index] : null;
            if (width <= 0)
            {
                continue;
            }

            if (segment is null)
            {
                AddRectangle(x, width, height, FindBrush(PartitionVisuals.FreeBrushKey), FreeTooltip(freeBytes));
            }
            else
            {
                var tooltip = SegmentTooltip(segment);
                AddRectangle(x, width, height, FindBrush(PartitionVisuals.BrushKeyFor(segment.Role)), tooltip);

                if (segment.Used is { } used && segment.Size.Bytes > 0)
                {
                    var shaded = width * Math.Clamp(used.Bytes / (double)segment.Size.Bytes, 0, 1);
                    AddRectangle(x, shaded, height, usedBrush, tooltip);
                }
            }

            if (index > 0)
            {
                AddRectangle(x, SeparatorWidth, height, FindBrush(PartitionVisuals.SeparatorBrushKey), null);
            }

            x += width;
        }
    }

    private void AddRectangle(double x, double width, double height, Brush brush, string? tooltip)
    {
        var rectangle = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = brush,
            ToolTip = tooltip,
        };

        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, 0);
        Surface.Children.Add(rectangle);
    }

    private static string SegmentTooltip(PartitionSegment segment) => segment.Used is { } used
        ? Loc.Format("Partition_Tooltip", segment.Label, SizeText.Format(used), SizeText.Format(segment.Size))
        : Loc.Format("Partition_Tooltip_NoUsage", segment.Label, SizeText.Format(segment.Size));

    private static string FreeTooltip(long freeBytes) =>
        Loc.Format("Partition_Tooltip_NoUsage", Labels.FreeSpace, SizeText.Format(new ByteSize(freeBytes)));

    private Brush FindBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;
}
