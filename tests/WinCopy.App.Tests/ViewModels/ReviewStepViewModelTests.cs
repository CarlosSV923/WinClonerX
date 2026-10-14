using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

public sealed class ReviewStepViewModelTests
{
    [Fact]
    public async Task NextIsDisabledUntilEveryWarningIsAccepted()
    {
        using var _ = new EnglishCulture();
        var inventory = AppFixtures.InventoryOf(
            AppFixtures.SystemDisk(windowsEncryption: EncryptionState.BitLockerOn),
            AppFixtures.TargetDisk());
        var builder = new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target, AppFixtures.Facts(onBattery: true)));
        var vm = new ReviewStepViewModel(builder);

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.Warnings.Count.ShouldBe(2);
        vm.IsValid.ShouldBeFalse();

        vm.Warnings[0].IsAccepted = true;
        vm.IsValid.ShouldBeFalse();

        vm.Warnings[1].IsAccepted = true;
        vm.IsValid.ShouldBeTrue();

        vm.Warnings[1].IsAccepted = false;
        vm.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ThePlanIsValidWhenItHasNoWarnings()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.HasPlan.ShouldBeTrue();
        vm.Warnings.ShouldBeEmpty();
        vm.IsValid.ShouldBeTrue();
        vm.IsFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task AcceptedWarningsListsTheCodesTheUserAccepted()
    {
        var inventory = AppFixtures.InventoryOf(
            AppFixtures.SystemDisk(windowsEncryption: EncryptionState.BitLockerOn),
            AppFixtures.TargetDisk());
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));
        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.AcceptedWarnings.ShouldBeEmpty();

        vm.Warnings.Single().IsAccepted = true;

        vm.AcceptedWarnings.ShouldBe(new HashSet<PreflightCode> { PreflightCode.BitLockerOn });
    }

    [Fact]
    public async Task BarsReflectTheSourceAndTheMappings()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var plan = AppFixtures.PlanFor(inventory, AppFixtures.TargetId).Value;
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan(_ => AppFixtures.PlanFor(inventory, AppFixtures.TargetId)));

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.BeforeBar.ShouldNotBeNull();
        vm.AfterBar.ShouldNotBeNull();
        vm.BeforeBar.Segments.Select(segment => segment.Size.Bytes)
            .ShouldBe(plan.Mappings.Select(mapping => mapping.Source.Extent.Length).ToArray());
        vm.AfterBar.Segments.Select(segment => segment.Size.Bytes)
            .ShouldBe(plan.Mappings.Select(mapping => mapping.TargetExtent.Length).ToArray());
        vm.BeforeBar.Segments.Select(segment => segment.Role)
            .ShouldBe(plan.Mappings.Select(mapping => mapping.Source.Role).ToArray());

        var largestEnd = plan.Mappings.Max(mapping => mapping.TargetExtent.End);
        vm.AfterBar.DiskSize.Bytes.ShouldBeGreaterThanOrEqualTo(largestEnd);
        vm.BeforeBar.DiskSize.ShouldBe(vm.AfterBar.DiskSize);
    }

    [Fact]
    public async Task SizeChangesShowTheGrownWindowsPartitionAndNoChangeForTheRest()
    {
        using var _ = new EnglishCulture();
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan(_ => AppFixtures.PlanFor(inventory, AppFixtures.TargetId)));

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        var windows = vm.PartitionChanges.Single(change => change.Label == Loc.Get("Partition_Role_Windows"));
        windows.DeltaBytes.ShouldBeGreaterThan(0);
        windows.Text.ShouldStartWith(Loc.Get("Partition_Role_Windows") + ": +");

        vm.PartitionChanges.Where(change => change != windows)
            .ShouldAllBe(change => change.DeltaBytes == 0 && change.Text.EndsWith(Loc.Get("Review_Change_None")));
    }

    [Fact]
    public async Task SummaryFormatsTheBytesToCopyAndTheDuration()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var plan = AppFixtures.PlanFor(inventory, AppFixtures.TargetId).Value;
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan(_ => AppFixtures.PlanFor(inventory, AppFixtures.TargetId)));

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.Summary.ShouldNotBeNull();
        vm.Summary.DataToCopy.ShouldContain(SizeText.Format(plan.Estimate.BytesToCopy));
        vm.Summary.EstimatedDuration.ShouldContain(DurationText.Format(plan.Estimate.EstimatedDuration));
    }

    [Fact]
    public async Task AFailedPlanShowsTheBlockingReasonsAndIsNotValid()
    {
        using var _ = new EnglishCulture();
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan(
            target => AppFixtures.PlanFor(inventory, target, AppFixtures.Facts(targetInUse: true))));

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.IsFailed.ShouldBeTrue();
        vm.IsValid.ShouldBeFalse();
        vm.HasPlan.ShouldBeFalse();
        vm.Plan.ShouldBeNull();
        vm.BeforeBar.ShouldBeNull();
        vm.Warnings.ShouldBeEmpty();
        vm.BlockingReasons.ShouldContain(reason => reason.Contains(Loc.Get("Preflight_TargetInUse")));
    }

    [Fact]
    public async Task LoadBuildsTheStructuralPlanForTheSelectedTarget()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var builder = new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target));
        var vm = new ReviewStepViewModel(builder);

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        builder.CallCount.ShouldBe(1);
        builder.LastTarget.ShouldBe(AppFixtures.TargetId);
        builder.LastMode.ShouldBe(VerificationMode.Structural);
        vm.Plan!.Verification.ShouldBe(VerificationMode.Structural);
    }

    [Fact]
    public async Task VerifyAllDataIsOffAndDisabled()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.VerifyAllData.ShouldBeFalse();
        vm.IsVerifyAllDataEnabled.ShouldBeFalse();
        vm.Plan!.Verification.ShouldBe(VerificationMode.Structural);
    }

    [Fact]
    public async Task ClearForgetsThePlanAndTheWarnings()
    {
        var inventory = AppFixtures.InventoryOf(
            AppFixtures.SystemDisk(windowsEncryption: EncryptionState.BitLockerOn),
            AppFixtures.TargetDisk());
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));
        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);
        vm.Warnings.Single().IsAccepted = true;
        vm.IsValid.ShouldBeTrue();

        vm.Clear();

        vm.HasPlan.ShouldBeFalse();
        vm.Warnings.ShouldBeEmpty();
        vm.PartitionChanges.ShouldBeEmpty();
        vm.IsValid.ShouldBeFalse();
    }
}
