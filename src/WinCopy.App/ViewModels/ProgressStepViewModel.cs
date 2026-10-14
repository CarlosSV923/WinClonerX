using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.App.Services;
using WinCopy.Application.UseCases.Execution;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Step 4 "Progress" (contracts/ui-wizard.md): runs the confirmed plan and shows its phases, the percentage, the bytes, the
/// throughput and the remaining time, from the execution events. The events come from a background thread and are
/// applied on the UI thread through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed partial class ProgressStepViewModel : ObservableObject
{
    private const double BytesPerMegabyte = 1024.0 * 1024.0;

    private readonly IExecuteClone _execute;
    private readonly IUiDispatcher _dispatcher;
    private IDisposable? _subscription;

    public ProgressStepViewModel(IExecuteClone execute, IUiDispatcher dispatcher)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// Phases in the order they started. The execution events do not list the planned phases, so each one appears when it starts.
    /// </summary>
    public ObservableCollection<PhaseItemViewModel> Phases { get; } = [];

    /// <summary>Share of the copy done, from 0 to 100.</summary>
    [ObservableProperty]
    private double _percent;

    /// <summary>Localized percentage, rounded down so it does not show 100% before the end.</summary>
    public string PercentText => Loc.Format("Progress_Percent", (int)Math.Floor(Percent));

    /// <summary>Localized "X GB of Y GB" (in Spanish, "X GB de Y GB").</summary>
    [ObservableProperty]
    private string _bytesText = string.Empty;

    /// <summary>Localized throughput in megabytes per second.</summary>
    [ObservableProperty]
    private string _throughputText = string.Empty;

    /// <summary>Localized remaining time, or "Calculating" while it is not known yet.</summary>
    [ObservableProperty]
    private string _etaText = string.Empty;

    /// <summary>Localized name of the phase in progress. Empty before the first phase starts.</summary>
    [ObservableProperty]
    private string _currentPhaseText = string.Empty;

    /// <summary>True while the execution runs.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// Stable code of the failure, when the run failed. It is the name of a <c>PlatformErrorCode</c> when the failure came from
    /// a known platform error. The result step uses it to show the matching message.
    /// </summary>
    [ObservableProperty]
    private string? _failureCode;

    /// <summary>
    /// Cancel is shown disabled until US3 adds the cancellation flow (FR-019 cancel, FR-024).
    /// TODO(US3): enable the Cancel button with its confirmation.
    /// </summary>
    public bool IsCancelAvailable { get; }

    partial void OnPercentChanged(double value) => OnPropertyChanged(nameof(PercentText));

    /// <summary>
    /// Runs the confirmed plan and returns its final report. Subscribes to the events before the run starts, and stops listening
    /// when the run ends, so the result is handed to the caller without later events changing this step.
    /// </summary>
    public async Task<CloneResultReport> StartAsync(PlanId planId, CancellationToken cancellationToken = default)
    {
        Reset();
        IsRunning = true;
        _subscription = _execute.Events.Subscribe(new EventObserver(this));
        try
        {
            return await _execute.ExecuteAsync(planId, cancellationToken);
        }
        finally
        {
            _subscription?.Dispose();
            _subscription = null;
            IsRunning = false;
        }
    }

    private void Reset()
    {
        Phases.Clear();
        Percent = 0;
        BytesText = string.Empty;
        ThroughputText = string.Empty;
        EtaText = string.Empty;
        CurrentPhaseText = string.Empty;
        FailureCode = null;
    }

    private void Apply(ExecutionEvent value)
    {
        switch (value)
        {
            case PhaseStarted started:
                var item = FindOrAddPhase(started.Phase);
                item.State = PhaseState.InProgress;
                CurrentPhaseText = Loc.Format("Progress_CurrentPhase", item.Name);
                break;

            case PhaseCompleted completed:
                FindOrAddPhase(completed.Phase).State = PhaseState.Done;
                break;

            case ProgressReported reported:
                ApplyProgress(reported.Progress);
                break;

            case ExecutionFailed failed:
                FailureCode = failed.Failure.Code;
                break;
        }
    }

    private void ApplyProgress(CloneProgress progress)
    {
        Percent = progress.Percent;
        BytesText = Loc.Format(
            "Progress_Bytes",
            SizeText.Format(new ByteSize(progress.BytesDone)),
            SizeText.Format(new ByteSize(progress.BytesTotal)));
        ThroughputText = Loc.Format(
            "Progress_Throughput",
            (progress.ThroughputBytesPerSecond / BytesPerMegabyte).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));
        EtaText = Loc.Format(
            "Progress_Eta",
            progress.Eta is { } eta ? DurationText.Format(eta) : Loc.Get("Progress_Eta_Calculating"));
    }

    private PhaseItemViewModel FindOrAddPhase(ExecutionPhase phase)
    {
        var existing = Phases.FirstOrDefault(item => item.Phase == phase);
        if (existing is not null)
        {
            return existing;
        }

        var added = new PhaseItemViewModel(phase);
        Phases.Add(added);
        return added;
    }

    /// <summary>Receives the events of the run and applies each one on the UI thread, in order.</summary>
    private sealed class EventObserver(ProgressStepViewModel owner) : IObserver<ExecutionEvent>
    {
        public void OnNext(ExecutionEvent value) => owner._dispatcher.Post(() => owner.Apply(value));

        public void OnError(Exception error)
        {
            // The use case reports failures in the result, not as observable errors.
        }

        public void OnCompleted()
        {
            // Nothing to do: the run ends when ExecuteAsync returns.
        }
    }
}
