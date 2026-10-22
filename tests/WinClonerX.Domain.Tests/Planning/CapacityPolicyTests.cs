using Shouldly;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;

namespace WinClonerX.Domain.Tests.Planning;

public sealed class CapacityPolicyTests
{
    [Fact]
    public void DefaultMarginIsTenPercentWithATenGibMinimum()
    {
        CapacityPolicy.Default.Percentage.ShouldBe(0.10m);
        CapacityPolicy.Default.Minimum.ShouldBe(ByteSize.FromGiB(10));
    }

    [Fact]
    public void MarginIsTheMinimumWhenTenPercentIsSmaller()
    {
        CapacityPolicy.Default.Margin(ByteSize.FromGiB(40)).ShouldBe(ByteSize.FromGiB(10));
        CapacityPolicy.Default.Margin(ByteSize.FromGiB(100)).ShouldBe(ByteSize.FromGiB(10));
    }

    [Fact]
    public void MarginIsTenPercentWhenItIsLargerThanTheMinimum()
    {
        CapacityPolicy.Default.Margin(ByteSize.FromGiB(200)).ShouldBe(ByteSize.FromGiB(20));
    }

    [Fact]
    public void MarginOfNothingUsedIsTheMinimum()
    {
        CapacityPolicy.Default.Margin(new ByteSize(0)).ShouldBe(ByteSize.FromGiB(10));
    }

    [Fact]
    public void TenPercentIsRoundedUpToTheNextByte()
    {
        var policy = new CapacityPolicy(0.10m, new ByteSize(0));

        policy.Margin(new ByteSize(15)).ShouldBe(new ByteSize(2));
    }

    [Fact]
    public void RequiredIsUsedPlusMargin()
    {
        CapacityPolicy.Default.Required(ByteSize.FromGiB(200)).ShouldBe(ByteSize.FromGiB(220));
        CapacityPolicy.Default.Required(ByteSize.FromGiB(40)).ShouldBe(ByteSize.FromGiB(50));
    }

    [Fact]
    public void PercentageAndMinimumAreConfigurable()
    {
        var policy = new CapacityPolicy(0.50m, ByteSize.FromGiB(1));

        policy.Margin(ByteSize.FromGiB(10)).ShouldBe(ByteSize.FromGiB(5));
        policy.Margin(ByteSize.FromGiB(1)).ShouldBe(ByteSize.FromGiB(1));
        policy.Required(ByteSize.FromGiB(10)).ShouldBe(ByteSize.FromGiB(15));
    }

    [Fact]
    public void NegativePercentageIsRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new CapacityPolicy(-0.01m, ByteSize.FromGiB(10)));
    }
}
