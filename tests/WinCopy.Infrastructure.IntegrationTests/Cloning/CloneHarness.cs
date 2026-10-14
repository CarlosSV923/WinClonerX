using Microsoft.Extensions.DependencyInjection;
using WinCopy.Application;
using WinCopy.Application.Ports;
using WinCopy.Application.UseCases.Execution;
using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.IntegrationTests.Fixtures;
using WinCopy.Infrastructure.Windows;
using WinCopy.Infrastructure.Windows.Logging;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.IntegrationTests.Cloning;

/// <summary>
/// Composes the application and the real infrastructure with <see cref="AddWinCopyApplication"/> and
/// <see cref="AddWinCopyInfrastructure"/>, then overrides the parts listed below. Everything else is the real implementation:
/// VSS, the snapshot source, the raw reader, the target disk, the copy pipeline, the GPT commit, the file system expander,
/// the temporary mounter, the structural verifier, power management and logging.
/// </summary>
/// <remarks>
/// <para>Overrides, and why:</para>
/// <list type="bullet">
/// <item><see cref="IDiskInventoryReader"/> is <see cref="FixtureOnlyInventoryReader"/> over the real reader. The real system disk must
/// never reach the planner, and the reference source disk must be seen as the system disk.</item>
/// <item><see cref="IOfflineRegistryEditor"/>, <see cref="IBootConfigurator"/> and <see cref="IRecoveryConfigurator"/> are recording fakes.
/// The reference disk is not a real Windows installation, so there is no SYSTEM hive to edit, nothing for bcdboot to build, and
/// no WinRE image to point. The real boot repair is validated in the VM checkpoint S2.</item>
/// <item><see cref="ITargetDiskFactory"/> is replaced only when a test passes <c>tamperCommittedLayout</c>. The factory is then the
/// real <see cref="TargetDiskFactory"/> wrapped by <see cref="TamperingTargetDiskFactory"/>, which alters the layout before the real
/// adapter commits it.</item>
/// </list>
/// <para>
/// The composition uses the same log directory for every run, and <see cref="DisposeAsync"/> disposes the container, which
/// closes the execution log file.
/// </para>
/// </remarks>
internal sealed class CloneHarness : IAsyncDisposable
{
    private readonly object _warningsGate = new();
    private readonly List<string> _warnings = [];
    private readonly ServiceProvider _provider;

    public CloneHarness(
        ReferenceDisk source,
        string logDirectory,
        Func<TargetLayout, TargetLayout>? tamperCommittedLayout = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        LogDirectory = logDirectory;
        RealReader = CreateRealReader(logDirectory, Warn);

        var services = new ServiceCollection();
        services.AddWinCopyApplication();
        services.AddWinCopyInfrastructure(options =>
        {
            options.LogDirectory = logDirectory;
            options.Warnings = Warn;
        });

        services.AddSingleton<IDiskInventoryReader>(_ => new FixtureOnlyInventoryReader(
            RealReader,
            source.Disk.DiskNumber,
            source.Windows.GptGuid));

        services.AddSingleton<IOfflineRegistryEditor>(Registry);
        services.AddSingleton<IBootConfigurator>(Boot);
        services.AddSingleton<IRecoveryConfigurator>(Recovery);

        if (tamperCommittedLayout is not null)
        {
            services.AddSingleton<ITargetDiskFactory>(sp => new TamperingTargetDiskFactory(
                new TargetDiskFactory(DiskWriteGuard.Default, sp.GetRequiredService<DiskOperationRecorder>(), Warn),
                tamperCommittedLayout));
        }

        _provider = services.BuildServiceProvider();
    }

    /// <summary>The composed services. Resolve the use cases and the real structural verifier from here.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>The real <see cref="IDiskInventoryReader"/>, without the fixture decorator.</summary>
    public IDiskInventoryReader RealReader { get; }

    /// <summary>Folder of the execution log of this harness.</summary>
    public string LogDirectory { get; }

    /// <summary>Records the offline registry calls of the boot repair.</summary>
    public RecordingOfflineRegistryEditor Registry { get; } = new();

    /// <summary>Records the bcdboot calls of the boot repair.</summary>
    public RecordingBootConfigurator Boot { get; } = new();

    /// <summary>Records the WinRE calls of the boot repair.</summary>
    public RecordingRecoveryConfigurator Recovery { get; } = new();

    /// <summary>Non-fatal warnings from the adapters, in order.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (_warningsGate)
            {
                return _warnings.ToArray();
            }
        }
    }

    /// <summary>
    /// Runs the planning and execution use cases on <paramref name="target"/>: reads the inventory, builds the plan for the
    /// target's disk identity, accepts every warning, confirms with <see cref="TargetConfirmation.ExpectedSuffix"/> and executes
    /// the plan. Returns the confirmed plan and the final report, which is returned also when the run fails.
    /// </summary>
    public async Task<(ClonePlan Plan, CloneResultReport Report)> CloneAsync(
        ReferenceDisk source,
        AttachedVirtualDisk target,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        InventoryOverview overview = await Services.GetRequiredService<IGetInventoryAndEligibility>().ExecuteAsync(ct);

        if (overview.Inventory.SystemDisk?.Id.Number != source.Disk.DiskNumber)
        {
            throw new InvalidOperationException(
                $"The test inventory's system disk is not the reference source disk {source.Disk.DiskNumber}.");
        }

        Disk targetDisk = overview.Inventory.Disks.FirstOrDefault(disk => disk.Id.Number == target.DiskNumber)
            ?? throw new InvalidOperationException($"The target disk {target.DiskNumber} is not in the test inventory.");

        Result<ClonePlan, PreflightReport> built = await Services.GetRequiredService<IBuildClonePlan>()
            .ExecuteAsync(targetDisk.Id, VerificationMode.Structural, ct);
        if (!built.IsSuccess)
        {
            string codes = string.Join(", ", built.Error.Checks.Select(check => check.Code));
            throw new InvalidOperationException($"The planner refused target disk {target.DiskNumber}: {codes}.");
        }

        ClonePlan draft = built.Value;
        PreflightCode[] acceptedWarnings = draft.Warnings.Select(warning => warning.Code).Distinct().ToArray();

        ClonePlan confirmed = await Services.GetRequiredService<IConfirmClonePlan>().ExecuteAsync(
            draft.Id,
            acceptedWarnings,
            TargetConfirmation.ExpectedSuffix(targetDisk),
            ct);

        CloneResultReport report = await Services.GetRequiredService<IExecuteClone>().ExecuteAsync(confirmed.Id, ct);
        return (confirmed, report);
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private static IDiskInventoryReader CreateRealReader(string logDirectory, Action<string> warnings)
    {
        // Only the reader is resolved from this container. It is stateless and does not create the execution log.
        using ServiceProvider realProvider = new ServiceCollection()
            .AddWinCopyInfrastructure(options =>
            {
                options.LogDirectory = logDirectory;
                options.Warnings = warnings;
            })
            .BuildServiceProvider();

        return realProvider.GetRequiredService<IDiskInventoryReader>();
    }

    private void Warn(string message)
    {
        lock (_warningsGate)
        {
            _warnings.Add(message);
        }

        Console.WriteLine($"[CloneHarness] warning: {message}");
    }
}
