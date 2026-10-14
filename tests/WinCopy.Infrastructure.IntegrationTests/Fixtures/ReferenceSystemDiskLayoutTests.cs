using Shouldly;
using WinCopy.Domain.Shared;
using Xunit;

namespace WinCopy.Infrastructure.IntegrationTests.Fixtures;

// Pure arithmetic of the reference layout. No disk is touched, no process is started and no elevation is needed.
public sealed class ReferenceSystemDiskLayoutTests
{
    private const long Mib = 1L << 20;

    // ESP 100 + MSR 16 + Recovery 600 + slack 4, in MiB.
    private const long ReservedMiB = 720;

    [Fact]
    public void WindowsPartitionForTwoGiBIsOneThousandThreeHundredTwentyEightMiB()
    {
        long windows = ReferenceSystemDisk.WindowsPartitionBytes(ByteSize.FromGiB(2));

        windows.ShouldBe(1328 * Mib);
        windows.ShouldBe(1_392_508_928L);
    }

    [Fact]
    public void WindowsPartitionForTwentyGiBIsNineteenThousandSevenHundredSixtyMiB()
    {
        long windows = ReferenceSystemDisk.WindowsPartitionBytes(ByteSize.FromGiB(20));

        windows.ShouldBe(19760 * Mib);
        windows.ShouldBe(20_719_861_760L);
    }

    [Theory]
    [InlineData(2048L)]
    [InlineData(4096L)]
    [InlineData(20480L)]
    [InlineData(30720L)]
    public void LayoutSumsBackToTheDiskSize(long totalMiB)
    {
        long windows = ReferenceSystemDisk.WindowsPartitionBytes(ByteSize.FromMiB(totalMiB));

        (windows + ReservedMiB * Mib).ShouldBe(totalMiB * Mib);
    }

    [Fact]
    public void DiskThatHoldsOnlyTheReservedPartitionsIsRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ReferenceSystemDisk.WindowsPartitionBytes(ByteSize.FromMiB(ReservedMiB)));
    }

    [Fact]
    public void DiskSizeThatIsNotAWholeNumberOfMiBIsRejected()
    {
        Should.Throw<ArgumentException>(() => ReferenceSystemDisk.WindowsPartitionBytes(new ByteSize((2L << 30) + 1)));
    }
}
