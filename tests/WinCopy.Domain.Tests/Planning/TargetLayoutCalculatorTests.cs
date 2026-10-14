using Shouldly;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;
using static WinCopy.Domain.Tests.Planning.PlanningFixtures;

namespace WinCopy.Domain.Tests.Planning;

public sealed class TargetLayoutCalculatorTests
{
    private const int ExpectedWindowsIndex = 2;

    [Fact]
    public void SameSizeTargetReproducesTheSourceLayout()
    {
        var source = SystemDisk();

        var extents = Calculate(source, TargetDisk(size: 256 * Gib));

        extents.ShouldBe(source.Partitions.Select(p => p.Extent).ToArray());
    }

    [Fact]
    public void LargerTargetGivesTheExtraSpaceToWindowsOnly()
    {
        var source = SystemDisk();

        var extents = Calculate(source, TargetDisk(size: 512 * Gib));

        extents[0].ShouldBe(source.Partitions[0].Extent, "EFI keeps its offset and length");
        extents[1].ShouldBe(source.Partitions[1].Extent, "MSR keeps its offset and length");
        extents[ExpectedWindowsIndex].Offset.ShouldBe(WindowsStart);
        extents[ExpectedWindowsIndex].Length.ShouldBe(source.Partitions[ExpectedWindowsIndex].Extent.Length + 256 * Gib);
    }

    [Fact]
    public void RecoveryIsMovedToTheEndWithItsOriginalLength()
    {
        var source = SystemDisk();

        var extents = Calculate(source, TargetDisk(size: 512 * Gib));

        var recovery = extents[^1];
        recovery.Length.ShouldBe(RecoveryLength);
        recovery.Offset.ShouldBe(512 * Gib - Mib - RecoveryLength);
        recovery.Offset.ShouldBeGreaterThan(extents[ExpectedWindowsIndex].Offset);
    }

    [Fact]
    public void PartitionsKeepTheSourceOrder()
    {
        var extents = Calculate(SystemDisk(), TargetDisk(size: 512 * Gib));

        extents.Select(e => e.Offset).ShouldBe(extents.Select(e => e.Offset).Order());
        extents.Length.ShouldBe(4);
    }

    [Fact]
    public void EveryOffsetIsAlignedToOneMebibyte()
    {
        var extents = Calculate(SystemDisk(), TargetDisk(size: 512 * Gib));

        extents.Select(e => e.Offset % Mib).ShouldAllBe(remainder => remainder == 0);
    }

    [Fact]
    public void ExtentsDoNotOverlapAndStayInsideTheUsableArea()
    {
        var target = TargetDisk(size: 512 * Gib);

        var extents = Calculate(SystemDisk(), target);

        AssertNoOverlaps(extents);
        extents[^1].End.ShouldBeLessThanOrEqualTo(target.Size.Bytes - GptBackupEntriesBytes - 512);
    }

    [Fact]
    public void UnalignedWindowsOffsetIsAlignedUpToOneMebibyte()
    {
        var system = SystemDisk();
        var windows = system.Partitions[ExpectedWindowsIndex];
        var source = WithPartitions(system, system.Partitions.Select((p, i) => i == ExpectedWindowsIndex
            ? p with { Extent = new Extent(windows.Extent.Offset + 4096, windows.Extent.Length - 4096) }
            : p));

        var extents = Calculate(source, TargetDisk(size: 512 * Gib));

        extents[ExpectedWindowsIndex].Offset.ShouldBe(WindowsStart + Mib);
        AssertNoOverlaps(extents);
    }

    [Fact]
    public void ExtraDataPartitionAfterWindowsIsPlacedBeforeRecoveryAtTheEnd()
    {
        var source = SystemDisk(dataLength: 50 * Gib);
        var target = TargetDisk(size: 512 * Gib);

        var extents = Calculate(source, target);

        var data = extents[3];
        var recovery = extents[4];
        data.Length.ShouldBe(50 * Gib, "the data partition keeps its length");
        data.End.ShouldBe(recovery.Offset, "data sits right before recovery, which is at the end");
        extents[ExpectedWindowsIndex].End.ShouldBe(data.Offset, "windows grows up to the data partition");
        extents[ExpectedWindowsIndex].Length.ShouldBe(source.Partitions[ExpectedWindowsIndex].Extent.Length + 256 * Gib);
        AssertNoOverlaps(extents);
    }

    [Fact]
    public void FourKibibyteSectorTargetKeepsTheLayoutAlignedToFourKibibytes()
    {
        var source = SystemDisk(sector: 4096);
        var target = TargetDisk(size: 512 * Gib, sector: 4096);

        var extents = Calculate(source, target);

        extents.Select(e => e.Offset % 4096).ShouldAllBe(remainder => remainder == 0);
        extents[^1].End.ShouldBeLessThanOrEqualTo(target.Size.Bytes - GptBackupEntriesBytes - 4096);
        extents[ExpectedWindowsIndex].Length.ShouldBe(source.Partitions[ExpectedWindowsIndex].Extent.Length + 256 * Gib);
        AssertNoOverlaps(extents);
    }

    [Fact]
    public void SmallerTargetFailsWithTheShortfallAsDetail()
    {
        // Windows ocupa 256 GiB - 868 MiB en el origen; en 200 GiB faltan exactamente 56 GiB.
        var result = TargetLayoutCalculator.Calculate(SystemDisk(), TargetDisk(size: 200 * Gib));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("TargetTooSmall");
        result.Error.Detail.ShouldBe((56 * Gib).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TargetThatCannotHoldTheTrailingPartitionsFailsAsTooSmall()
    {
        var result = TargetLayoutCalculator.Calculate(SystemDisk(), TargetDisk(size: 64 * Mib));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("TargetTooSmall");
    }

    [Fact]
    public void SourceWithoutWindowsPartitionIsAnInvalidLayout()
    {
        var system = SystemDisk();
        var source = WithPartitions(system, system.Partitions.Select(p => p.Role == PartitionRole.Windows
            ? p with { Volume = p.Volume! with { IsWindowsVolume = false } }
            : p));

        var result = TargetLayoutCalculator.Calculate(source, TargetDisk(size: 512 * Gib));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("InvalidLayout");
    }

    [Fact]
    public void PartitionsThatAlignUpIntoEachOtherAreAnInvalidLayout()
    {
        // La EFI empieza en 1 MiB + 512 B; alineada hacia arriba termina después de donde empieza la MSR.
        var system = SystemDisk();
        var source = WithPartitions(system, system.Partitions.Select((p, i) => i switch
        {
            0 => p with { Extent = new Extent(Mib + 512, 100 * Mib - 512) },
            _ => p,
        }));

        var result = TargetLayoutCalculator.Calculate(source, TargetDisk(size: 512 * Gib));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("InvalidLayout");
    }

    private static Extent[] Calculate(Disk source, Disk target)
    {
        var result = TargetLayoutCalculator.Calculate(source, target);
        result.IsSuccess.ShouldBeTrue(result.IsSuccess ? null : result.Error.Code);
        return result.Value.ToArray();
    }

    private static void AssertNoOverlaps(Extent[] extents)
    {
        for (var i = 1; i < extents.Length; i++)
        {
            extents[i].Overlaps(extents[i - 1]).ShouldBeFalse($"extent {i} overlaps extent {i - 1}");
            extents[i].Offset.ShouldBeGreaterThanOrEqualTo(extents[i - 1].End);
        }
    }
}
