using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WinCopy.Application.Ports;
using WinCopy.Infrastructure.Windows.Boot;
using WinCopy.Infrastructure.Windows.Journal;
using WinCopy.Infrastructure.Windows.Logging;
using WinCopy.Infrastructure.Windows.Platform;
using WinCopy.Infrastructure.Windows.RawIo;
using WinCopy.Infrastructure.Windows.Snapshots;
using WinCopy.Infrastructure.Windows.Storage;
using WinCopy.Infrastructure.Windows.Verification;

namespace WinCopy.Infrastructure.Windows;

/// <summary>Opciones de la composición de infraestructura.</summary>
public sealed class WinCopyInfrastructureOptions
{
    /// <summary>
    /// Carpeta de los registros de ejecución. Si es null se usa <c>%ProgramData%\WinCopy\logs</c>. El registro se crea
    /// solo cuando se resuelve <see cref="IExecutionLog"/> por primera vez, no al construir el contenedor.
    /// </summary>
    public string? LogDirectory { get; set; }

    /// <summary>
    /// Folder of the source restore journal (<c>pending-source-restore.json</c>). When null, the journal goes to
    /// <c>%ProgramData%\WinCopy</c>, the same base folder as the logs.
    /// </summary>
    public string? JournalDirectory { get; set; }

    /// <summary>Versión de la aplicación, que se escribe en cada línea del registro.</summary>
    public string AppVersion { get; set; } =
        typeof(WinCopyInfrastructureOptions).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    /// <summary>Recibe los avisos no fatales de los adaptadores (por ejemplo, un volumen omitido). Puede ser null.</summary>
    public Action<string>? Warnings { get; set; }
}

/// <summary>
/// Registra los adaptadores reales de <see cref="WinCopy.Application.Ports"/>. Todas las fábricas son perezosas: construir
/// el contenedor y resolver los servicios no toca discos, dispositivos, WMI ni el registro. Solo se hace trabajo del sistema
/// cuando se llama a un método del adaptador.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registra todos los puertos de salida con su adaptador real, más los componentes internos compartidos.
    /// No registra <see cref="IDeviceChangeMonitor"/>: todavía no tiene adaptador.
    /// </summary>
    public static IServiceCollection AddWinCopyInfrastructure(
        this IServiceCollection services,
        Action<WinCopyInfrastructureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new WinCopyInfrastructureOptions();
        configure?.Invoke(options);

        if (options.LogDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.LogDirectory);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.AppVersion);

        if (options.JournalDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.JournalDirectory);
        }

        string? logDirectory = options.LogDirectory;
        string? journalDirectory = options.JournalDirectory;
        string appVersion = options.AppVersion;
        Action<string>? warnings = options.Warnings;

        // Componentes internos compartidos.
        services.AddSingleton<IClock>(static _ => new SystemClock());
        services.AddSingleton<IExecutionLog>(sp =>
        {
            var clock = sp.GetRequiredService<IClock>();
            Guid executionId = Guid.NewGuid();

            return logDirectory is null
                ? SerilogExecutionLog.CreateDefault(executionId, appVersion, clock)
                : new SerilogExecutionLog(logDirectory, executionId, appVersion, clock);
        });
        services.AddSingleton<DiskOperationRecorder>();
        services.AddSingleton<IVolumePathResolver>(static _ => new StorageVolumePathResolver());
        services.AddSingleton<IVolumeExtentsReader>(static _ => new WindowsVolumeExtentsReader());
        services.AddSingleton<ExternalToolRunner>(sp =>
            ExternalToolRunner.ForThisMachine(sp.GetRequiredService<IExecutionLog>()));

        // Puertos de salida.
        services.AddSingleton<IDiskInventoryReader>(sp => new StorageInventoryReader(
            new WmiStorageInventory(),
            new Win32DriveLayoutReader(),
            new Win32FirmwareInfo(),
            new BitLockerQuery(new WmiBitLockerQuery(), warnings),
            new Win32WindowsVolumeInfo(),
            sp.GetRequiredService<IVolumeExtentsReader>(),
            sp.GetRequiredService<IClock>(),
            warnings));

        services.AddSingleton<IPowerManager>(static _ => new PowerManager());

        // The journal is a plain file, so it is created right away; it writes only when a shrink is about to start.
        services.AddSingleton<ISourceRestoreJournal>(_ => journalDirectory is null
            ? FileSourceRestoreJournal.CreateDefault()
            : new FileSourceRestoreJournal(journalDirectory));

        services.AddSingleton<ISourceResizer>(sp => new StorageSourceResizer(
            new WmiPartitionResizeApi(),
            sp.GetRequiredService<IVolumeExtentsReader>(),
            sp.GetRequiredService<DiskOperationRecorder>(),
            sp.GetRequiredService<IClock>()));

        services.AddSingleton<IPlatformFactsProvider>(sp => new PlatformFactsProvider(
            sp.GetRequiredService<IPowerManager>(),
            new WmiServiceCatalog(),
            sp.GetRequiredService<IVolumePathResolver>(),
            new Win32VolumeLockProbe(),
            sp.GetRequiredService<ISourceResizer>(),
            warnings));

        services.AddSingleton<ISnapshotService>(sp => new VssSnapshotService(
            sp.GetRequiredService<IVolumePathResolver>(),
            new ComVssBackupFactory(),
            warnings));

        services.AddSingleton<IRawPartitionReader>(static _ => new RawPartitionReader());

        services.AddSingleton<ITargetDiskFactory>(sp => new TargetDiskFactory(
            DiskWriteGuard.Default,
            sp.GetRequiredService<DiskOperationRecorder>(),
            warnings));

        // El copiador es un singleton: la etiqueta de su registro solo se usa si el origen o el destino no tienen la suya propia
        // (IDiskOperationLabel). El origen (instantánea o tramo en bruto) y el destino real siempre la tienen.
        services.AddSingleton<IVolumeCopier>(sp => new CopyPipeline(
            CopyPipeline.CreateDefaultPool,
            sp.GetRequiredService<DiskOperationRecorder>(),
            "target"));

        services.AddSingleton<IFileSystemExpander>(sp => new FileSystemExpander(
            sp.GetRequiredService<IVolumePathResolver>(),
            new Win32VolumeAccess(),
            sp.GetRequiredService<DiskOperationRecorder>()));

        services.AddSingleton<ITemporaryMounter>(sp => new TemporaryMounter(
            sp.GetRequiredService<IVolumePathResolver>(),
            new EnvironmentLogicalDriveSource(),
            new Win32MountPointApi(),
            warnings));

        services.AddSingleton<IOfflineRegistryEditor>(static _ => new OfflineRegistryEditor());

        services.AddSingleton<IBootConfigurator>(sp =>
        {
            var environment = new Win32BootTargetEnvironment();
            var safety = new BootTargetSafety(environment, sp.GetRequiredService<IVolumeExtentsReader>());

            return new BcdBootConfigurator(sp.GetRequiredService<ExternalToolRunner>(), safety, environment);
        });

        services.AddSingleton<IRecoveryConfigurator>(sp =>
        {
            var environment = new Win32BootTargetEnvironment();
            var safety = new BootTargetSafety(environment, sp.GetRequiredService<IVolumeExtentsReader>());

            return new ReagentcRecoveryConfigurator(sp.GetRequiredService<ExternalToolRunner>(), safety, environment);
        });

        services.AddSingleton<IStructuralVerifier>(static _ => new StructuralVerifier());

        return services;
    }
}
