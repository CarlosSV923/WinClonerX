using Shouldly;
using Xunit;

namespace WinCopy.App.Tests;

public sealed class SingleInstanceTests
{
    private static string UniqueMutexName() => $@"Local\WinCopy.Test.{Guid.NewGuid():N}";

    [Fact]
    public void FirstInstanceAcquiresTheMutex()
    {
        using var first = new SingleInstance(UniqueMutexName());

        first.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public void SecondInstanceWithSameNameDoesNotAcquire()
    {
        var name = UniqueMutexName();
        using var first = new SingleInstance(name);
        using var second = new SingleInstance(name);

        first.TryAcquire().ShouldBeTrue();
        second.TryAcquire().ShouldBeFalse();
    }

    [Fact]
    public void MutexCanBeAcquiredAgainAfterDispose()
    {
        var name = UniqueMutexName();
        var first = new SingleInstance(name);
        first.TryAcquire().ShouldBeTrue();

        first.Dispose();

        using var next = new SingleInstance(name);
        next.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public void TryAcquireIsIdempotentForTheOwner()
    {
        using var instance = new SingleInstance(UniqueMutexName());

        instance.TryAcquire().ShouldBeTrue();
        instance.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public void TryAcquireThrowsAfterDispose()
    {
        var instance = new SingleInstance(UniqueMutexName());
        instance.Dispose();

        Should.Throw<ObjectDisposedException>(() => instance.TryAcquire());
    }

    [Fact]
    public void DisposingWithoutAcquiringIsSafe()
    {
        var losing = new SingleInstance(UniqueMutexName());

        Should.NotThrow(() => losing.Dispose());
        Should.NotThrow(() => losing.Dispose());
    }
}
