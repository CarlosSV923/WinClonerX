using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private const double BytesPerMebibyte = 1024.0 * 1024.0;

    private readonly IExecuteClone _execute;
    private readonly IUiDispatcher _dispatcher;
    private readonly IConfirmationService _confirmation;
    private IDisposable? _subscription;

    /// <summary>Phase in progress, from the events. Null between phases. Decides whether a requested cancel waits for it.</summary>
    private ExecutionPhase? _runningPhase;

    public ProgressStepViewModel(IExecuteClone execute, IUiDispatcher dispatcher, IConfirmationService confirmation)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
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

    /// <summary>Localized "X GiB of Y GiB" (in Spanish, "X GiB de Y GiB").</summary>
    [ObservableProperty]
    private string _bytesText = string.Empty;

    /// <summary>Localized throughput in mebibytes per second.</summary>
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
    /// True while the Cancel button can be used: the run is in progress, no cancel has been requested and the structural
    /// verification has not passed. After that the clone is valid and a cancel is ignored (FR-024).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancelAvailable;

    /// <summary>True once the person has confirmed the cancel. The run then stops at the next safe point.</summary>
    [ObservableProperty]
    private bool _isCancelRequested;

    /// <summary>
    /// Localized state of a requested cancel: "Cancelling…", or "Finishing the current safe step…" while a phase that cannot be
    /// interrupted runs. Empty when no cancel was requested.
    /// </summary>
    [ObservableProperty]
    private string _cancelStatusText = string.Empty;

    /// <summary>True when <see cref="CancelStatusText"/> has text to show.</summary>
    public bool HasCancelStatus => CancelStatusText.Length > 0;

    partial void OnPercentChanged(double value) => OnPropertyChanged(nameof(PercentText));

    partial void OnCancelStatusTextChanged(string value) => OnPropertyChanged(nameof(HasCancelStatus));

    /// <summary>
    /// Runs the confirmed plan and returns its final report. Subscribes to the events before the run starts, and stops listening
    /// when the run ends, so the result is handed to the caller without later events changing this step.
    /// </summary>
    public async Task<CloneResultReport> StartAsync(PlanId planId, CancellationToken cancellationToken = default)
    {
        Reset();
        IsRunning = true;
        IsCancelAvailable = true;
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
            IsCancelAvailable = false;
        }
    }

    /// <summary>
    /// Asks the person to confirm the cancel and, if they do, requests it (FR-024). Returns true when the request was made.
    /// Once requested, the Cancel button is disabled. Declining or a cancel that no longer applies leaves the run as it is.
    /// </summary>
    public bool TryRequestCancel()
    {
        if (!IsCancelAvailable)
        {
            return false;
        }

        var question = new ConfirmationRequest(
            Loc.Get("App_Title"),
            Loc.Get("Cancel_Confirm_Message"),
            Loc.Get("Cancel_Confirm_Yes"),
            Loc.Get("Cancel_Confirm_No"));
        if (!_confirmation.Confirm(question))
        {
            return false;
        }

        IsCancelAvailable = false;
        IsCancelRequested = true;
        _execute.RequestCancel();
        RefreshCancelStatus();
        return true;
    }

    [RelayCommand(CanExecute = nameof(IsCancelAvailable))]
    private void Cancel() => TryRequestCancel();

    private void Reset()
    {
        Phases.Clear();
        Percent = 0;
        BytesText = string.Empty;
        ThroughputText = string.Empty;
        EtaText = string.Empty;
        CurrentPhaseText = string.Empty;
        IsCancelRequested = false;
        CancelStatusText = string.Empty;
        _runningPhase = null;
    }

    /// <summary>
    /// Shows the cancel state: "Finishing the current safe step…" while a phase that cannot be interrupted runs, otherwise
    /// "Cancelling…". Empty when no cancel was requested.
    /// </summary>
    private void RefreshCancelStatus()
    {
        if (!IsCancelRequested)
        {
            CancelStatusText = string.Empty;
            return;
        }

        CancelStatusText = _runningPhase is { } phase && !CloneExecution.IsInterruptible(phase)
            ? Loc.Get("Progress_Cancelling_SafeStep")
            : Loc.Get("Progress_Cancelling");
    }

    private void Apply(ExecutionEvent value)
    {
        switch (value)
        {
            case PhaseStarted started:
                var item = FindOrAddPhase(started.Phase);
                item.State = PhaseState.InProgress;
                CurrentPhaseText = Loc.Format("Progress_CurrentPhase", item.Name);
                _runningPhase = started.Phase;
                RefreshCancelStatus();
                break;

            case PhaseCompleted completed:
                FindOrAddPhase(completed.Phase).State = PhaseState.Done;
                if (_runningPhase == completed.Phase)
                {
                    _runningPhase = null;
                }

                // Once the structural verification has passed, the clone is valid: a cancel from here on is ignored.
                if (completed.Phase == ExecutionPhase.StructuralVerification)
                {
                    IsCancelAvailable = false;
                }

                RefreshCancelStatus();
                break;

            case ProgressReported reported:
                ApplyProgress(reported.Progress);
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
            (progress.ThroughputBytesPerSecond / BytesPerMebibyte).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture));
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
