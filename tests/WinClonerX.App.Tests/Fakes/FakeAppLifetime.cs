using WinClonerX.App.Services;

namespace WinClonerX.App.Tests.Fakes;

internal sealed class FakeAppLifetime : IAppLifetime
{
    public int ShutdownCalls { get; private set; }

    public void Shutdown() => ShutdownCalls++;
}
