using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinCopy.App.ViewModels;

namespace WinCopy.App;

/// <summary>
/// Composition root. Builds the Generic Host, registers the windows and view models, and enforces a single instance.
/// </summary>
public sealed partial class App : Application, IDisposable
{
    private SingleInstance? _singleInstance;
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstance();
        if (!_singleInstance.TryAcquire())
        {
            SingleInstance.BringExistingInstanceToFront();
            Shutdown();
            return;
        }

        var builder = Host.CreateApplicationBuilder(e.Args);
        ConfigureServices(builder.Services);

        _host = builder.Build();
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    /// <summary>Stops the host and releases the single-instance mutex. Safe to call more than once.</summary>
    public void Dispose()
    {
        _host?.Dispose();
        _host = null;

        _singleInstance?.Dispose();
        _singleInstance = null;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<WizardViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
