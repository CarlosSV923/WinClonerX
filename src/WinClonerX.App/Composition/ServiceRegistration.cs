using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WinClonerX.App.Services;
using WinClonerX.App.ViewModels;
using WinClonerX.Application;
using WinClonerX.Infrastructure.Windows;

namespace WinClonerX.App.Composition;

/// <summary>Registra en el contenedor los casos de uso, los adaptadores reales y las ventanas y vistas de la aplicación.</summary>
internal static class ServiceRegistration
{
    /// <summary>Añade todos los servicios de la aplicación a la colección indicada.</summary>
    public static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        string appVersion = typeof(ServiceRegistration).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        services.AddWinClonerXApplication();
        services.AddWinClonerXInfrastructure(options => options.AppVersion = appVersion);

        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.AddSingleton<IConfirmationService, WpfConfirmationService>();
        services.AddSingleton<IInfoDialogService, WpfInfoDialogService>();
        services.AddSingleton<IFileDialogService, WpfFileDialogService>();
        services.AddSingleton<IFolderOpener, ExplorerFolderOpener>();
        services.AddSingleton<IAppLifetime, WpfAppLifetime>();

        services.AddSingleton<DisksStepViewModel>();
        services.AddSingleton<ReviewStepViewModel>();
        services.AddSingleton<ConfirmStepViewModel>();
        services.AddSingleton<ProgressStepViewModel>();
        services.AddSingleton<ResultStepViewModel>();
        services.AddSingleton<WizardViewModel>();
        services.AddSingleton<MainWindow>();
    }
}
