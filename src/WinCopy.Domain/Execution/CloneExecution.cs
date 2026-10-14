using WinCopy.Domain.Planning;

namespace WinCopy.Domain.Execution;

/// <summary>
/// Ejecución de un plan confirmado (agregado raíz y máquina de estados). Avanza por las fases planificadas, registra el
/// progreso y los fallos y, al fallar o cancelarse, obliga a completar las compensaciones antes de terminar.
/// <para>
/// Transiciones: Pending → Running (<see cref="Start"/>); Running → Completed (<see cref="Finish"/> con todas las fases);
/// Running o Cancelling → Failing (<see cref="Fail"/>); Failing → Failed y Cancelling → Cancelled (<see cref="Finish"/>,
/// tras las compensaciones); Running → Cancelling (<see cref="RequestCancel"/>), diferida durante CommitLayout.
/// </para>
/// <para>
/// Restauración del origen (planes con reducción): si RestoreSource falla en el camino feliz, no es un fallo de la ejecución.
/// Se registra con <see cref="RecordRestoreFailure"/>, el clon es válido y la ejecución termina como Completed con
/// <see cref="CloneResultReport.SourceRestored"/> = false. Si RestoreSource falla como compensación, queda registrado en
/// <see cref="Compensations"/> sin ocultar el fallo original (FR-021a).
/// </para>
/// <para>
/// Invariante: el disco de origen solo se modifica en ShrinkSource y RestoreSource (FR-021); ver <see cref="PhaseMayModifySource"/>.
/// </para>
/// </summary>
public sealed class CloneExecution
{
    private readonly List<ExecutionPhase> _completed = [];
    private readonly HashSet<ExecutionPhase> _begun = [];
    private readonly List<CompensationRecord> _compensations = [];
    private readonly List<ExecutionEvent> _events = [];
    private ExecutionPhase? _inProgress;
    private DateTimeOffset _phaseStartedAt;
    private bool _cancelPending;

    private CloneExecution(ExecutionId id, ClonePlan plan, IReadOnlyList<ExecutionPhase> plannedPhases)
    {
        Id = id;
        Plan = plan;
        PlannedPhases = plannedPhases;
    }

    /// <summary>Identificador de la ejecución.</summary>
    public ExecutionId Id { get; }

    /// <summary>Plan confirmado que se ejecuta.</summary>
    public ClonePlan Plan { get; }

    /// <summary>Fases que se ejecutarán, en orden. Las condicionales solo aparecen si aplican.</summary>
    public IReadOnlyList<ExecutionPhase> PlannedPhases { get; }

    /// <summary>Situación actual de la ejecución.</summary>
    public ExecutionState State { get; private set; } = ExecutionState.Pending;

    /// <summary>
    /// Fase en curso o la última iniciada. Al completarse la ejecución vale <see cref="ExecutionPhase.Done"/>.
    /// Es también la fase en la que se registra un fallo.
    /// </summary>
    public ExecutionPhase CurrentPhase { get; private set; } = ExecutionPhase.Revalidate;

    /// <summary>Último avance de la copia o de la verificación completa, si lo hay.</summary>
    public CloneProgress? Progress { get; private set; }

    /// <summary>Fallo que ha provocado la ejecución de compensaciones, si lo hay.</summary>
    public ExecutionFailure? Failure { get; private set; }

    /// <summary>Momento en que se registró el fallo.</summary>
    public DateTimeOffset? FailedAt { get; private set; }

    /// <summary>
    /// Motivo por el que no se pudo restaurar el tamaño original del origen en el camino feliz, o null si se restauró o si
    /// no hubo reducción. Con un valor, la ejecución termina como Completed con <see cref="CloneResultReport.SourceRestored"/> = false.
    /// </summary>
    public string? RestoreFailure { get; private set; }

    /// <summary>Resultado de cada compensación, en el orden en que se ejecutaron.</summary>
    public IReadOnlyList<CompensationRecord> Compensations => _compensations;

    /// <summary>Informe final, presente solo cuando la ejecución ha terminado.</summary>
    public CloneResultReport? Result { get; private set; }

