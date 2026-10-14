using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;

namespace WinCopy.App.ViewModels;

/// <summary>
/// One candidate destination disk: its card plus its eligibility, the localized reasons, what is missing and the
/// strategy that would be used. Only eligible items can be selected.
/// </summary>
public sealed class TargetDiskItemViewModel : DiskCardViewModel
{
    public TargetDiskItemViewModel(Disk disk, TargetEligibility eligibility, bool globalBlocked)
        : base(disk)
    {
        ArgumentNullException.ThrowIfNull(eligibility);

        IsEligible = eligibility.IsEligible && !globalBlocked;
        IsSelectable = IsEligible;
        IsDisabled = !IsEligible;
        StatusText = Loc.Get(IsEligible ? "Disks_Status_Eligible" : "Disks_Status_NotEligible");

        var reasons = eligibility.Reasons.Select(PreflightText.Reason).ToList();
        if (!IsEligible && reasons.Count == 0)
        {
            reasons.Add(Loc.Get("Disks_Reason_GlobalBlocked"));
        }

        Reasons = reasons;
        StrategyText = IsEligible ? StrategyFor(eligibility.Strategy) : null;
        ShortfallText = eligibility.Shortfall is { } missing
            ? Loc.Format("Disks_Shortfall", SizeText.Format(missing))
            : null;

        var help = new List<string> { StatusText };
        help.AddRange(Reasons);
        if (StrategyText is not null)
        {
            help.Add(StrategyText);
        }

        if (IsSelectable)
        {
            help.Add(Loc.Get("Disks_Select_Hint"));
        }

        HelpText = string.Join(" ", help);
    }

    private static string? StrategyFor(SizeStrategy? strategy) => strategy switch
    {
        SizeStrategy.SameOrLarger => Loc.Get("Disks_Strategy_Extend"),
        SizeStrategy.ShrinkSource => Loc.Get("Disks_Strategy_Shrink"),
        _ => null,
    };
}
