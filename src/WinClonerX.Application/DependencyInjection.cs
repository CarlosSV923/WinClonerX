using Microsoft.Extensions.DependencyInjection;
using WinClonerX.Application.Ports;
using WinClonerX.Application.UseCases;
using WinClonerX.Application.UseCases.Execution;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Application.UseCases.Recovery;

namespace WinClonerX.Application;

/// <summary>Registro de los casos de uso y del estado de planificación de la capa de aplicación.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra los casos de uso y <see cref="IClonePlanStore"/> como singletons. Los puertos de salida no se registran
    /// aquí: los registra la capa de infraestructura (<c>AddWinClonerXInfrastructure</c>).
    /// </summary>
    public static IServiceCollection AddWinClonerXApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClonePlanStore, ClonePlanStore>();
        services.AddSingleton<IGetInventoryAndEligibility, GetInventoryAndEligibility>();
        services.AddSingleton<IBuildClonePlan, BuildClonePlan>();
        services.AddSingleton<IConfirmClonePlan, ConfirmClonePlan>();
        services.AddSingleton<IExecuteClone, ExecuteClone>();

        // El registro se pide con una función: solo se crea cuando la recuperación encuentra algo que registrar.
        services.AddSingleton<IRecoverPendingSourceRestore>(sp => new RecoverPendingSourceRestore(
            sp.GetRequiredService<IDiskInventoryReader>(),
            sp.GetRequiredService<ISourceResizer>(),
            sp.GetRequiredService<ISourceRestoreJournal>(),
            () => sp.GetRequiredService<IExecutionLog>()));

        // La exportación también pide el registro con una función: solo se resuelve cuando la persona exporta.
        services.AddSingleton<IExportLog>(sp => new ExportLog(() => sp.GetRequiredService<IExecutionLog>()));

        return services;
    }
}
