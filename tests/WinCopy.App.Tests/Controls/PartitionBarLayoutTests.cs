using Shouldly;
using WinCopy.App.Controls;
using Xunit;

namespace WinCopy.App.Tests.Controls;

public sealed class PartitionBarLayoutTests
{
    [Fact]
    public void WidthsAddUpToTheTotalWidth()
    {
        var widths = PartitionBarLayout.Widths([100, 37, 4096, 1, 999_999], 640, 14);

        widths.Sum().ShouldBe(640, tolerance: 1e-6);
    }

    [Fact]
    public void WidthsAreProportionalWhenNoSegmentNeedsTheMinimum()
    {
        var widths = PartitionBarLayout.Widths([1, 1, 2], 400, 0);

        widths.ShouldBe([100, 100, 200]);
    }

    [Fact]
    public void MinimumWidthIsAppliedToSmallSegments()
    {
        var widths = PartitionBarLayout.Widths([1, 999], 1000, 50);

        widths[0].ShouldBe(50, tolerance: 1e-9);
        widths[1].ShouldBe(950, tolerance: 1e-9);
    }

    [Fact]
    public void EveryVisibleSegmentGetsAtLeastTheMinimumWhenItFits()
    {
        var sizes = new long[] { 1, 2, 3, 500, 100_000 };

        var widths = PartitionBarLayout.Widths(sizes, 800, 40);

        widths.ShouldAllBe(width => width >= 40 - 1e-9);
        widths.Sum().ShouldBe(800, tolerance: 1e-6);
    }

    [Fact]
    public void SeveralSmallSegmentsAreFixedAtTheMinimumTogether()
    {
        var widths = PartitionBarLayout.Widths([1, 1, 1, 10_000], 500, 30);

        widths[0].ShouldBe(30, tolerance: 1e-9);
        widths[1].ShouldBe(30, tolerance: 1e-9);
        widths[2].ShouldBe(30, tolerance: 1e-9);
        widths[3].ShouldBe(410, tolerance: 1e-9);
    }

    [Fact]
    public void ZeroSizedSegmentsGetZeroWidthAndNoMinimum()
    {
        var widths = PartitionBarLayout.Widths([0, 300, 0, 100], 400, 20);

        widths.ShouldBe([0, 300, 0, 100]);
    }

    [Fact]
    public void WhenTheMinimumCannotBeHonoredTheSegmentsShareTheWidthEqually()
    {
        var widths = PartitionBarLayout.Widths([1, 1000, 1000], 90, 40);

        widths.ShouldBe([30, 30, 30]);
        widths.Sum().ShouldBe(90, tolerance: 1e-9);
    }

    [Fact]
    public void NoSegmentsOrNoWidthGiveEmptyOrZeroWidths()
    {
        PartitionBarLayout.Widths([], 500, 10).ShouldBeEmpty();
        PartitionBarLayout.Widths([10, 20], 0, 10).ShouldBe([0, 0]);
    }

    [Fact]
    public void NegativeSizesAreRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PartitionBarLayout.Widths([10, -1], 100, 0));
    }
}
