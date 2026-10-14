using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Step 2 "Review" (contracts/ui-wizard.md): builds the plan for the selected destination and shows the before and after
/// partition bars, the summary, the warnings that must be accepted, and the (not yet available) full verification option.
/// </summary>
public sealed partial class ReviewStepViewModel : ObservableObject
{
    private readonly IBuildClonePlan _buildPlan;

    public ReviewStepViewModel(IBuildClonePlan buildPlan)
    {
        _buildPlan = buildPlan ?? throw new ArgumentNullException(nameof(buildPlan));
    }

    /// <summary>The draft plan, when it could be built.</summary>
    [ObservableProperty]
    private ClonePlan? _plan;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True when the plan could not be built; <see cref="BlockingReasons"/> explains why.</summary>
    [ObservableProperty]
    private bool _isFailed;

    /// <summary>Whether the step is complete: a plan exists and every warning is accepted.</summary>
    [ObservableProperty]
    private bool _isValid;

    /// <summary>Source partitions, drawn on the same scale as <see cref="AfterBar"/>.</summary>
    [ObservableProperty]
    private PartitionBarData? _beforeBar;

    /// <summary>Destination partitions from the plan mappings, drawn on the same scale as <see cref="BeforeBar"/>.</summary>
    [ObservableProperty]
    private PartitionBarData? _afterBar;

    [ObservableProperty]
    private PlanSummary? _summary;

    /// <summary>True when the plan reduces Windows on the source temporarily (FR-021). Shows the shrink information block.</summary>
    [ObservableProperty]
    private bool _hasSourceShrink;

    /// <summary>Localized sentence with the size of the source Windows partition before and during the copy.</summary>
    [ObservableProperty]
    private string _sourceShrinkText = string.Empty;

    /// <summary>Warnings of the plan, one per warning, each with its own "I understand" checkbox.</summary>
    public ObservableCollection<WarningItemViewModel> Warnings { get; } = [];

    /// <summary>Size change per partition, shown under the after bar.</summary>
    public ObservableCollection<PartitionChange> PartitionChanges { get; } = [];

    /// <summary>Reasons the plan could not be built. Empty unless <see cref="IsFailed"/>.</summary>
    public ObservableCollection<string> BlockingReasons { get; } = [];

    /// <summary>Full data verification (FR-022a). Off by default and always off in this step.</summary>
    [ObservableProperty]
    private bool _verifyAllData;

    /// <summary>
    /// Whether the full verification checkbox can be changed. It stays false until T108 connects the option; the checkbox
    /// is visible but disabled, with a "coming soon" tooltip.
    /// </summary>
    [ObservableProperty]
    private bool _isVerifyAllDataEnabled;

    public bool HasPlan => Plan is not null;

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Codes of the warnings the person has accepted, for step 3.</summary>
    public IReadOnlySet<PreflightCode> AcceptedWarnings =>
        Warnings.Where(warning => warning.IsAccepted).Select(warning => warning.Code).ToHashSet();

    /// <summary>Builds the plan for <paramref name="target"/> with structural verification. Resets the step first.</summary>
    public async Task LoadAsync(DiskId target, CancellationToken cancellationToken = default)
    {
        Clear();
        IsLoading = true;
        try
        {
            var result = await _buildPlan.ExecuteAsync(target, VerificationMode.Structural, cancellationToken);
            if (result.IsSuccess)
            {
                ApplyPlan(result.Value);
            }
            else
            {
                ApplyFailure(result.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Forgets the plan and the warnings, so the step is not valid until a new plan is built.</summary>
    public void Clear()
    {
        Plan = null;
        IsFailed = false;
        BeforeBar = null;
        AfterBar = null;
        Summary = null;
        HasSourceShrink = false;
        SourceShrinkText = string.Empty;

        foreach (var warning in Warnings)
        {
            warning.PropertyChanged -= OnWarningChanged;
        }

        Warnings.Clear();
        PartitionChanges.Clear();
        BlockingReasons.Clear();
        UpdateValidity();
    }

    partial void OnPlanChanged(ClonePlan? value) => OnPropertyChanged(nameof(HasPlan));

    private void ApplyPlan(ClonePlan plan)
    {
        Plan = plan;

        foreach (var warning in plan.Warnings)
        {
            var item = new WarningItemViewModel(warning.Code, PreflightText.Message(warning));
            item.PropertyChanged += OnWarningChanged;
            Warnings.Add(item);
        }

        var sourceSegments = plan.Mappings
            .Select(mapping => new PartitionSegment(
                mapping.Source.Role,
                new ByteSize(mapping.Source.Extent.Length),
                mapping.Source.Volume?.Used,
                Labels.Role(mapping.Source.Role)))
            .ToArray();

        var targetSegments = plan.Mappings
            .Select(mapping => new PartitionSegment(
                mapping.Source.Role,
                new ByteSize(mapping.TargetExtent.Length),
                mapping.Source.Volume?.Used,
                Labels.Role(mapping.Source.Role)))
            .ToArray();

        // Both bars share one scale, so the destination partitions line up with the source ones.
        var scale = new ByteSize(Math.Max(
            plan.Mappings.Select(mapping => mapping.Source.Extent.End).DefaultIfEmpty(0).Max(),
            plan.Mappings.Select(mapping => mapping.TargetExtent.End).DefaultIfEmpty(0).Max()));

        BeforeBar = new PartitionBarData(sourceSegments, scale);
        AfterBar = new PartitionBarData(targetSegments, scale);

        foreach (var mapping in plan.Mappings)
        {
            var delta = mapping.TargetExtent.Length - mapping.Source.Extent.Length;
            var label = Labels.Role(mapping.Source.Role);
            var change = delta switch
            {
                > 0 => Loc.Format("Review_Change_Grow", SizeText.Format(new ByteSize(delta))),
                < 0 => Loc.Format("Review_Change_Shrink", SizeText.Format(new ByteSize(-delta))),
                _ => Loc.Get("Review_Change_None"),
            };

            PartitionChanges.Add(new PartitionChange(label, delta, Loc.Format("Review_Change_Line", label, change)));
        }

        Summary = new PlanSummary(
            Loc.Format("Review_Summary_Copy", SizeText.Format(plan.Estimate.BytesToCopy)),
            Loc.Format("Review_Summary_Duration", DurationText.Format(plan.Estimate.EstimatedDuration)));

        if (plan.SourceShrink is { } shrink)
        {
            HasSourceShrink = true;
            SourceShrinkText = Loc.Format(
                "Shrink_Review_Body",
                SizeText.Format(shrink.OriginalLength),
                SizeText.Format(shrink.ShrunkLength));
        }

        UpdateValidity();
    }

    private void ApplyFailure(PreflightReport report)
    {
        IsFailed = true;

        var reasons = report.Checks
            .Where(check => check.Severity == Severity.Blocking)
            .Select(PreflightText.Reason)
            .ToArray();

        foreach (var reason in reasons.Length > 0 ? reasons : [Loc.Get("Review_Failed_Generic")])
        {
            BlockingReasons.Add(reason);
        }

        UpdateValidity();
    }

    private void OnWarningChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WarningItemViewModel.IsAccepted))
        {
            UpdateValidity();
        }
    }

    private void UpdateValidity()
    {
        IsValid = Plan is not null && Warnings.All(warning => warning.IsAccepted);
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(AcceptedWarnings));
    }
}
