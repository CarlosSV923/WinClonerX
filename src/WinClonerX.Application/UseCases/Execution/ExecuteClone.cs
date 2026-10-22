using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.BootRepair;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.UseCases.Execution;

/// <summary>
/// Orquestador del clonado (US1). Aplica un plan confirmado en las fases de <see cref="ExecutionPhase"/>, en el orden del
/// dominio, y publica cada hecho como <see cref="ExecutionEvent"/> y en el registro. El disco de origen solo se lee: todas
/// las escrituras van al destino. Si un disco de la ejecución se desconecta (<see cref="IDeviceRemovalMonitor"/>), la ejecución
/// falla con el código de esa desconexión. Ante un fallo o una cancelación ejecuta las compensaciones que indica
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
    IVolumeComparer comparer,
    IFileSystemExpander expander,
    ITemporaryMounter mounter,
    IOfflineRegistryEditor registry,
    IBootConfigurator bootConfigurator,
    IRecoveryConfigurator recoveryConfigurator,
    IStructuralVerifier verifier,
    ISourceResizer resizer,
    ISourceRestoreJournal journal,
    IPowerManager power,
    IDeviceRemovalMonitor removals,
    IExecutionLog log,
    IClock clock) : IExecuteClone
{
    private const string DisksChangedReason = "DisksChanged";
    private const string ShrinkInsufficientCode = "ShrinkInsufficient";
    private const string ShrinkFailedCode = "ShrinkFailed";
    private const string ShrinkJournalFailedCode = "ShrinkJournalFailed";

    /// <summary>Número máximo de desplazamientos distintos que el informe de la verificación completa lleva.</summary>
    private const int MaxRecordedDifferenceOffsets = 10;

    private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ProgressLogInterval = TimeSpan.FromSeconds(5);

    private readonly ExecutionEventSubject _subject = new();

    /// <summary>Protege la ejecución activa, que la solicitud de cancelación lee desde la interfaz.</summary>
    private readonly object _activeGate = new();

    /// <summary>Ejecución en curso, o null. Solo hay una a la vez.</summary>
    private ExecutionRun? _active;

    /// <inheritdoc />
    public IObservable<ExecutionEvent> Events => _subject;

    /// <inheritdoc />
    public void RequestCancel()
    {
        ExecutionRun? run;
        lock (_activeGate)
        {
            run = _active;
        }

        if (run is not null)
        {
            ExecutionPhase phase;
            bool pending;
            bool interrupt;
            lock (run.Gate)
            {
                if (run.CancelRequestLogged || !run.Execution.CanRequestCancel)
                {
                    return;
                }

                run.Execution.RequestCancel();
                run.CancelRequestLogged = true;
                phase = run.Execution.CurrentPhase;
                pending = run.Execution.CancelPending;
                interrupt = run.Execution.State == ExecutionState.Cancelling;
            }

            Record(ExecutionLogEvents.CancelRequested(phase, pending));
            if (interrupt)
            {
                run.Interrupt.Cancel();
            }
        }
    }

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

        var sleep = new SleepBlock(power.PreventSleep());
        try
        {
            var startedAt = clock.UtcNow;
            var run = new ExecutionRun(
                plan,
                CloneExecution.Start(new ExecutionId(Guid.NewGuid()), plan, startedAt),
                startedAt,
                sleep,
                // No se libera: RequestCancel puede señalarlo justo después de que la ejecución termine.
                new CancellationTokenSource());

            // El token de la llamada y RequestCancel pasan por el mismo camino: una cancelación con token queda sujeta a las mismas reglas por fase.
            using var cancelRegistration = ct.Register(() => Transition(run, execution => execution.RequestCancel()));
            lock (_activeGate)
            {
                _active = run;
            }

            IDisposable? removalWatch = null;
            try
            {
                Flush(run);

                // Los discos de la ejecución se vigilan mientras dura: una retirada aborta el clonado como fallo (US3, escenario 2).
                // Si la vigilancia no se puede activar, el clonado sigue: una retirada se detecta igualmente por el error de E/S
                // que provoca, solo que más tarde. Se deja constancia en el registro.
                try
                {
                    removalWatch = removals.Watch([plan.Source.Number, plan.Target.Number], change => OnDiskRemoved(run, change));
                }
                catch (Exception ex)
                {
                    Record(ExecutionLogEvents.DeviceMonitorUnavailable(ex.ToString()));
                }

                await RunPhasesAsync(run);
                return FinishCompleted(run);
            }
            catch (OperationCanceledException) when (IsCancelling(run) || HasDeviceLoss(run))
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
            finally
            {
                removalWatch?.Dispose();

                lock (_activeGate)
                {
                    if (ReferenceEquals(_active, run))
                    {
                        _active = null;
                    }
                }
            }
        }
        finally
        {
            sleep.Release();
        }
    }

    private async Task RunPhasesAsync(ExecutionRun run)
    {
        await RunPhaseAsync(run, ExecutionPhase.Revalidate, token => RevalidateAsync(run, token));

        // La reducción del origen va antes de la instantánea: la copia ve el origen ya reducido (FR-021).
        if (run.Plan.SourceShrink is not null)
        {
            await RunPhaseAsync(run, ExecutionPhase.ShrinkSource, _ => ShrinkSourceAsync(run));
        }

        await RunPhaseAsync(run, ExecutionPhase.Snapshot, token => SnapshotAsync(run, token));
        await RunPhaseAsync(run, ExecutionPhase.PrepareTarget, token => PrepareTargetAsync(run, token));
        await RunPhaseAsync(run, ExecutionPhase.CopyData, token => CopyDataAsync(run, token));

        // La comparación va antes del diseño: el destino sigue fuera de línea y sin volúmenes montados (R18).
        if (run.Plan.Verification == VerificationMode.StructuralAndFull)
        {
            await RunPhaseAsync(run, ExecutionPhase.FullVerification, token => FullVerificationAsync(run, token));
        }

        await RunPhaseAsync(run, ExecutionPhase.CommitLayout, _ => CommitLayoutAsync(run));

        if (run.Plan.Mappings.Any(mapping => mapping.Resize == ResizeKind.GrowFileSystem))
        {
            await RunPhaseAsync(run, ExecutionPhase.ExpandFileSystem, token => ExpandFileSystemAsync(run, token));
        }

        await RunPhaseAsync(run, ExecutionPhase.RepairBoot, token => RepairBootAsync(run, token));
        await RunPhaseAsync(run, ExecutionPhase.StructuralVerification, token => VerifyStructureAsync(run, token));
        await RunPhaseAsync(run, ExecutionPhase.Cleanup, _ => CleanupAsync(run));

        // La restauración del origen es lo último del camino feliz: después de la limpieza y de una verificación correcta.
        if (run.Plan.SourceShrink is not null)
        {
            await RunPhaseAsync(run, ExecutionPhase.RestoreSource, _ => RestoreSourceAsync(run));
        }
    }

    /// <summary>
    /// Ejecuta una fase: no empieza si la ejecución se está cancelando, la marca como iniciada y como completada, y publica sus
    /// eventos. Solo una fase interrumpible (<see cref="CloneExecution.IsInterruptible"/>) recibe el token de interrupción. Las
    /// demás reciben <see cref="CancellationToken.None"/>: una cancelación pedida durante ellas se hace efectiva al completarlas,
    /// antes de la siguiente fase.
    /// </summary>
    private async Task RunPhaseAsync(ExecutionRun run, ExecutionPhase phase, Func<CancellationToken, Task> body)
    {
        // La comprobación y el inicio de la fase van bajo el mismo bloqueo: una cancelación que llega justo entre ambos no puede
        // hacer que BeginPhase falle y convierta la cancelación en un fallo.
        Transition(run, execution =>
        {
            // Una retirada de disco pendiente impide empezar otra fase, igual que una cancelación: la causa va en DeviceLoss.
            if (execution.State == ExecutionState.Cancelling || run.DeviceLoss is not null)
            {
                throw new OperationCanceledException("La ejecución se está cancelando o se ha desconectado un disco.");
            }

            execution.BeginPhase(phase, clock.UtcNow);
        });
        Flush(run);

        var token = CloneExecution.IsInterruptible(phase) ? run.Interrupt.Token : CancellationToken.None;
        await body(token);

        Transition(run, execution => execution.CompletePhase(phase, clock.UtcNow));
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
            Record(ExecutionLogEvents.PlanInvalidated(plan.Id, DisksChangedReason));
            throw new ExecutionAbortException(DisksChangedReason, "El inventario de los discos cambió después de planificar; hay que volver a planificar.");
        }

        run.FreshTarget = fresh;
    }

    /// <summary>
    /// Reduce temporalmente la partición de Windows del origen (FR-021). Antes de tocar nada consulta el mínimo que admite
    /// Windows: si ha subido por encima de la longitud reducida planificada, falla con ShrinkInsufficient sin reducir ni abrir
    /// el destino (US2, escenario 2). Solo se usa la partición de la reducción del plan.
    /// <para>
    /// Fase no interrumpible: una reducción en curso debe terminar, así que no recibe el token de cancelación.
    /// </para>
    /// </summary>
    private async Task ShrinkSourceAsync(ExecutionRun run)
    {
        var shrink = RequireShrink(run);

        ByteSize minimum;
        try
        {
            minimum = await resizer.GetMinimumSizeAsync(shrink.Partition, CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new ExecutionAbortException(ShrinkFailedCode, ex.ToString());
        }

        if (minimum > shrink.ShrunkLength)
        {
            throw new ExecutionAbortException(
                ShrinkInsufficientCode,
                $"El mínimo que admite Windows ({minimum.Bytes} bytes) supera la longitud reducida planificada ({shrink.ShrunkLength.Bytes} bytes).");
        }

        // El diario va antes de la reducción: si el proceso muere con el origen reducido, la siguiente ejecución lo restaura.
        // Sin diario no se reduce nunca (FR-021a).
        RecordJournal(run, shrink);

        // A partir de aquí la compensación RestoreSource debe restaurar: una reducción fallida puede haber cambiado el origen.
        run.ShrinkAttempted = true;
        try
        {
            await resizer.ShrinkAsync(shrink.Partition, shrink.ShrunkLength, CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new ExecutionAbortException(ShrinkFailedCode, ex.ToString());
        }
    }

    /// <summary>
    /// Restaura el tamaño original del origen en el camino feliz. No se interrumpe con la cancelación: el clon ya está
    /// verificado. Un fallo no invalida el clon: se registra en la ejecución y en el registro (SourceRestoreFailed), y la
    /// ejecución termina como Completed con SourceRestored = false.
    /// </summary>
    private async Task RestoreSourceAsync(ExecutionRun run)
    {
        var shrink = RequireShrink(run);

        try
        {
            await resizer.RestoreAsync(shrink.Partition, shrink.OriginalLength, CancellationToken.None);
            run.SourceRestored = true;
        }
        catch (Exception ex)
        {
            var detail = ex.ToString();
            run.Execution.RecordRestoreFailure(detail);
            run.SourceRestored = false;
            Record(ExecutionLogEvents.SourceRestoreFailed(shrink.Partition, shrink.OriginalLength, detail));
            return;
        }

        ClearJournal(shrink);
    }

    /// <summary>
    /// Escribe la entrada del diario antes de reducir. Si ya hay una entrada pendiente, o el diario no se puede leer, no se reduce:
    /// una entrada nueva sustituiría la que falta por restaurar. Cualquier fallo de escritura aborta la ejecución antes de tocar
    /// el origen.
    /// </summary>
    private void RecordJournal(ExecutionRun run, SourceShrinkStep shrink)
    {
        PendingSourceRestoreRead pending;
        try
        {
            pending = journal.Read();
        }
        catch (Exception ex)
        {
            throw new ExecutionAbortException(ShrinkJournalFailedCode, ex.ToString());
        }

        if (pending.State != PendingSourceRestoreState.Absent)
        {
            throw new ExecutionAbortException(
                ShrinkJournalFailedCode,
                "Hay una restauración del origen pendiente de una ejecución anterior. Reinicia WinClonerX para resolverla antes de reducir. "
                + (pending.Detail ?? string.Empty));
        }

        var entry = new PendingSourceRestore(shrink.Partition, shrink.OriginalLength, run.Plan.Id, clock.UtcNow);
        try
        {
            journal.Record(entry);
        }
        catch (Exception ex)
        {
            throw new ExecutionAbortException(ShrinkJournalFailedCode, ex.ToString());
        }

        Record(ExecutionLogEvents.SourceRestoreJournalWritten(entry));
    }

    /// <summary>
    /// Borra la entrada del diario una vez el origen tiene su longitud original. Un fallo es un aviso y nunca cambia el
    /// desenlace: el origen ya está bien, y la siguiente ejecución comprobará la entrada y la borrará.
    /// </summary>
    private void ClearJournal(SourceShrinkStep shrink)
    {
        try
        {
            journal.Clear();
            Record(ExecutionLogEvents.SourceRestoreJournalCleared(shrink.Partition));
        }
        catch (Exception ex)
        {
            Record(ExecutionLogEvents.SourceRestoreJournalClearFailed(shrink.Partition, ex.ToString()));
        }
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
                ct.ThrowIfCancellationRequested();

                var source = OpenSource(run, mapping);
                opened.Add(source);
                var allocation = await source.GetAllocationAsync(ct);
                items.Add(new CopyItem(mapping, source, allocation));
            }

            var total = items.Sum(item => item.Allocation.TotalUsed.Bytes);
            var estimator = new ProgressEstimator(total, clock, InitialBytesPerSecond(run.Plan));

            // La verificación completa compara exactamente estos tramos, leídos de la misma instantánea.
            run.Copied = items.Select(item => new CopiedRange(item.Mapping, item.Allocation)).ToArray();

            long completed = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                var before = completed;
                var progress = new CallbackProgress(bytes => ReportTransferProgress(run, estimator, before + bytes));

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
    /// Compara byte a byte cada tramo copiado con el origen: los mismos tramos ocupados que copió <see cref="CopyDataAsync"/>,
    /// leídos de la misma instantánea, y los bytes del destino en su desplazamiento. Corre con el destino fuera de línea y sin
    /// volúmenes montados, antes del diseño (R18). Es interrumpible: una cancelación se atiende entre fragmentos.
    /// <para>
    /// Un byte distinto no es un error de lectura: la fase falla con <see cref="PlatformErrorCode.VerificationFailed"/> y el
    /// informe lleva el número de bytes distintos y los primeros desplazamientos del disco de destino (diez como mucho). Los
    /// desplazamientos de cada partición también van en el detalle, para saber en cuál están.
    /// </para>
    /// </summary>
    private async Task FullVerificationAsync(ExecutionRun run, CancellationToken ct)
    {
        var target = RequireTarget(run);
        var copied = run.Copied ?? throw new InvalidOperationException("No hay tramos copiados que comparar.");

        var total = copied.Sum(item => item.Allocation.TotalUsed.Bytes);
        var estimator = new ProgressEstimator(total, clock, InitialBytesPerSecond(run.Plan));

        var partitions = new List<PartitionComparison>(copied.Count);
        long completed = 0;
        foreach (var item in copied)
        {
            ct.ThrowIfCancellationRequested();

            var before = completed;
            var progress = new CallbackProgress(bytes => ReportTransferProgress(run, estimator, before + bytes));

            // Se abre de nuevo el mismo origen: la instantánea sigue viva hasta la limpieza.
            using IBlockSource source = OpenSource(run, item.Mapping);
            RangeComparison comparison = await comparer.CompareAsync(
                source,
                item.Allocation,
                target,
                item.Mapping.TargetExtent.Offset,
                progress,
                ct);

            completed += item.Allocation.TotalUsed.Bytes;
            partitions.Add(new PartitionComparison(item.Mapping, comparison));
        }

        // Avance final exacto, sin esperar al intervalo, como al terminar la copia.
        lock (run.Gate)
        {
            var now = clock.UtcNow;
            run.LastProgressReport = now;
            run.Execution.ReportProgress(estimator.Update(total), now);
            Flush(run);
        }

        var result = BuildFullResult(partitions);
        run.Full = result;
        Record(ExecutionLogEvents.FullVerificationCompleted(result));

        if (!result.Passed)
        {
            throw new PlatformException(PlatformErrorCode.VerificationFailed, string.Join("; ", result.Issues));
        }
    }

    /// <summary>
    /// Une los resultados de las particiones en el de la verificación completa. Los desplazamientos pasan a ser del disco de
    /// destino, como en el registro, y el total de la lista se limita a <see cref="MaxRecordedDifferenceOffsets"/>.
    /// </summary>
    private static VerificationResult BuildFullResult(IReadOnlyList<PartitionComparison> partitions)
    {
        var differing = partitions.Sum(partition => partition.Comparison.DifferingBytes);
        if (differing == 0)
        {
            return new VerificationResult(true, [], 0, []);
        }

        var offsets = partitions
            .SelectMany(partition => partition.Comparison.FirstDifferenceOffsets
                .Select(offset => checked(partition.Mapping.TargetExtent.Offset + offset)))
            .Take(MaxRecordedDifferenceOffsets)
            .ToArray();

        var issues = partitions
            .Where(partition => partition.Comparison.DifferingBytes > 0)
            .Select(partition =>
                $"Partition {partition.Mapping.NewUniqueId:B} ({partition.Mapping.Source.Role}): {partition.Comparison.DifferingBytes} bytes differ; "
                + "first at target disk offsets "
                + string.Join(", ", partition.Comparison.FirstDifferenceOffsets.Select(offset =>
                    (partition.Mapping.TargetExtent.Offset + offset).ToString(System.Globalization.CultureInfo.InvariantCulture))))
            .ToArray();

        return new VerificationResult(false, issues, differing, offsets);
    }

    /// <summary>
    /// Escribe el diseño y lo pone en línea. Fase no interrumpible: una cancelación pedida aquí se atiende al completarla
    /// (<see cref="CloneExecution.IsInterruptible"/>).
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
        await bootConfigurator.RebuildAsync(windows.Root, esp.Root, recovery?.Root, ct);

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
            LogCleanupFailure("ReleaseMounts", failure);
        }

        if (run.Snapshot is { } snapshot)
        {
            try
            {
                await snapshot.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogCleanupFailure("DisposeSnapshot", ex.ToString());
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
                LogCleanupFailure("DisposeTarget", ex.ToString());
            }

            run.Target = null;
        }
    }

    private void LogCleanupFailure(string step, string technicalDetail) =>
        Record(ExecutionLogEvents.CleanupFailed(step, technicalDetail));

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
            Full: run.Full,
            SourceRestored: SourceRestoredFor(run),
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

        // Una retirada de disco es la causa raíz: prevalece sobre el fallo o la cancelación que haya llegado con ella.
        lock (run.Gate)
        {
            failure = run.DeviceLoss ?? failure;
        }

        if (failure is { } fault)
        {
            Transition(run, e => e.Fail(fault.Code, fault.Detail, clock.UtcNow));
        }

        Flush(run);

        foreach (var kind in execution.RequiredCompensations)
        {
            var (success, detail) = await RunCompensationAsync(run, kind);
            if (kind == CompensationKind.RestoreSource)
            {
                run.SourceRestored = success;
            }

            execution.CompleteCompensation(kind, success, detail, clock.UtcNow);
            Flush(run);
        }

        await DisposeTargetAfterAbortAsync(run);

        run.Elapsed = clock.UtcNow - run.StartedAt;
        var report = new CloneResultReport(
            failure is null ? CloneOutcome.Cancelled : CloneOutcome.Failed,
            run.Structural,
            Full: run.Full,
            SourceRestored: SourceRestoredFor(run),
            run.Recovery,
            run.Elapsed,
            log.CurrentFile,
            [NextStepCode.KeepOriginalDiskUntilVerified],
            Failure: failure is null ? null : execution.Failure);

        execution.Finish(clock.UtcNow, report);
        Flush(run);

        return report;
    }

    /// <summary>
    /// Ejecuta una compensación. Usa <see cref="CancellationToken.None"/>: ya hay una cancelación en marcha y las compensaciones
    /// deben completarse. Un fallo se devuelve como resultado y no interrumpe las compensaciones siguientes.
    /// </summary>
    private async Task<(bool Success, string? Detail)> RunCompensationAsync(ExecutionRun run, CompensationKind kind)
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

                case CompensationKind.RestoreSource:
                    // Si la reducción nunca se intentó, el origen no se ha tocado: no hay nada que restaurar y no se llama al puerto.
                    if (!run.ShrinkAttempted)
                    {
                        return (true, "la reducción no llegó a intentarse; el origen no se ha modificado");
                    }

                    var shrink = RequireShrink(run);
                    await resizer.RestoreAsync(shrink.Partition, shrink.OriginalLength, CancellationToken.None);

                    // Si la restauración falla, la excepción sale aquí y la entrada del diario se conserva para el siguiente inicio.
                    ClearJournal(shrink);
                    return (true, null);

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
            Record(ExecutionLogEvents.CleanupFailed("DisposeTarget", ex.ToString()));
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

    private void ReportTransferProgress(ExecutionRun run, ProgressEstimator estimator, long bytesDone)
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

    /// <summary>
    /// Cambia el agregado bajo el bloqueo de la ejecución. Si después del cambio la ejecución está cancelándose, interrumpe la
    /// fase en curso. La señal se envía fuera del bloqueo, para que los suscriptores de la cancelación no se ejecuten con él tomado.
    /// </summary>
    private static void Transition(ExecutionRun run, Action<CloneExecution> change)
    {
        bool interrupt;
        lock (run.Gate)
        {
            change(run.Execution);
            interrupt = run.Execution.State == ExecutionState.Cancelling;
        }

        if (interrupt)
        {
            run.Interrupt.Cancel();
        }
    }

    private static bool IsCancelling(ExecutionRun run)
    {
        lock (run.Gate)
        {
            return run.Execution.State == ExecutionState.Cancelling;
        }
    }

    private static bool HasDeviceLoss(ExecutionRun run)
    {
        lock (run.Gate)
        {
            return run.DeviceLoss is not null;
        }
    }

    /// <summary>
    /// Recibe la retirada de un disco de la ejecución. Llega desde el hilo de notificación del sistema. Registra la causa, que
    /// prevalece sobre cualquier fallo o cancelación posterior, e interrumpe la fase en curso si es interruptible. Una fase no
    /// interruptible termina primero; después la ejecución no empieza la siguiente (<see cref="RunPhaseAsync"/>).
    /// <para>
    /// Una retirada posterior a la verificación estructural no hace nada: el clon ya está verificado y solo quedan la limpieza
    /// y la restauración del origen, que cierran la ejecución por su cuenta.
    /// </para>
    /// </summary>
    private static void OnDiskRemoved(ExecutionRun run, DeviceChange change)
    {
        if (change.Kind != DeviceChangeKind.Removed || change.DiskNumber is not { } number)
        {
            return;
        }

        (string Code, string Detail) loss;
        if (number == run.Plan.Source.Number)
        {
            loss = (PlatformErrorCode.SourceDeviceRemoved.ToString(),
                $"El disco de origen (número {number}) se desconectó durante la ejecución.");
        }
        else if (number == run.Plan.Target.Number)
        {
            loss = (PlatformErrorCode.TargetDeviceRemoved.ToString(),
                $"El disco de destino (número {number}) se desconectó durante la ejecución.");
        }
        else
        {
            return;
        }

        lock (run.Gate)
        {
            bool inactive = run.Execution.State is not (ExecutionState.Running or ExecutionState.Cancelling);
            if (run.DeviceLoss is not null || inactive || run.Structural is { Passed: true })
            {
                return;
            }

            run.DeviceLoss = loss;
        }

        run.Interrupt.Cancel();
    }

    /// <summary>
    /// Escribe un evento en el registro sin dejar que un fallo del registro interrumpa la ejecución: si el registro lanza una
    /// excepción, la fase en curso y las compensaciones deben seguir igualmente (una compensación saltada dejaría el destino o
    /// el origen a medio hacer). Los eventos se publican a los suscriptores aunque el registro falle.
    /// </summary>
    private void Record(LogEvent e)
    {
        try
        {
            log.Record(e);
        }
        catch (Exception)
        {
            // A propósito: un fallo al escribir el registro no debe detener la fase ni saltarse una compensación.
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
                    Record(ToLogEvent(domainEvent, run));
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
        ExecutionStarted => ExecutionLogEvents.Started(run.Plan.Id),
        PhaseStarted started => ExecutionLogEvents.PhaseStarted(started.Phase),
        PhaseCompleted completed => ExecutionLogEvents.PhaseCompleted(completed.Phase, completed.Duration),
        ProgressReported progress => ExecutionLogEvents.ProgressReported(progress.Progress),
        CompensationCompleted compensation => ExecutionLogEvents.CompensationCompleted(compensation.Kind, compensation.Success, compensation.Detail),
        ExecutionCompleted completed => ExecutionLogEvents.Completed(run.Elapsed, run.RecoveryDetail),
        ExecutionCancelled => ExecutionLogEvents.Cancelled(run.Elapsed),
        ExecutionFailed failed => ExecutionLogEvents.Failed(run.Elapsed, failed.Failure),
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

    private static SourceShrinkStep RequireShrink(ExecutionRun run) =>
        run.Plan.SourceShrink ?? throw new InvalidOperationException("El plan no reduce el origen.");

    /// <summary>
    /// Estado del origen para el informe: null si el plan no reduce el origen. Si lo reduce, true salvo que la restauración
    /// haya fallado. Si la reducción nunca llegó a empezar, el origen no se ha tocado, así que también es true.
    /// </summary>
    private static bool? SourceRestoredFor(ExecutionRun run) =>
        run.Plan.SourceShrink is null ? null : run.SourceRestored ?? true;

    /// <summary>Estado de una ejecución en curso: el agregado de dominio y los recursos que hay que liberar.</summary>
    private sealed class ExecutionRun(
        ClonePlan plan,
        CloneExecution execution,
        DateTimeOffset startedAt,
        SleepBlock sleep,
        CancellationTokenSource interrupt)
    {
        public object Gate { get; } = new();

        public bool CancelRequestLogged { get; set; }

        public ClonePlan Plan { get; } = plan;

        public CloneExecution Execution { get; } = execution;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public SleepBlock Sleep { get; } = sleep;

        /// <summary>Se cancela cuando el agregado pasa a Cancelling. Las fases interrumpibles reciben su token.</summary>
        public CancellationTokenSource Interrupt { get; } = interrupt;

        /// <summary>
        /// Código y detalle de la retirada de un disco de la ejecución, o null. Se escribe y se lee bajo <see cref="Gate"/>.
        /// Con un valor, la ejecución termina como fallida con ese código (<see cref="OnDiskRemoved"/>).
        /// </summary>
        public (string Code, string Detail)? DeviceLoss { get; set; }

        public Disk? FreshTarget { get; set; }

        /// <summary>Verdadero si ShrinkAsync se llegó a llamar: a partir de ahí la compensación RestoreSource debe restaurar.</summary>
        public bool ShrinkAttempted { get; set; }

        /// <summary>Estado del origen tras la restauración del camino feliz o la compensación RestoreSource. Null si no ha habido.</summary>
        public bool? SourceRestored { get; set; }

        public ISnapshotSet? Snapshot { get; set; }

        public ITargetDisk? Target { get; set; }

        public List<TemporaryMount> Mounts { get; } = [];

        public VerificationResult? Structural { get; set; }

        /// <summary>Resultado de la verificación completa, o null si no se hizo.</summary>
        public VerificationResult? Full { get; set; }

        /// <summary>Tramos copiados, con su correspondencia, para compararlos después. Null hasta que empieza la copia.</summary>
        public IReadOnlyList<CopiedRange>? Copied { get; set; }

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

    /// <summary>Correspondencia copiada y sus tramos ocupados: lo que compara la verificación completa.</summary>
    private sealed record CopiedRange(PartitionMapping Mapping, VolumeAllocation Allocation);

    /// <summary>Resultado de comparar una correspondencia con el destino.</summary>
    private sealed record PartitionComparison(PartitionMapping Mapping, RangeComparison Comparison);

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
