using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using System.IO;
using WinCopy.Application;
using WinCopy.Application.Ports;
using WinCopy.Application.UseCases;
using WinCopy.Application.UseCases.Execution;
using WinCopy.Application.UseCases.Planning;
using WinCopy.Application.UseCases.Recovery;
using WinCopy.Infrastructure.Windows;
using Xunit;

namespace WinCopy.App.Tests.Composition;

/// <summary>
/// Comprueba la composición real: que todos los puertos y casos de uso se resuelven con el contenedor validado, y que
/// resolver no toca nada fuera de la carpeta temporal de registros.
/// </summary>
public sealed class CompositionTests : IDisposable
{
    /// <summary>Interfaces del espacio de nombres de puertos que no son servicios del contenedor, por diseño.</summary>
    private static readonly Type[] NotContainerServices =
    [
        // Los produce un adaptador (ISnapshotSet.OpenVolume, IRawPartitionReader.Open) y nunca se resuelven directamente.
        typeof(IBlockSource),
        // Los produce ISnapshotService.CreateAsync.
        typeof(ISnapshotSet),
        // Lo produce ITargetDiskFactory.Open con un plan confirmado.
        typeof(ITargetDisk),
    ];

    /// <summary>Puertos sin adaptador real todavía. Se quitan de esta lista cuando exista su implementación.</summary>
    private static readonly Type[] NotImplementedYet =
    [
        // Sin adaptador: FR-011, US3 escenario 2.
        typeof(IDeviceChangeMonitor),
    ];

    private readonly string _logDirectory = Path.Combine(
        Path.GetTempPath(),
        "WinCopy.CompositionTests." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // Cada prueba dispone su proveedor antes de salir, así que el archivo de registro ya está cerrado.
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    [Fact]
    public void EveryPortInterfaceResolvesExceptTheKnownExclusions()
    {
        var ports = PortInterfaces().ToList();
        ports.Count.ShouldBeGreaterThan(0, "No port interfaces found in WinCopy.Application.Ports.");

        using var provider = BuildProvider();

        var unresolved = new List<string>();
        foreach (var port in ports.Except(NotContainerServices).Except(NotImplementedYet))
        {
            if (provider.GetService(port) is null)
            {
                unresolved.Add(port.Name);
            }
        }

        unresolved.ShouldBeEmpty("Port interfaces that do not resolve: " + string.Join(", ", unresolved));
    }

    [Fact]
    public void ExclusionListsOnlyNameRealPortInterfaces()
    {
        // Evita que la lista de excepciones quede obsoleta si un puerto se renombra o se elimina.
        var ports = PortInterfaces().ToHashSet();

        foreach (var excluded in NotContainerServices.Concat(NotImplementedYet))
        {
            ports.ShouldContain(excluded);
        }
    }

    [Fact]
    public void EveryUseCaseInterfaceResolves()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IGetInventoryAndEligibility>().ShouldNotBeNull();
        provider.GetRequiredService<IBuildClonePlan>().ShouldNotBeNull();
        provider.GetRequiredService<IConfirmClonePlan>().ShouldNotBeNull();
        provider.GetRequiredService<IExecuteClone>().ShouldNotBeNull();
        provider.GetRequiredService<IClonePlanStore>().ShouldNotBeNull();
        provider.GetRequiredService<IRecoverPendingSourceRestore>().ShouldNotBeNull();
        provider.GetRequiredService<IExportLog>().ShouldNotBeNull();
    }

    [Fact]
    public void SharedServicesAreSingletons()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IExecutionLog>().ShouldBeSameAs(provider.GetRequiredService<IExecutionLog>());
        provider.GetRequiredService<IExecuteClone>().ShouldBeSameAs(provider.GetRequiredService<IExecuteClone>());
        provider.GetRequiredService<IClonePlanStore>().ShouldBeSameAs(provider.GetRequiredService<IClonePlanStore>());
    }

    [Fact]
    public void ResolutionDoesNotThrowAndCreatesFilesOnlyInTheLogDirectory()
    {
        using var provider = BuildProvider();

        var ports = PortInterfaces().Except(NotContainerServices).Except(NotImplementedYet).ToList();
        foreach (var port in ports)
        {
            Should.NotThrow(() => provider.GetRequiredService(port), port.Name);
        }

        var log = provider.GetRequiredService<IExecutionLog>();
        log.CurrentFile.ShouldStartWith(_logDirectory);

        // Solo el registro de la ejecución puede existir en la carpeta. Nada más se ha creado.
        var files = Directory.GetFiles(_logDirectory);
        files.ShouldNotBeEmpty();
        files.ShouldAllBe(file => file.EndsWith(".clef", StringComparison.Ordinal));
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddWinCopyApplication();
        services.AddWinCopyInfrastructure(options =>
        {
            options.LogDirectory = _logDirectory;
            options.JournalDirectory = _logDirectory;
        });

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static IEnumerable<Type> PortInterfaces() =>
        typeof(IClock).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == typeof(IClock).Namespace);
}
