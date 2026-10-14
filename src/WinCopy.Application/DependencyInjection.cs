using Microsoft.Extensions.DependencyInjection;
using WinCopy.Application.UseCases.Execution;
using WinCopy.Application.UseCases.Planning;

namespace WinCopy.Application;

/// <summary>Registro de los casos de uso y del estado de planificación de la capa de aplicación.</summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra los casos de uso y <see cref="IClonePlanStore"/> como singletons. Los puertos de salida no se registran
    /// aquí: los registra la capa de infraestructura (<c>AddWinCopyInfrastructure</c>).
    /// </summary>
    public static IServiceCollection AddWinCopyApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IClonePlanStore, ClonePlanStore>();
        services.AddSingleton<IGetInventoryAndEligibility, GetInventoryAndEligibility>();
        services.AddSingleton<IBuildClonePlan, BuildClonePlan>();
        services.AddSingleton<IConfirmClonePlan, ConfirmClonePlan>();
        services.AddSingleton<IExecuteClone, ExecuteClone>();

        return services;
    }
}
