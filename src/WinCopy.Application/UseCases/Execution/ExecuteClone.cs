using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.BootRepair;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.UseCases.Execution;

/// <summary>
/// Orquestador del clonado (US1). Aplica un plan confirmado en las fases de <see cref="ExecutionPhase"/>, en el orden del
/// dominio, y publica cada hecho como <see cref="ExecutionEvent"/> y en el registro. El disco de origen solo se lee: todas
/// las escrituras van al destino. Ante un fallo o una cancelación ejecuta las compensaciones que indica
/// <see cref="CloneExecution.RequiredCompensations"/>, siempre en ese orden, y devuelve un informe; no relanza excepciones,
/// salvo las de precondición del plan.
/// </summary>
public sealed class ExecuteClone(
    IClonePlanStore planStore,
    IDiskInventoryReader inventoryReader,
    ISnapshotService snapshots,
    IRawPartitionReader rawReader,
    ITargetDiskFactory targetFactory,
    IVolumeCopier copier,
    IFileSystemExpander expander,
    ITemporaryMounter mounter,
    IOfflineRegistryEditor registry,
    IBootConfigurator bootConfigurator,
    IRecoveryConfigurator recoveryConfigurator,
    IStructuralVerifier verifier,
    IPowerManager power,
    IExecutionLog log,
    IClock clock) : IExecuteClone
{
    private const string DisksChangedReason = "DisksChanged";
    private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ProgressLogInterval = TimeSpan.FromSeconds(5);

    private readonly ExecutionEventSubject _subject = new();

    /// <inheritdoc />
    public IObservable<ExecutionEvent> Events => _subject;

    /// <inheritdoc />
    public async Task<CloneResultReport> ExecuteAsync(PlanId planId, CancellationToken ct)
    {
        if (!planStore.TryGet(planId, out var plan))
        {
            throw new InvalidOperationException("No existe un plan de clonado con ese identificador.");
        }

        if (plan.Status != PlanStatus.Confirmed)
        {
            throw new InvalidOperationException("Solo se puede ejecutar un plan confirmado.");
        }

        if (plan.Verification == VerificationMode.StructuralAndFull)
        {
            // TODO(polish): la verificación completa (comparación byte a byte) queda para después de la US1.
            throw new NotSupportedException("La verificación completa todavía no está implementada.");
        }

        var sleep = new SleepBlock(power.PreventSleep());
        try
        {
            var startedAt = clock.UtcNow;
            var run = new ExecutionRun(plan, CloneExecution.Start(new ExecutionId(Guid.NewGuid()), plan, startedAt), startedAt, sleep);
            Flush(run);

            try
            {
                await RunPhasesAsync(run, ct);
                return FinishCompleted(run);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return await AbortAsync(run, failure: null);
            }
            catch (ExecutionAbortException abort)
            {
                return await AbortAsync(run, (abort.Code, abort.Detail));
            }
            catch (PlatformException ex)
            {
                return await AbortAsync(run, (ex.Code.ToString(), ex.TechnicalDetail));
            }
            catch (Exception ex)
            {
                return await AbortAsync(run, ("Unexpected", ex.ToString()));
            }
        }
        finally
        {
            sleep.Release();
        }
    }

    private async Task RunPhasesAsync(ExecutionRun run, CancellationToken ct)
    {
        await RunPhaseAsync(run, ExecutionPhase.Revalidate, () => RevalidateAsync(run, ct), ct);
        await RunPhaseAsync(run, ExecutionPhase.Snapshot, () => SnapshotAsync(run, ct), ct);
        await RunPhaseAsync(run, ExecutionPhase.PrepareTarget, () => PrepareTargetAsync(run, ct), ct);
        await RunPhaseAsync(run, ExecutionPhase.CopyData, () => CopyDataAsync(run, ct), ct);
        await RunPhaseAsync(run, ExecutionPhase.CommitLayout, () => CommitLayoutAsync(run), ct);

        if (run.Plan.Mappings.Any(mapping => mapping.Resize == ResizeKind.GrowFileSystem))
        {
            await RunPhaseAsync(run, ExecutionPhase.ExpandFileSystem, () => ExpandFileSystemAsync(run, ct), ct);
        }

        await RunPhaseAsync(run, ExecutionPhase.RepairBoot, () => RepairBootAsync(run, ct), ct);
        await RunPhaseAsync(run, ExecutionPhase.StructuralVerification, () => VerifyStructureAsync(run, ct), ct);
        await RunPhaseAsync(run, ExecutionPhase.Cleanup, () => CleanupAsync(run), ct);
    }

    /// <summary>Ejecuta una fase: comprueba la cancelación antes de empezar, la marca como iniciada y como completada, y publica sus eventos.</summary>
    private async Task RunPhaseAsync(ExecutionRun run, ExecutionPhase phase, Func<Task> body, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        run.Execution.BeginPhase(phase, clock.UtcNow);
        Flush(run);

        await body();

        run.Execution.CompletePhase(phase, clock.UtcNow);
        Flush(run);
    }

    private async Task RevalidateAsync(ExecutionRun run, CancellationToken ct)
    {
        var plan = run.Plan;
        var inventory = await inventoryReader.ReadAsync(ct);
        var fresh = inventory.FindDisk(plan.Target);

        if (fresh is null || inventory.FindDisk(plan.Source) is null || inventory.Fingerprint(plan.Source, plan.Target) != plan.Fingerprint)
        {
            plan.Invalidate(DisksChangedReason);
            planStore.Save(plan);
            log.Record(ExecutionLogEvents.PlanInvalidated(run.Id, plan.Id, DisksChangedReason));
            throw new ExecutionAbortException(DisksChangedReason, "El inventario de los discos cambió después de planificar; hay que volver a planificar.");
        }

        run.FreshTarget = fresh;
    }

    private async Task SnapshotAsync(ExecutionRun run, CancellationToken ct)
    {
        var volumes = run.Plan.Mappings
            .Where(mapping => mapping.CopyMethod == CopyMethod.UsedClustersFromSnapshot)
            .Select(mapping => mapping.Source.UniqueId)
            .ToArray();

        run.Snapshot = await snapshots.CreateAsync(volumes, ct);
    }

    private async Task PrepareTargetAsync(ExecutionRun run, CancellationToken ct)
    {
        // Se asigna antes de bloquear: si el bloqueo falla, la compensación de cierre sigue teniendo el disco.
        var target = targetFactory.Open(run.Plan);
        run.Target = target;

        await target.LockAndTakeOfflineAsync(ct);
        await target.WipeBoundariesAsync(ct);
    }

    /// <summary>
    /// Copia cada correspondencia en su desplazamiento del destino.
    /// <para>
    /// Semántica del progreso (contrato de <see cref="IVolumeCopier"/>): cada llamada a <c>CopyAsync</c> informa bytes acumulados
    /// solo de su propia copia; este método suma los bytes de las correspondencias ya copiadas para obtener el avance total.
    /// </para>
    /// <para>
    /// Los avances se publican como mucho cada 500 ms. Al terminar la última correspondencia se publica siempre un avance final
    /// con BytesDone == BytesTotal, aunque no hayan pasado 500 ms desde el anterior.
    /// </para>
    /// </summary>
    private async Task CopyDataAsync(ExecutionRun run, CancellationToken ct)
    {
        var target = RequireTarget(run);
        var opened = new List<IBlockSource>();
        var items = new List<CopyItem>();

        try
        {
            foreach (var mapping in run.Plan.Mappings)
            {
                var source = OpenSource(run, mapping);
                opened.Add(source);
                var allocation = await source.GetAllocationAsync(ct);
                items.Add(new CopyItem(mapping, source, allocation));
            }

            var total = items.Sum(item => item.Allocation.TotalUsed.Bytes);
            var estimator = new ProgressEstimator(total, clock, InitialBytesPerSecond(run.Plan));

            long completed = 0;
            foreach (var item in items)
            {
                var before = completed;
                var progress = new CallbackProgress(bytes => ReportCopyProgress(run, estimator, before + bytes));

                await copier.CopyAsync(item.Source, item.Allocation, target, item.Mapping.TargetExtent.Offset, progress, ct);

                completed += item.Allocation.TotalUsed.Bytes;
                item.Source.Dispose();
                opened.Remove(item.Source);
            }

            // Avance final: se publica sin esperar a los 500 ms para que el último valor sea exactamente el total.
            lock (run.Gate)
            {
                var now = clock.UtcNow;
                run.LastProgressReport = now;
                run.Execution.ReportProgress(estimator.Update(total), now);
                Flush(run);
            }
        }
        finally
        {
            foreach (var source in opened)
            {
                source.Dispose();
            }
        }
    }

    /// <summary>
    /// Escribe el diseño y lo pone en línea. La fase no se interrumpe: la cancelación pendiente se atiende justo después
    /// (<see cref="CloneExecution"/> la difiere durante CommitLayout).
    /// </summary>
    private static async Task CommitLayoutAsync(ExecutionRun run)
    {
        var target = RequireTarget(run);
        var freshTarget = run.FreshTarget ?? throw new InvalidOperationException("No hay copia del disco de destino para el diseño.");

        await target.CommitLayoutAsync(run.Plan.ToTargetLayout(freshTarget), CancellationToken.None);
        await target.BringOnlineAsync(CancellationToken.None);
    }

    private async Task ExpandFileSystemAsync(ExecutionRun run, CancellationToken ct)
    {
        foreach (var mapping in run.Plan.Mappings.Where(mapping => mapping.Resize == ResizeKind.GrowFileSystem))
        {
            await expander.ExpandToPartitionAsync(mapping.NewUniqueId, ct);
        }
    }

    private async Task RepairBootAsync(ExecutionRun run, CancellationToken ct)
    {
        var spec = BootRepairSpec.From(run.Plan);

        var windows = await MountAsync(run, spec.WindowsPartition, ct);
        var esp = await MountAsync(run, spec.EspPartition, ct);
        var recovery = spec.RecoveryPartition is { } recoveryPartition
            ? await MountAsync(run, recoveryPartition, ct)
            : null;

        await registry.RewriteMountedDevicesAsync(windows.Root, spec.MountedDevices, ct);
        await bootConfigurator.RebuildAsync(windows.Root, esp.Root, ct);

        if (recovery is null)
        {
            run.Recovery = null;
            return;
        }

        // WinRE no configurado es un aviso, no un fallo: Windows arranca igualmente (R7).
        var result = await recoveryConfigurator.ConfigureAsync(windows.Root, recovery.Root, ct);
        run.Recovery = result.Configured ? RecoveryEnvironmentState.Configured : RecoveryEnvironmentState.Warning;
        run.RecoveryDetail = result.Configured ? null : result.Detail;
    }

    private async Task VerifyStructureAsync(ExecutionRun run, CancellationToken ct)
    {
        var result = await verifier.VerifyAsync(run.Plan, ct);
        run.Structural = result;

        if (!result.Passed)
        {
            var detail = result.Issues.Count == 0 ? "La verificación estructural no ha pasado." : string.Join("; ", result.Issues);
            throw new PlatformException(PlatformErrorCode.VerificationFailed, detail);
        }
    }

    /// <summary>
    /// Cierre normal, después de una verificación estructural correcta. Cada paso se intenta por separado: un fallo se registra
    /// como aviso (<c>CleanupFailed</c>) y el cierre sigue. Así un fallo de limpieza nunca hace fracasar un clon verificado, y
    /// nunca se llega a borrar su diseño. La liberación de la suspensión la hace siempre el bloque final de
    /// <see cref="ExecuteAsync"/>.
    /// </summary>
    private async Task CleanupAsync(ExecutionRun run)
    {
        foreach (var failure in await ReleaseMountsAsync(run))
        {
            LogCleanupFailure(run, "ReleaseMounts", failure);
        }

        if (run.Snapshot is { } snapshot)
        {
            try
            {
                await snapshot.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogCleanupFailure(run, "DisposeSnapshot", ex.ToString());
            }

            run.Snapshot = null;
        }

        if (run.Target is { } target)
        {
            try
            {
                await target.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogCleanupFailure(run, "DisposeTarget", ex.ToString());
            }

            run.Target = null;
        }
    }

    private void LogCleanupFailure(ExecutionRun run, string step, string technicalDetail) =>
        log.Record(ExecutionLogEvents.CleanupFailed(run.Id, step, technicalDetail));

    private CloneResultReport FinishCompleted(ExecutionRun run)
    {
        var nextSteps = new List<NextStepCode>
        {
            NextStepCode.ShutDownAndReplaceDisk,
            NextStepCode.SelectBootDeviceInFirmware,
        };
        if (run.Plan.Warnings.Any(warning => warning.Code == PreflightCode.BitLockerOn))
        {
            nextSteps.Add(NextStepCode.ReenableBitLocker);
        }

        run.Elapsed = clock.UtcNow - run.StartedAt;
        var report = new CloneResultReport(
            CloneOutcome.Completed,
            run.Structural,
            Full: null,
            SourceRestored: null,
            run.Recovery,
            run.Elapsed,
            log.CurrentFile,
            nextSteps);

        run.Execution.Finish(clock.UtcNow, report);
        Flush(run);

        return report;
    }

    /// <summary>
    /// Termina una ejecución fallida (si <paramref name="failure"/> no es nulo) o cancelada: registra el motivo, ejecuta las
    /// compensaciones en orden, cierra el destino y emite el informe final.
    /// </summary>
    private async Task<CloneResultReport> AbortAsync(ExecutionRun run, (string Code, string Detail)? failure)
    {
        var execution = run.Execution;
        if (failure is { } fault)
        {
            execution.Fail(fault.Code, fault.Detail, clock.UtcNow);
        }
        else if (execution.State == ExecutionState.Running)
        {
            execution.RequestCancel();
        }

        Flush(run);

        foreach (var kind in execution.RequiredCompensations)
        {
            var (success, detail) = await RunCompensationAsync(run, kind);
            execution.CompleteCompensation(kind, success, detail, clock.UtcNow);
            Flush(run);
        }

        await DisposeTargetAfterAbortAsync(run);

        run.Elapsed = clock.UtcNow - run.StartedAt;
        var report = new CloneResultReport(
            failure is null ? CloneOutcome.Cancelled : CloneOutcome.Failed,
            run.Structural,
            Full: null,
            SourceRestored: null,
            run.Recovery,
            run.Elapsed,
            log.CurrentFile,
            [NextStepCode.KeepOriginalDiskUntilVerified]);

        execution.Finish(clock.UtcNow, report);
        Flush(run);

        return report;
    }

    /// <summary>
    /// Ejecuta una compensación. Usa <see cref="CancellationToken.None"/>: ya hay una cancelación en marcha y las compensaciones
    /// deben completarse. Un fallo se devuelve como resultado y no interrumpe las compensaciones siguientes.
    /// </summary>
    private static async Task<(bool Success, string? Detail)> RunCompensationAsync(ExecutionRun run, CompensationKind kind)
    {
        try
        {
            switch (kind)
            {
                case CompensationKind.DeleteTargetLayout:
                    if (run.Target is null)
                    {
                        return (false, "no hay disco de destino abierto para borrar el diseño");
                    }

                    await run.Target.DeleteLayoutAsync(CancellationToken.None);
                    return (true, null);

                case CompensationKind.DeleteSnapshot:
                    if (run.Snapshot is not { } snapshot)
                    {
                        return (true, "no llegó a crearse ninguna instantánea");
                    }

                    await snapshot.DisposeAsync();
                    run.Snapshot = null;
                    return (true, null);

                case CompensationKind.ReleaseMounts:
                    var failures = await ReleaseMountsAsync(run);
                    return failures.Count == 0 ? (true, null) : (false, string.Join("; ", failures));

                case CompensationKind.AllowSleep:
                    run.Sleep.Release();
                    return (true, null);

                default:
                    return (false, "compensación no soportada en la versión 1: " + kind);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.ToString());
        }
    }

    /// <summary>Cierra el destino después de las compensaciones. Un fallo se registra como aviso; el informe ya tiene el desenlace.</summary>
    private async Task DisposeTargetAfterAbortAsync(ExecutionRun run)
    {
        if (run.Target is not { } target)
        {
            return;
        }

        run.Target = null;
        try
        {
            await target.DisposeAsync();
        }
        catch (Exception ex)
        {
            log.Record(ExecutionLogEvents.CleanupFailed(run.Id, "DisposeTarget", ex.ToString()));
        }
    }

    /// <summary>Libera los montajes. Los que no se pueden liberar siguen en la lista y se devuelve el motivo de cada fallo.</summary>
    private static async Task<List<string>> ReleaseMountsAsync(ExecutionRun run)
    {
        var failures = new List<string>();
        var remaining = new List<TemporaryMount>();

        foreach (var mount in run.Mounts)
        {
            try
            {
                await mount.Release.DisposeAsync();
            }
            catch (Exception ex)
            {
                failures.Add(ex.ToString());
                remaining.Add(mount);
            }
        }

        run.Mounts.Clear();
        run.Mounts.AddRange(remaining);

        return failures;
    }

    private async Task<TemporaryMount> MountAsync(ExecutionRun run, PartitionGuid partition, CancellationToken ct)
    {
        var mount = await mounter.MountAsync(partition, ct);
        run.Mounts.Add(mount);

        return mount;
    }

    private void ReportCopyProgress(ExecutionRun run, ProgressEstimator estimator, long bytesDone)
    {
        lock (run.Gate)
        {
            var now = clock.UtcNow;
            if (run.LastProgressReport is { } last && now - last < ProgressReportInterval)
            {
                return;
            }

            run.LastProgressReport = now;
            run.Execution.ReportProgress(estimator.Update(bytesDone), now);
            Flush(run);
        }
    }

    /// <summary>Publica los eventos pendientes a los suscriptores y los registra. El progreso se registra como mucho cada 5 s.</summary>
    private void Flush(ExecutionRun run)
    {
        lock (run.Gate)
        {
            foreach (var domainEvent in run.Execution.Events)
            {
                var logged = domainEvent is not ProgressReported progress || ShouldLogProgress(run, progress.At);
                if (logged)
                {
                    log.Record(ToLogEvent(domainEvent, run));
                }

                _subject.Publish(domainEvent);
            }

            run.Execution.ClearEvents();
        }
    }

    private static bool ShouldLogProgress(ExecutionRun run, DateTimeOffset at)
    {
        if (run.LastProgressLog is { } last && at - last < ProgressLogInterval)
        {
            return false;
        }

        run.LastProgressLog = at;
        return true;
    }

    private static LogEvent ToLogEvent(ExecutionEvent domainEvent, ExecutionRun run) => domainEvent switch
    {
        ExecutionStarted => ExecutionLogEvents.Started(domainEvent.Id, run.Plan.Id),
        PhaseStarted started => ExecutionLogEvents.PhaseStarted(started.Id, started.Phase),
        PhaseCompleted completed => ExecutionLogEvents.PhaseCompleted(completed.Id, completed.Phase, completed.Duration),
        ProgressReported progress => ExecutionLogEvents.ProgressReported(progress.Id, progress.Progress),
        CompensationCompleted compensation => ExecutionLogEvents.CompensationCompleted(compensation.Id, compensation.Kind, compensation.Success, compensation.Detail),
        ExecutionCompleted completed => ExecutionLogEvents.Completed(completed.Id, run.Elapsed, run.RecoveryDetail),
        ExecutionCancelled cancelled => ExecutionLogEvents.Cancelled(cancelled.Id, run.Elapsed),
        ExecutionFailed failed => ExecutionLogEvents.Failed(failed.Id, run.Elapsed, failed.Failure),
        _ => throw new InvalidOperationException("Evento de ejecución sin registro: " + domainEvent.GetType().Name),
    };

    /// <summary>Abre el origen de lectura de una correspondencia: el volumen de la instantánea o el tramo en bruto del disco de origen.</summary>
    private IBlockSource OpenSource(ExecutionRun run, PartitionMapping mapping) => mapping.CopyMethod switch
    {
        CopyMethod.UsedClustersFromSnapshot => (run.Snapshot
            ?? throw new InvalidOperationException("No hay instantánea para copiar los clústeres ocupados."))
            .OpenVolume(mapping.Source.UniqueId),
        _ => rawReader.Open(run.Plan.Source, mapping.Source.Extent),
    };

    private static double InitialBytesPerSecond(ClonePlan plan)
    {
        var seconds = plan.Estimate.EstimatedDuration.TotalSeconds;
        return seconds > 0 ? plan.Estimate.BytesToCopy.Bytes / seconds : 0;
    }

    private static ITargetDisk RequireTarget(ExecutionRun run) =>
        run.Target ?? throw new InvalidOperationException("El disco de destino no está abierto.");

    /// <summary>Estado de una ejecución en curso: el agregado de dominio y los recursos que hay que liberar.</summary>
    private sealed class ExecutionRun(ClonePlan plan, CloneExecution execution, DateTimeOffset startedAt, SleepBlock sleep)
    {
        public object Gate { get; } = new();

        public ClonePlan Plan { get; } = plan;

        public CloneExecution Execution { get; } = execution;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public SleepBlock Sleep { get; } = sleep;

        public ExecutionId Id => Execution.Id;

        public Disk? FreshTarget { get; set; }

        public ISnapshotSet? Snapshot { get; set; }

        public ITargetDisk? Target { get; set; }

        public List<TemporaryMount> Mounts { get; } = [];

        public VerificationResult? Structural { get; set; }

        public RecoveryEnvironmentState? Recovery { get; set; }

        public string? RecoveryDetail { get; set; }

        public TimeSpan Elapsed { get; set; }

        public DateTimeOffset? LastProgressReport { get; set; }

        public DateTimeOffset? LastProgressLog { get; set; }
    }

    /// <summary>Bloqueo de suspensión que se libera una sola vez, desde la compensación AllowSleep o al terminar.</summary>
    private sealed class SleepBlock(IDisposable inner)
    {
        private bool _released;

        public void Release()
        {
            if (!_released)
            {
                _released = true;
                inner.Dispose();
            }
        }
    }

    /// <summary>Tramo de origen abierto para copiar: la correspondencia, el origen y su asignación de bytes ocupados.</summary>
    private sealed record CopyItem(PartitionMapping Mapping, IBlockSource Source, VolumeAllocation Allocation);

    /// <summary>Recibe los avances de un <see cref="IVolumeCopier"/> de forma síncrona.</summary>
    private sealed class CallbackProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    /// <summary>Un fallo de fase que el orquestador traduce a una ejecución fallida, con su código estable y su detalle.</summary>
    private sealed class ExecutionAbortException(string code, string detail) : Exception(detail)
    {
        public string Code { get; } = code;

        public string Detail { get; } = detail;
    }
}
