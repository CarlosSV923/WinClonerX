using Shouldly;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Domain.Inventory;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

public sealed class WizardStepWiringTests
{
    [Fact]
    public async Task NextOnDisksIsEnabledOnlyAfterAnEligibleSelection()
    {
        var (wizard, disks, _) = Create(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()));
        await disks.LoadAsync(TestContext.Current.CancellationToken);

        wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Disks);
        wizard.IsCurrentStepValid.ShouldBeFalse();
        wizard.NextCommand.CanExecute(null).ShouldBeFalse();

        disks.SelectTargetCommand.Execute(disks.Targets.Single());

        wizard.IsCurrentStepValid.ShouldBeTrue();
        wizard.NextCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task NextFromDisksBuildsThePlanAndShowsTheReviewPage()
    {
        var (wizard, disks, review) = Create(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()));
        await disks.LoadAsync(TestContext.Current.CancellationToken);
        disks.SelectTargetCommand.Execute(disks.Targets.Single());

        await wizard.NextCommand.ExecuteAsync(null);

        wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Review);
        wizard.CurrentPage.ShouldBeSameAs(review);
        review.HasPlan.ShouldBeTrue();
        review.IsValid.ShouldBeTrue();
        wizard.IsCurrentStepValid.ShouldBeTrue();
    }

    [Fact]
    public async Task NextOnReviewWaitsForTheWarningsToBeAccepted()
    {
        var inventory = AppFixtures.InventoryOf(
            AppFixtures.SystemDisk(windowsEncryption: EncryptionState.BitLockerOn),
            AppFixtures.TargetDisk());
        var (wizard, disks, review) = Create(inventory);
        await disks.LoadAsync(TestContext.Current.CancellationToken);
        disks.SelectTargetCommand.Execute(disks.Targets.Single());
        await wizard.NextCommand.ExecuteAsync(null);

        wizard.IsCurrentStepValid.ShouldBeFalse();
        wizard.NextCommand.CanExecute(null).ShouldBeFalse();

        review.Warnings.Single().IsAccepted = true;

        wizard.IsCurrentStepValid.ShouldBeTrue();
        wizard.NextCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task GoingBackToDisksKeepsTheSelectionAndRebuildsThePlanOnNext()
    {
        var builder = new FakeBuildClonePlan(target => AppFixtures.PlanFor(
            AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()), target));
        var inventory = new FakeGetInventoryAndEligibility(
            AppFixtures.Overview(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk())));
        var disks = new DisksStepViewModel(inventory);
        var review = new ReviewStepViewModel(builder);
        var wizard = new WizardViewModel(disks, review);
        await disks.LoadAsync(TestContext.Current.CancellationToken);
        disks.SelectTargetCommand.Execute(disks.Targets.Single());
        await wizard.NextCommand.ExecuteAsync(null);

        wizard.BackCommand.Execute(null);

        wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Disks);
        disks.IsValid.ShouldBeTrue();
        wizard.IsCurrentStepValid.ShouldBeTrue();

        await wizard.NextCommand.ExecuteAsync(null);

        builder.CallCount.ShouldBe(2);
        review.HasPlan.ShouldBeTrue();
    }

    [Fact]
    public void ShellWithoutPagesKeepsTheOriginalBehavior()
    {
        var wizard = new WizardViewModel();

        wizard.DisksStep.ShouldBeNull();
        wizard.ReviewStep.ShouldBeNull();
        wizard.CurrentPage.ShouldBeSameAs(wizard.CurrentStep);
    }

    private static (WizardViewModel Wizard, DisksStepViewModel Disks, ReviewStepViewModel Review) Create(DiskInventory inventory)
    {
        var disks = new DisksStepViewModel(new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory)));
        var review = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));
        return (new WizardViewModel(disks, review), disks, review);
    }
}
