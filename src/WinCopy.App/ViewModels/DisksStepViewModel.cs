using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.Application.UseCases.Planning;
using WinCopy.Application.UseCases.Recovery;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Step 1 "Disks" (contracts/ui-wizard.md): the source disk, the list of candidate destinations with their eligibility,
/// the global block panel, the empty state and the selection of one eligible destination.
/// </summary>
public sealed partial class DisksStepViewModel : ObservableObject
{
    /// <summary>Bytes reserved by GPT structures and by the 1 MiB alignment of each partition, on top of the partitions.</summary>
    private const long AlignmentSlackPerPartition = 1L << 20;
    private const long FirstUsableBytes = 1L << 20;
    private const long BackupGptBytes = 16L * 1024;

    private readonly IGetInventoryAndEligibility _inventory;
    private readonly IRecoverPendingSourceRestore? _recovery;
    private TargetDiskItemViewModel? _selectedTarget;
    private bool _recoveryChecked;

    /// <param name="inventory">Reads the disks and evaluates the destinations.</param>
    /// <param name="recovery">
    /// Startup recovery of a source left shrunk by an interrupted clone. Null skips the check; the composition root always passes it.
    /// </param>
    public DisksStepViewModel(IGetInventoryAndEligibility inventory, IRecoverPendingSourceRestore? recovery = null)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _recovery = recovery;
    }

    /// <summary>
    /// Raised once per application start when the startup recovery found a pending source restore that was restored or still
    /// needs attention. Not raised when there was nothing to recover.
    /// </summary>
    public event EventHandler<PendingSourceRestoreReportedEventArgs>? PendingSourceRestoreReported;

    /// <summary>The system disk, shown as the source card. Null when the system is not supported.</summary>
    [ObservableProperty]
    private DiskCardViewModel? _sourceDisk;

    /// <summary>Candidate destinations, never including the system disk.</summary>
    public ObservableCollection<TargetDiskItemViewModel> Targets { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True once a read has finished, successfully or not. Nothing is shown before the first read.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>True when the global report blocks cloning (for example, the system is not UEFI or is not supported).</summary>
    [ObservableProperty]
    private bool _globalBlocked;

    /// <summary>Localized reasons for the global block, shown in place of the destination list.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _globalBlockReasons = [];

    /// <summary>True when there are no destination candidates at all.</summary>
    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Rough minimum capacity a destination needs to receive the system disk (see <see cref="EstimateMinimumCapacity"/>).</summary>
    [ObservableProperty]
    private ByteSize _minimumCapacity;

    /// <summary>Empty-state text: the minimum capacity and how to connect a disk.</summary>
    [ObservableProperty]
    private string _emptyStateBody = string.Empty;

    /// <summary>The selected destination. Only eligible items can be selected; other values are ignored.</summary>
    public TargetDiskItemViewModel? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (value is not null && (!value.IsSelectable || !Targets.Contains(value)))
            {
                return;
            }

            if (ReferenceEquals(_selectedTarget, value))
            {
                return;
            }

            if (_selectedTarget is not null)
            {
                _selectedTarget.IsSelected = false;
            }

            _selectedTarget = value;
            if (value is not null)
            {
                value.IsSelected = true;
            }

            OnPropertyChanged(nameof(SelectedTarget));
            OnPropertyChanged(nameof(IsValid));
        }
    }

    /// <summary>
    /// Whether the step is complete: a destination has been selected and no read is in progress. While the disks are being
    /// read (including the startup recovery) the person cannot go on.
    /// </summary>
    public bool IsValid => !IsLoading && SelectedTarget is not null;

    /// <summary>Show the global block panel instead of the list.</summary>
    public bool IsGlobalPanelVisible => IsLoaded && GlobalBlocked;

    /// <summary>Show the empty state with the refresh button.</summary>
    public bool IsEmptyPanelVisible => IsLoaded && !GlobalBlocked && IsEmpty;

    /// <summary>Show the destination list.</summary>
    public bool IsListVisible => IsLoaded && !GlobalBlocked && !IsEmpty;

    public bool HasSourceDisk => SourceDisk is not null;

    /// <summary>Reads the inventory and evaluates every destination. Keeps no selection from a previous read.</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            // The recovery goes first: the inventory must show the restored partition, not the shrunk one.
            await RecoverPendingSourceRestoreOnceAsync();

            var overview = await _inventory.ExecuteAsync(cancellationToken);
            Apply(overview);
            IsLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Runs the startup recovery once per application start, before the first inventory read. It never throws: an unexpected
    /// failure is reported as needing attention, and the wizard still starts.
    /// </summary>
    private async Task RecoverPendingSourceRestoreOnceAsync()
    {
        if (_recovery is null || _recoveryChecked)
        {
            return;
        }

        _recoveryChecked = true;

        PendingSourceRestoreRecovery recovery;
        try
        {
            recovery = await _recovery.RecoverAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            recovery = PendingSourceRestoreRecovery.NeedsAttention(PendingSourceRestoreIssue.UnexpectedError, ex.ToString());
        }

        if (recovery.Outcome != PendingSourceRestoreOutcome.Nothing)
        {
            PendingSourceRestoreReported?.Invoke(this, new PendingSourceRestoreReportedEventArgs(recovery));
        }
    }

    /// <summary>Reads the inventory again. Used by the refresh buttons.</summary>
    [RelayCommand]
    public Task RefreshAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void SelectTarget(TargetDiskItemViewModel? target)
    {
        SelectedTarget = target;
    }

    private static bool CanSelect(TargetDiskItemViewModel? target) => target is { IsSelectable: true };

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsValid));

    partial void OnIsLoadedChanged(bool value) => NotifyLayout();

    partial void OnGlobalBlockedChanged(bool value) => NotifyLayout();

    partial void OnIsEmptyChanged(bool value) => NotifyLayout();

    partial void OnSourceDiskChanged(DiskCardViewModel? value) => OnPropertyChanged(nameof(HasSourceDisk));

    private void NotifyLayout()
    {
        OnPropertyChanged(nameof(IsGlobalPanelVisible));
        OnPropertyChanged(nameof(IsEmptyPanelVisible));
        OnPropertyChanged(nameof(IsListVisible));
    }

    private void Apply(InventoryOverview overview)
    {
        SelectedTarget = null;

        var inventory = overview.Inventory;
        var source = inventory.SystemDisk;

        GlobalBlocked = overview.Global.IsBlocked;
        GlobalBlockReasons = overview.Global.IsBlocked
            ? overview.Global.Checks
                .Where(check => check.Severity == Severity.Blocking)
                .Select(PreflightText.Reason)
                .ToArray()
            : [];

        SourceDisk = source is null ? null : new DiskCardViewModel(source);
        MinimumCapacity = source is null ? default : EstimateMinimumCapacity(source);

        Targets.Clear();
        foreach (var eligibility in overview.Targets)
        {
            var disk = inventory.FindDisk(eligibility.Disk);

            // The system disk is never a destination, even if an evaluator returns it (defense in depth).
            if (disk is null || disk.IsSystem || (source is not null && disk.Id == source.Id))
            {
                continue;
            }

            Targets.Add(new TargetDiskItemViewModel(disk, eligibility, overview.Global.IsBlocked));
        }

        IsEmpty = Targets.Count == 0;
        EmptyStateBody = Loc.Format("Disks_Empty_Body", SizeText.Format(MinimumCapacity));
    }

    /// <summary>
    /// Approximate smallest disk that can hold the source: the partitions as they are, Windows with its required margin,
    /// one megabyte of alignment per partition and the GPT structures. The evaluator makes the exact decision.
    /// </summary>
    private static ByteSize EstimateMinimumCapacity(Disk source)
    {
        var policy = CapacityPolicy.Default;
        var total = FirstUsableBytes + BackupGptBytes + source.LogicalSectorSize.Bytes;

        foreach (var partition in source.Partitions)
        {
            total += AlignmentSlackPerPartition;
            total += partition.Role == PartitionRole.Windows
                ? policy.Required(partition.Volume?.Used ?? default).Bytes
                : partition.Extent.Length;
        }

        return new ByteSize(total);
    }
}

/// <summary>Arguments of <see cref="DisksStepViewModel.PendingSourceRestoreReported"/>: the result of the startup recovery.</summary>
public sealed class PendingSourceRestoreReportedEventArgs(PendingSourceRestoreRecovery recovery) : EventArgs
{
    /// <summary>The recovery result. Its outcome is never Nothing when the event is raised.</summary>
    public PendingSourceRestoreRecovery Recovery { get; } = recovery;
}
