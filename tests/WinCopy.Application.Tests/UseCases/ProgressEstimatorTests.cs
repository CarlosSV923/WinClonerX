using Shouldly;
using WinCopy.Application.Tests.Fakes;
using WinCopy.Application.UseCases.Execution;
using Xunit;
using static WinCopy.Application.Tests.TestData.PlanningTestData;

namespace WinCopy.Application.Tests.UseCases;

public sealed class ProgressEstimatorTests
{
    private const long Total = 100 * Mib;

    [Fact]
    public void EtaIsNullWhileLessThanOnePercentIsDoneAndTenSecondsHaveNotElapsed()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(Total, clock, 10 * Mib);

        estimator.Update(0).Eta.ShouldBeNull();

        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        estimator.Update(Mib / 2).Eta.ShouldBeNull();
    }

    [Fact]
    public void EtaAppearsOnceOnePercentIsDoneEvenBeforeTenSeconds()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(Total, clock, 10 * Mib);

        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        var progress = estimator.Update(Mib);

        progress.Eta.ShouldNotBeNull();
    }

    [Fact]
    public void EtaAppearsAfterTenSecondsEvenBelowOnePercent()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(Total, clock, 10 * Mib);

        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        var progress = estimator.Update(Mib / 2);

        progress.Eta.ShouldNotBeNull();
    }

    [Fact]
    public void InitialThroughputIsThePlanEstimateUntilTimeHasPassed()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(Total, clock, 10 * Mib);

        // Sin tiempo transcurrido no hay muestra: el rendimiento sigue siendo el de la estimación del plan.
        var progress = estimator.Update(2 * Mib);

        progress.ThroughputBytesPerSecond.ShouldBe(10.0 * Mib);
        var eta = progress.Eta!.Value.TotalSeconds;
        Math.Abs(eta - 9.8).ShouldBeLessThan(1e-6);
    }

    [Fact]
    public void ThroughputIsAnExponentialMovingAverageWithAThirtySecondWindow()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(1000 * Mib, clock, 10.0 * Mib);

        // 30 s a 30 MiB/s. alpha = 1 - exp(-30 / 30) y la media mezcla la muestra con la estimación inicial.
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        var progress = estimator.Update(900 * Mib);

        var alpha = 1 - Math.Exp(-1.0);
        var expected = (alpha * 30.0 * Mib) + ((1 - alpha) * 10.0 * Mib);
        Math.Abs(progress.ThroughputBytesPerSecond - expected).ShouldBeLessThan(1.0);
    }

    [Fact]
    public void UpdatesWithoutElapsedTimeDoNotChangeThroughputOrFail()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(Total, clock, 10 * Mib);

        estimator.Update(Mib);
        var progress = estimator.Update(2 * Mib);

        progress.ThroughputBytesPerSecond.ShouldBe(10.0 * Mib);
        progress.BytesDone.ShouldBe(2 * Mib);
    }

    [Fact]
    public void EtaIsNullWhenThereIsNoThroughputToExtrapolate()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(Total, clock, 0);

        // 1 % hecho pero sin tiempo transcurrido: no hay muestra de rendimiento que extrapolar.
        var progress = estimator.Update(Mib);

        progress.Eta.ShouldBeNull();
    }

    [Fact]
    public void EtaIsZeroWhenEverythingIsDone()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(Total, clock, 10 * Mib);

        var progress = estimator.Update(Total);

        progress.Percent.ShouldBe(100.0);
        progress.Eta.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ZeroTotalCountsAsCompleted()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(0, clock, 0);

        var progress = estimator.Update(0);

        progress.Percent.ShouldBe(100.0);
        progress.Eta.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void BytesBeyondTheTotalAreClampedToIt()
    {
        var clock = new FakeClock();
        var estimator = new ProgressEstimator(Total, clock, 10 * Mib);

        var progress = estimator.Update(Total + 5);

        progress.BytesDone.ShouldBe(Total);
    }

    [Fact]
    public void NegativeTotalOrInitialThroughputIsRejected()
    {
        var clock = new FakeClock();

        Should.Throw<ArgumentOutOfRangeException>(() => new ProgressEstimator(-1, clock, 1));
        Should.Throw<ArgumentOutOfRangeException>(() => new ProgressEstimator(Total, clock, -1));
    }
}