    /// <summary>Eventos de dominio emitidos desde la última vez que se limpiaron.</summary>
    public IReadOnlyList<ExecutionEvent> Events => _events;

    /// <summary>
    /// Crea una ejecución del plan, que debe estar confirmado. Calcula las fases planificadas, pasa a Running y emite
    /// <see cref="ExecutionStarted"/>.
    /// </summary>
    public static CloneExecution Start(ExecutionId id, ClonePlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Status != PlanStatus.Confirmed)
        {
            throw new InvalidOperationException("Solo se puede ejecutar un plan confirmado.");
        }

        var execution = new CloneExecution(id, plan, PlanPhases(plan))
        {
            State = ExecutionState.Running,
        };
        execution._events.Add(new ExecutionStarted(id, now));

        return execution;
    }

    /// <summary>Indica si la fase puede modificar el disco de origen. Solo ShrinkSource y RestoreSource (FR-021).</summary>
    public static bool PhaseMayModifySource(ExecutionPhase phase) =>
        phase is ExecutionPhase.ShrinkSource or ExecutionPhase.RestoreSource;

    /// <summary>
    /// Inicia la fase, que debe ser la siguiente planificada. La anterior tiene que estar completada y la ejecución,
    /// en marcha.
    /// </summary>
    public void BeginPhase(ExecutionPhase phase, DateTimeOffset now)
    {
        if (State != ExecutionState.Running)
        {
            throw new InvalidOperationException($"No se puede iniciar la fase {phase} con la ejecución en estado {State}.");
        }

        if (_inProgress is { } active)
        {
            throw new InvalidOperationException($"La fase {active} sigue en curso; hay que completarla antes de iniciar {phase}.");
        }

        var next = _completed.Count < PlannedPhases.Count ? PlannedPhases[_completed.Count] : (ExecutionPhase?)null;
        if (next != phase)
        {
            throw new InvalidOperationException($"La siguiente fase planificada es {next}, no {phase}.");
        }

        _inProgress = phase;
        _phaseStartedAt = now;
        _begun.Add(phase);
        CurrentPhase = phase;
        _events.Add(new PhaseStarted(Id, now, phase));
    }

    /// <summary>
    /// Completa la fase en curso. Durante una cancelación, una fase que ya estaba en marcha puede terminar.
    /// Si la cancelación se pidió durante CommitLayout, al completarla la ejecución pasa a Cancelling.
    /// </summary>
    public void CompletePhase(ExecutionPhase phase, DateTimeOffset now)
    {
        if (State is not (ExecutionState.Running or ExecutionState.Cancelling))
        {
            throw new InvalidOperationException($"No se puede completar la fase {phase} con la ejecución en estado {State}.");
        }

        if (_inProgress != phase)
        {
            throw new InvalidOperationException($"La fase {phase} no está en curso.");
        }

        _completed.Add(phase);
        _inProgress = null;
        _events.Add(new PhaseCompleted(Id, now, phase, now - _phaseStartedAt));

        if (_cancelPending && phase == ExecutionPhase.CommitLayout)
        {
            _cancelPending = false;
            State = ExecutionState.Cancelling;
        }
    }

    /// <summary>
    /// Registra un avance. Solo se acepta mientras se copian datos (CopyData) o se verifican (FullVerification).
    /// </summary>
    public void ReportProgress(CloneProgress progress, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (State != ExecutionState.Running || _inProgress is not (ExecutionPhase.CopyData or ExecutionPhase.FullVerification))
        {
            throw new InvalidOperationException("El progreso solo se informa durante CopyData o FullVerification, en marcha.");
        }

        Progress = progress;
        _events.Add(new ProgressReported(Id, now, progress));
    }

    /// <summary>
    /// Registra un fallo y pasa a Failing. El fallo queda asociado a la fase actual. Un fallo durante una cancelación
    /// prevalece y la ejecución termina como fallida, no como cancelada.
    /// </summary>
    public void Fail(string code, string detail, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(detail);

        if (State is not (ExecutionState.Running or ExecutionState.Cancelling))
        {
            throw new InvalidOperationException($"No se puede registrar un fallo con la ejecución en estado {State}.");
        }

        Failure = new ExecutionFailure(code, CurrentPhase, detail);
        FailedAt = now;
        _cancelPending = false;
        State = ExecutionState.Failing;
    }

    /// <summary>
    /// Solicita la cancelación. Pasa a Cancelling, salvo durante CommitLayout: esa fase no se puede interrumpir, así que
    /// la cancelación queda pendiente hasta que termine. Repetir la solicitud pendiente no tiene efecto.
    /// </summary>
    public void RequestCancel()
    {
        if (State == ExecutionState.Running && _cancelPending)
        {
            return;
        }

        if (State != ExecutionState.Running)
        {
            throw new InvalidOperationException($"No se puede cancelar una ejecución en estado {State}.");
        }

        if (_inProgress == ExecutionPhase.CommitLayout)
        {
            _cancelPending = true;
            return;
        }

        State = ExecutionState.Cancelling;
    }

    /// <summary>
    /// Registra que no se ha podido restaurar el tamaño original del origen en el camino feliz (fase RestoreSource en marcha).
    /// No es un fallo de la ejecución: el clon es válido. Después hay que completar la fase y terminar como Completed con
    /// <see cref="CloneResultReport.SourceRestored"/> = false. Solo válido en un plan que reduce el origen.
    /// Un error no controlado de la restauración se registra con <see cref="Fail"/> y pasa por las compensaciones.
    /// </summary>
    public void RecordRestoreFailure(string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        if (Plan.SourceShrink is null)
        {
            throw new InvalidOperationException("La ejecución no reduce el origen: no hay nada que restaurar.");
        }

        if (State != ExecutionState.Running || _inProgress != ExecutionPhase.RestoreSource)
        {
            throw new InvalidOperationException("El fallo de la restauración solo se registra durante la fase RestoreSource, en marcha.");
        }

        RestoreFailure = detail;
    }

    /// <summary>
    /// Compensaciones que deben ejecutarse tras un fallo o una cancelación, en orden fijo y solo las que aplican:
    /// <list type="number">
    /// <item><description>DeleteTargetLayout, si se inició CommitLayout;</description></item>
    /// <item><description>DeleteSnapshot, si se inició Snapshot;</description></item>
    /// <item><description>ReleaseMounts, si se inició RepairBoot;</description></item>
    /// <item><description>RestoreSource, si se inició ShrinkSource;</description></item>
    /// <item><description>AllowSleep, siempre.</description></item>
    /// </list>
    /// Solo es válido en Failing y en Cancelling.
    /// </summary>
    public IReadOnlyList<CompensationKind> RequiredCompensations
    {
        get
        {
            if (State is not (ExecutionState.Failing or ExecutionState.Cancelling))
            {
                throw new InvalidOperationException("Las compensaciones solo se conocen en Failing o Cancelling.");
            }

            var required = new List<CompensationKind>();
            if (_begun.Contains(ExecutionPhase.CommitLayout))
            {
                required.Add(CompensationKind.DeleteTargetLayout);
            }

            if (_begun.Contains(ExecutionPhase.Snapshot))
            {
                required.Add(CompensationKind.DeleteSnapshot);
            }

            if (_begun.Contains(ExecutionPhase.RepairBoot))
            {
                required.Add(CompensationKind.ReleaseMounts);
            }

            if (_begun.Contains(ExecutionPhase.ShrinkSource))
            {
                required.Add(CompensationKind.RestoreSource);
            }

            required.Add(CompensationKind.AllowSleep);

            return required;
        }
    }

    /// <summary>
    /// Registra el resultado de la siguiente compensación. Deben completarse en el orden de
    /// <see cref="RequiredCompensations"/>. Un fallo se registra y no oculta el error original.
    /// </summary>
    public void CompleteCompensation(CompensationKind kind, bool success, string? detail, DateTimeOffset now)
    {
        var required = RequiredCompensations;
        var index = _compensations.Count;

        if (index >= required.Count || required[index] != kind)
        {
            var expected = index < required.Count ? required[index].ToString() : "ninguna";
            throw new InvalidOperationException($"Se esperaba la compensación {expected} y no {kind}.");
        }

        _compensations.Add(new CompensationRecord(kind, success, detail));
        _events.Add(new CompensationCompleted(Id, now, kind, success, detail));
    }

    /// <summary>
    /// Termina la ejecución con su informe. Completada exige todas las fases y, si el plan reduce el origen, que el informe
    /// diga si el origen se restauró; fallida o cancelada exige todas las compensaciones. El desenlace del informe debe
    /// corresponder al estado final.
    /// </summary>
    public void Finish(DateTimeOffset now, CloneResultReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        switch (State)
        {
            case ExecutionState.Running:
                if (_completed.Count != PlannedPhases.Count)
                {
                    throw new InvalidOperationException("Faltan fases por completar para terminar la ejecución.");
                }

                RequireOutcome(report, CloneOutcome.Completed);
                RequireRestoreOutcome(report);
                State = ExecutionState.Completed;
                CurrentPhase = ExecutionPhase.Done;
                _events.Add(new ExecutionCompleted(Id, now));
                break;

            case ExecutionState.Failing:
                RequireAllCompensations();
                RequireOutcome(report, CloneOutcome.Failed);
                State = ExecutionState.Failed;
                _events.Add(new ExecutionFailed(Id, now, Failure!));
                break;

            case ExecutionState.Cancelling:
                RequireAllCompensations();
                RequireOutcome(report, CloneOutcome.Cancelled);
                State = ExecutionState.Cancelled;
                _events.Add(new ExecutionCancelled(Id, now));
                break;

            default:
                throw new InvalidOperationException($"No se puede terminar una ejecución en estado {State}.");
        }

        Result = report;
    }

    /// <summary>Vacía la lista de eventos, una vez que se han registrado o entregado.</summary>
    public void ClearEvents() => _events.Clear();

    private void RequireAllCompensations()
    {
        if (_compensations.Count != RequiredCompensations.Count)
        {
            throw new InvalidOperationException("Hay compensaciones pendientes; hay que completarlas antes de terminar.");
        }
    }

    private static void RequireOutcome(CloneResultReport report, CloneOutcome expected)
    {
        if (report.Outcome != expected)
        {
            throw new ArgumentException($"El desenlace del informe ({report.Outcome}) no corresponde al estado final ({expected}).", nameof(report));
        }
    }

    /// <summary>En un plan que reduce el origen, el informe de una ejecución completada debe reflejar la restauración.</summary>
    private void RequireRestoreOutcome(CloneResultReport report)
    {
        if (Plan.SourceShrink is null)
        {
            return;
        }

        if (report.SourceRestored != (RestoreFailure is null))
        {
            throw new ArgumentException("El informe no coincide con el resultado de la restauración del origen.", nameof(report));
        }
    }

    /// <summary>
    /// Fases planificadas. ShrinkSource va justo después de Revalidate, para que la instantánea y la copia vean el origen
    /// reducido. RestoreSource es la última, tras la limpieza. Ambas solo aparecen si el plan reduce el origen.
    /// </summary>
    private static List<ExecutionPhase> PlanPhases(ClonePlan plan)
    {
        var shrinks = plan.SourceShrink is not null;
        var phases = new List<ExecutionPhase> { ExecutionPhase.Revalidate };

        if (shrinks)
        {
            phases.Add(ExecutionPhase.ShrinkSource);
        }

        phases.Add(ExecutionPhase.Snapshot);
        phases.Add(ExecutionPhase.PrepareTarget);
        phases.Add(ExecutionPhase.CopyData);
        phases.Add(ExecutionPhase.CommitLayout);

        if (plan.Mappings.Any(mapping => mapping.Resize == ResizeKind.GrowFileSystem))
        {
            phases.Add(ExecutionPhase.ExpandFileSystem);
        }

        phases.Add(ExecutionPhase.RepairBoot);
        phases.Add(ExecutionPhase.StructuralVerification);

        if (plan.Verification == VerificationMode.StructuralAndFull)
        {
            phases.Add(ExecutionPhase.FullVerification);
        }

        phases.Add(ExecutionPhase.Cleanup);

        if (shrinks)
        {
            phases.Add(ExecutionPhase.RestoreSource);
        }

        return phases;
    }
}
