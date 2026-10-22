using System.Windows;

namespace WinCopy.App.Services;

/// <summary>WPF implementation of application shutdown.</summary>
public sealed class WpfAppLifetime : IAppLifetime
{
    public void Shutdown() => System.Windows.Application.Current.Shutdown();
}
