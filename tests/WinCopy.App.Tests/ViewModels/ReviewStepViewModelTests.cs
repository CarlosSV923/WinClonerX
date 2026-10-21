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
    public async Task VerifyAllDataIsOffByDefaultAndTheDraftPlanIsStructural()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan((target, mode) => AppFixtures.PlanFor(inventory, target, mode: mode)));

        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.VerifyAllData.ShouldBeFalse();
        vm.Plan!.Verification.ShouldBe(VerificationMode.Structural);
    }

    [Fact]
    public async Task ChangingVerifyAllDataRebuildsThePlanWithTheFullModeAndItsDuration()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var builder = new FakeBuildClonePlan((target, mode) => AppFixtures.PlanFor(inventory, target, mode: mode));
        var vm = new ReviewStepViewModel(builder);
        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);
        var structural = vm.Plan!;

        vm.VerifyAllData = true;

        builder.CallCount.ShouldBe(2);
        builder.LastMode.ShouldBe(VerificationMode.StructuralAndFull);
        vm.Plan!.Verification.ShouldBe(VerificationMode.StructuralAndFull);
        vm.Plan.Estimate.EstimatedDuration.ShouldBe(structural.Estimate.EstimatedDuration + structural.Estimate.EstimatedDuration);
        vm.Summary!.EstimatedDuration.ShouldBe(Loc.Format("Review_Summary_Duration", DurationText.Format(vm.Plan.Estimate.EstimatedDuration)));

        vm.VerifyAllData = false;

        builder.LastMode.ShouldBe(VerificationMode.Structural);
        vm.Plan!.Verification.ShouldBe(VerificationMode.Structural);
        vm.Plan.Estimate.EstimatedDuration.ShouldBe(structural.Estimate.EstimatedDuration);
    }

    [Fact]
    public async Task AFailingVerificationRebuildShowsTheGenericError()
    {
        using var _ = new EnglishCulture();
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var builder = new FakeBuildClonePlan((target, mode) =>
        {
            if (mode == VerificationMode.StructuralAndFull)
            {
                throw new InvalidOperationException("Plan build failed.");
            }

            return AppFixtures.PlanFor(inventory, target, mode: mode);
        });
        var vm = new ReviewStepViewModel(builder);
        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.VerifyAllData = true;
        await WaitForAsync(() => !vm.IsLoading);

        vm.IsFailed.ShouldBeTrue();
        vm.IsValid.ShouldBeFalse();
        vm.BlockingReasons.ShouldContain(Loc.Get("Review_Failed_Generic"));
    }

    [Fact]
    public async Task TwoQuickVerificationTogglesEndWithTheLastModeAndKeepNextDisabledWhileBuilding()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var fullBuildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFullBuild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = new FakeBuildClonePlan(async (target, mode, _) =>
        {
            if (mode == VerificationMode.StructuralAndFull)
            {
                fullBuildStarted.SetResult();
                await releaseFullBuild.Task;
            }

            return AppFixtures.PlanFor(inventory, target, mode: mode);
        });
        var vm = new ReviewStepViewModel(builder);
        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.VerifyAllData = true;
        await fullBuildStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // While the full-verification plan is being built, the step cannot go on.
        vm.IsLoading.ShouldBeTrue();
        vm.IsValid.ShouldBeFalse();

        // The newest build wins: the structural plan is applied at once, and the older build is ignored when it ends.
        vm.VerifyAllData = false;
        await WaitForAsync(() => !vm.IsLoading);
        vm.Plan!.Verification.ShouldBe(VerificationMode.Structural);

        releaseFullBuild.SetResult();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        builder.CallCount.ShouldBe(3);
        vm.IsLoading.ShouldBeFalse();
        vm.Plan!.Verification.ShouldBe(VerificationMode.Structural);
    }

    [Fact]
    public async Task AcceptedWarningsStayAcceptedWhenTheVerificationChanges()
    {
        var inventory = AppFixtures.InventoryOf(
            AppFixtures.SystemDisk(windowsEncryption: EncryptionState.BitLockerOn),
            AppFixtures.TargetDisk());
        var vm = new ReviewStepViewModel(new FakeBuildClonePlan((target, mode) => AppFixtures.PlanFor(inventory, target, mode: mode)));
        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);
        vm.Warnings.Count.ShouldBeGreaterThan(0);
        foreach (var warning in vm.Warnings)
        {
            warning.IsAccepted = true;
        }

        vm.VerifyAllData = true;

        vm.Warnings.ShouldAllBe(warning => warning.IsAccepted);
        vm.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void ChangingVerifyAllDataBuildsNothingWhenNoPlanWasAskedFor()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var builder = new FakeBuildClonePlan((target, mode) => AppFixtures.PlanFor(inventory, target, mode: mode));
        var vm = new ReviewStepViewModel(builder);

        vm.VerifyAllData = true;

        builder.CallCount.ShouldBe(0);
        vm.HasPlan.ShouldBeFalse();
    }

    [Fact]
    public async Task ClearForgetsTheTargetSoAChangeOfVerificationBuildsNothing()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var builder = new FakeBuildClonePlan((target, mode) => AppFixtures.PlanFor(inventory, target, mode: mode));
        var vm = new ReviewStepViewModel(builder);
        await vm.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);

        vm.Clear();
        vm.VerifyAllData = true;

        builder.CallCount.ShouldBe(1);
        vm.HasPlan.ShouldBeFalse();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
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
