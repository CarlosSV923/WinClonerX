using Shouldly;
using WinClonerX.App.Localization;
using WinClonerX.App.Presentation;
using WinClonerX.App.Tests.Fakes;
using WinClonerX.App.ViewModels;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;

namespace WinClonerX.App.Tests.ViewModels;

public sealed class DisksStepViewModelTests
{
    [Fact]
    public async Task SystemDiskIsNeverATargetEvenWhenTheEvaluatorReturnsIt()
    {
        var source = AppFixtures.SystemDisk();
        var inventory = AppFixtures.InventoryOf(source, AppFixtures.TargetDisk());
        var overview = AppFixtures.Overview(inventory);
        var forged = overview with
        {
            Targets =
            [
                new TargetEligibility(AppFixtures.SourceId, true, [], SizeStrategy.SameOrLarger, null),
                .. overview.Targets,
            ],
        };
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(forged));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.Targets.Select(target => target.DiskId).ShouldBe([AppFixtures.TargetId]);
        vm.SourceDisk!.DiskId.ShouldBe(AppFixtures.SourceId);
    }

    [Fact]
    public async Task SourceDiskIsShownButCannotBeSelected()
    {
        using var _ = new EnglishCulture();
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(
            AppFixtures.Overview(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()))));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.HasSourceDisk.ShouldBeTrue();
        vm.SourceDisk!.IsSelectable.ShouldBeFalse();
        vm.SourceDisk.StatusText.ShouldBeNull();
    }

    [Fact]
    public async Task NotEligibleTargetCannotBeSelectedAndShowsItsLocalizedReason()
    {
        using var _ = new EnglishCulture();
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(
            AppFixtures.Overview(inventory, AppFixtures.Facts(targetInUse: true))));

        await vm.LoadAsync(TestContext.Current.CancellationToken);
        var item = vm.Targets.Single();
        vm.SelectTargetCommand.CanExecute(item).ShouldBeFalse();
        vm.SelectedTarget = item;

        item.IsEligible.ShouldBeFalse();
        item.IsSelectable.ShouldBeFalse();
        item.IsDisabled.ShouldBeTrue();
        vm.SelectedTarget.ShouldBeNull();
        vm.IsValid.ShouldBeFalse();
        item.StatusText.ShouldBe(Loc.Get("Disks_Status_NotEligible"));
        item.Reasons.Single().ShouldContain(Loc.Get("Preflight_TargetInUse"));
    }

    [Fact]
    public async Task IsValidOnlyWithAnEligibleSelection()
    {
        using var _ = new EnglishCulture();
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(
            AppFixtures.Overview(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()))));
        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.IsValid.ShouldBeFalse();

        vm.SelectTargetCommand.Execute(vm.Targets.Single());

        vm.IsValid.ShouldBeTrue();
        vm.SelectedTarget.ShouldBeSameAs(vm.Targets.Single());
        vm.Targets.Single().IsSelected.ShouldBeTrue();
        vm.Targets.Single().StrategyText.ShouldBe(Loc.Get("Disks_Strategy_Extend"));

        vm.SelectedTarget = null;

        vm.IsValid.ShouldBeFalse();
        vm.Targets.Single().IsSelected.ShouldBeFalse();
    }

    [Fact]
    public async Task TooSmallTargetShowsWhatIsMissing()
    {
        using var _ = new EnglishCulture();
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk(size: 100 * AppFixtures.Gib));
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory)));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        var item = vm.Targets.Single();
        item.IsEligible.ShouldBeFalse();
        item.ShortfallText.ShouldNotBeNull();
        item.ShortfallText.ShouldStartWith("Missing ");
        item.StrategyText.ShouldBeNull();
    }

    [Fact]
    public async Task GlobalBlockReplacesTheListWithReasons()
    {
        using var _ = new EnglishCulture();
        var bios = new DiskInventory(AppFixtures.CapturedAt, FirmwareType.Bios,
            [AppFixtures.SystemDisk(), AppFixtures.TargetDisk()]);
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(AppFixtures.Overview(bios)));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.GlobalBlocked.ShouldBeTrue();
        vm.GlobalBlockReasons.ShouldContain(reason => reason.Contains(Loc.Get("Preflight_FirmwareNotUefi")));
        vm.IsGlobalPanelVisible.ShouldBeTrue();
        vm.IsListVisible.ShouldBeFalse();
        vm.IsEmptyPanelVisible.ShouldBeFalse();
        vm.SelectTargetCommand.CanExecute(vm.Targets.FirstOrDefault()).ShouldBeFalse();
        vm.Targets.All(target => !target.IsSelectable).ShouldBeTrue();
    }

    [Fact]
    public async Task UnsupportedSystemHasNoSourceCardAndNoTargets()
    {
        var first = AppFixtures.SystemDisk();
        var second = AppFixtures.SystemDisk() with { Id = new DiskId(5, AppFixtures.G(555)) };
        var inventory = AppFixtures.InventoryOf(first, second);
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory)));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.GlobalBlocked.ShouldBeTrue();
        vm.HasSourceDisk.ShouldBeFalse();
        vm.Targets.ShouldBeEmpty();
    }

    [Fact]
    public async Task EmptyStateExplainsTheMinimumCapacity()
    {
        using var _ = new EnglishCulture();
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(
            AppFixtures.Overview(AppFixtures.InventoryOf(AppFixtures.SystemDisk()))));

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.IsEmpty.ShouldBeTrue();
        vm.IsEmptyPanelVisible.ShouldBeTrue();
        vm.IsListVisible.ShouldBeFalse();
        vm.MinimumCapacity.Bytes.ShouldBeGreaterThan(AppFixtures.Gib * 40);
        vm.EmptyStateBody.ShouldContain(SizeText.Format(vm.MinimumCapacity));
    }

    [Fact]
    public async Task NothingIsShownBeforeTheFirstRead()
    {
        var vm = new DisksStepViewModel(new FakeGetInventoryAndEligibility(
            AppFixtures.Overview(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()))));

        vm.IsLoaded.ShouldBeFalse();
        vm.IsListVisible.ShouldBeFalse();
        vm.IsEmptyPanelVisible.ShouldBeFalse();
        vm.IsGlobalPanelVisible.ShouldBeFalse();

        await vm.LoadAsync(TestContext.Current.CancellationToken);

        vm.IsLoaded.ShouldBeTrue();
        vm.IsListVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task RefreshReloadsAndDropsThePreviousSelection()
    {
        var first = AppFixtures.Overview(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()));
        var second = AppFixtures.Overview(AppFixtures.InventoryOf(
            AppFixtures.SystemDisk(),
            AppFixtures.TargetDisk(),
            AppFixtures.TargetDisk(size: 300 * AppFixtures.Gib, serial: "NEW", id: AppFixtures.OtherTargetId)));
        var inventory = new FakeGetInventoryAndEligibility(first, second);
        var vm = new DisksStepViewModel(inventory);
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        vm.SelectTargetCommand.Execute(vm.Targets.Single());

        await vm.RefreshCommand.ExecuteAsync(null);

        inventory.CallCount.ShouldBe(2);
        vm.Targets.Count.ShouldBe(2);
        vm.IsValid.ShouldBeFalse();
        vm.SelectedTarget.ShouldBeNull();
    }

    [Fact]
    public async Task LoadCommandReadsTheInventoryOnce()
    {
        var inventory = new FakeGetInventoryAndEligibility(
            AppFixtures.Overview(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk())));
        var vm = new DisksStepViewModel(inventory);

        await vm.LoadCommand.ExecuteAsync(null);

        inventory.CallCount.ShouldBe(1);
        vm.IsLoading.ShouldBeFalse();
        vm.Targets.Count.ShouldBe(1);
    }
}
