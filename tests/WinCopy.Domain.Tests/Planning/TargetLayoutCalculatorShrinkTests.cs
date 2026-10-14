using System.Globalization;
using Shouldly;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;
using static WinCopy.Domain.Tests.Planning.PlanningFixtures;
using static WinCopy.Domain.Tests.Planning.ShrinkFixtures;

namespace WinCopy.Domain.Tests.Planning;

public sealed class TargetLayoutCalculatorShrinkTests
{
    private const int WindowsIndex = 2;

    [Fact]
    public void ShrunkLengthIsTheUsedSpacePlusMarginWhenTheMinimumIsSmaller()
    {
        // 40 GiB ocupados + 10 GiB de margen = 50 GiB; el mínimo de 45 GiB no supera ese valor.
        var decision = Decide(SystemDisk(), TargetDisk(size: SmallTargetSize), ByteSize.FromGiB(45));

        decision.Strategy.ShouldBe(SizeStrategy.ShrinkSource);
        decision.SourceShrink!.ShrunkLength.ShouldBe(ByteSize.FromGiB(50));
    }

    [Fact]
    public void ShrunkLengthIsTheMinimumWhenItIsLargerThanTheUsedSpacePlusMargin()
    {
        var decision = Decide(SystemDisk(), TargetDisk(size: SmallTargetSize), ByteSize.FromGiB(60));

        decision.SourceShrink!.ShrunkLength.ShouldBe(ByteSize.FromGiB(60));
        decision.SourceShrink!.MinimumSupported.ShouldBe(ByteSize.FromGiB(60));
    }

    [Fact]
    public void ShrunkLengthIsRoundedUpToOneMebibyte()
    {
        var decision = Decide(SystemDisk(), TargetDisk(size: SmallTargetSize), ByteSize.FromGiB(60) + new ByteSize(1));

        var shrunk = decision.SourceShrink!.ShrunkLength;
        shrunk.ShouldBe(ByteSize.FromGiB(60) + ByteSize.FromMiB(1));
        (shrunk.Bytes % Mib).ShouldBe(0L);
    }

    [Fact]
    public void ShrinkKeepsTheLengthOfEveryPartitionExceptWindows()
    {
        var source = SystemDisk();

        var decision = Decide(source, TargetDisk(size: SmallTargetSize), ByteSize.FromGiB(45));

        decision.Extents[0].ShouldBe(source.Partitions[0].Extent, "EFI keeps its offset and length (FR-021b)");
        decision.Extents[1].ShouldBe(source.Partitions[1].Extent, "MSR keeps its offset and length (FR-021b)");
        decision.Extents[3].Length.ShouldBe(RecoveryLength, "Recovery keeps its length (FR-021b)");
    }

    [Fact]
    public void ShrinkGivesWindowsAllTheRemainingSpaceOfTheTarget()
    {
        var decision = Decide(SystemDisk(), TargetDisk(size: SmallTargetSize), ByteSize.FromGiB(45));

        decision.Extents[WindowsIndex].Offset.ShouldBe(WindowsStart);
        decision.Extents[WindowsIndex].Length.ShouldBe(SmallTargetWindowsLength);
        AssertNoOverlaps(decision.Extents.ToArray());
    }

    [Fact]
    public void ShrinkStepRecordsTheSourceWindowsPartitionAndItsLengths()
    {
        var decision = Decide(SystemDisk(), TargetDisk(size: SmallTargetSize), ByteSize.FromGiB(45));

        var step = decision.SourceShrink!;
        step.Partition.ShouldBe(SourcePartitionGuid(3));
        step.OriginalLength.ShouldBe(new ByteSize(SourceWindowsLength));
        step.ShrunkLength.Bytes.ShouldBeLessThan(step.OriginalLength.Bytes);
    }

    [Fact]
    public void ShrinkToExactlyTheWindowsSpaceIsAllowed()
    {
        var minimum = new ByteSize(SmallTargetWindowsLength);

        var decision = Decide(SystemDisk(), TargetDisk(size: SmallTargetSize), minimum);

        decision.SourceShrink!.ShrunkLength.ShouldBe(minimum);
        decision.Extents[WindowsIndex].Length.ShouldBe(SmallTargetWindowsLength);
    }

    [Fact]
    public void WithoutAKnownMinimumTheSmallerTargetIsTooSmallAndNotShrunk()
    {
        var result = TargetLayoutCalculator.Decide(SystemDisk(), TargetDisk(size: SmallTargetSize), CapacityPolicy.Default, null);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(TargetLayoutCalculator.TargetTooSmallCode);
        result.Error.Detail.ShouldBe((56 * Gib).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ASameOrLargerTargetIsNotShrunkEvenWithAKnownMinimum()
    {
        var source = SystemDisk();
        var target = TargetDisk(size: 512 * Gib);

        var decision = Decide(source, target, ByteSize.FromGiB(45));

        decision.Strategy.ShouldBe(SizeStrategy.SameOrLarger);
        decision.SourceShrink.ShouldBeNull();
        decision.Extents.ShouldBe(TargetLayoutCalculator.Calculate(source, target).Value);
    }

    [Fact]
    public void ShrinkIsInsufficientWhenTheMinimumIsTheLimitingTerm()
    {
        // El mínimo de 201 GiB supera los 200 GiB - 868 MiB que caben: faltan 1 GiB + 868 MiB.
        var result = TargetLayoutCalculator.Decide(SystemDisk(), TargetDisk(size: SmallTargetSize), CapacityPolicy.Default, ByteSize.FromGiB(201));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(TargetLayoutCalculator.ShrinkInsufficientCode);
        result.Error.Detail.ShouldBe((ByteSize.FromGiB(1) + ByteSize.FromMiB(868)).Bytes.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ShrinkFailsAsTooSmallWhenTheMarginIsTheLimitingTerm()
    {
        // 150 GiB ocupados exigen 165 GiB con el margen; el destino de 160 GiB deja 160 GiB - 868 MiB.
        var result = TargetLayoutCalculator.Decide(
            SystemDisk(windowsUsed: 150 * Gib),
            TargetDisk(size: 160 * Gib),
            CapacityPolicy.Default,
            ByteSize.FromGiB(10));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(TargetLayoutCalculator.TargetTooSmallCode);
        result.Error.Detail.ShouldBe((ByteSize.FromGiB(5) + ByteSize.FromMiB(868)).Bytes.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AMarginShortfallWithoutAKnownMinimumIsTooSmallWithTheMarginAsShortfall()
    {
        // 40 GiB ocupados con un margen fijo de 300 GiB exigen 340 GiB; el origen solo tiene 256 GiB - 868 MiB.
        var policy = new CapacityPolicy(0m, ByteSize.FromGiB(300));

        var result = TargetLayoutCalculator.Decide(SystemDisk(), TargetDisk(size: 256 * Gib), policy, null);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe(TargetLayoutCalculator.TargetTooSmallCode);
        result.Error.Detail.ShouldBe((ByteSize.FromGiB(84) + ByteSize.FromMiB(868)).Bytes.ToString(CultureInfo.InvariantCulture));
    }

    private static LayoutDecision Decide(Disk source, Disk target, ByteSize? minimum)
    {
        var result = TargetLayoutCalculator.Decide(source, target, CapacityPolicy.Default, minimum);
        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? null : result.Error.Code);
        return result.Value;
    }

    private static void AssertNoOverlaps(Extent[] extents)
    {
        for (var i = 1; i < extents.Length; i++)
        {
            extents[i].Overlaps(extents[i - 1]).ShouldBeFalse($"extent {i} overlaps extent {i - 1}");
        }
    }
}
