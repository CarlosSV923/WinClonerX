using Shouldly;
using WinClonerX.App.Localization;
using WinClonerX.App.Tests.Fakes;
using WinClonerX.App.ViewModels;
using WinClonerX.Application.UseCases.Recovery;
using Xunit;

namespace WinClonerX.App.Tests.ViewModels;

/// <summary>
/// Arranque (FR-021a): la restauración pendiente del origen se comprueba una vez, antes de leer el inventario. Su resultado se
/// muestra como aviso en el paso 1, y mientras corre el paso no deja avanzar.
/// </summary>
public sealed class StartupSourceRestoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARestoredSourceShowsTheRestoredNoticeOnTheDisksStep()
    {
        using var _ = new EnglishCulture();
        var recovery = new FakeRecoverPendingSourceRestore { Result = PendingSourceRestoreRecovery.Restored };
        var (wizard, disks) = Create(recovery);

        await disks.LoadAsync(Ct);

        wizard.Notice.ShouldBe(Loc.Get("Startup_SourceRestored"));
        wizard.Notice.ShouldNotStartWith("[[");
        wizard.HasNotice.ShouldBeTrue();
        wizard.IsNoticeSuccess.ShouldBeTrue();
        recovery.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task ASourceThatNeedsAttentionShowsTheManualFixNotice()
    {
        using var _ = new EnglishCulture();
        var recovery = new FakeRecoverPendingSourceRestore
        {
            Result = PendingSourceRestoreRecovery.NeedsAttention(PendingSourceRestoreIssue.RestoreFailed, "vds busy"),
        };
        var (wizard, disks) = Create(recovery);

        await disks.LoadAsync(Ct);

        wizard.Notice.ShouldBe(Loc.Get("Startup_SourceRestoreNeedsAttention"));
        wizard.Notice.ShouldNotStartWith("[[");
        wizard.Notice!.ShouldContain("Extend Volume");
        wizard.IsNoticeSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task ANothingToRecoverLeavesTheNoticeEmpty()
    {
        var recovery = new FakeRecoverPendingSourceRestore { Result = PendingSourceRestoreRecovery.Nothing };
        var (wizard, disks) = Create(recovery);

        await disks.LoadAsync(Ct);

        wizard.Notice.ShouldBeNull();
        wizard.HasNotice.ShouldBeFalse();
    }

    [Fact]
    public async Task AnAlreadyRestoredSourceLeavesTheNoticeEmpty()
    {
        var recovery = new FakeRecoverPendingSourceRestore { Result = PendingSourceRestoreRecovery.AlreadyRestored };
        var (wizard, disks) = Create(recovery);

        await disks.LoadAsync(Ct);

        wizard.Notice.ShouldBeNull();
        wizard.HasNotice.ShouldBeFalse();
    }

    [Fact]
    public async Task TheRecoveryRunsOnceAcrossReloadsOfTheDisksStep()
    {
        var recovery = new FakeRecoverPendingSourceRestore { Result = PendingSourceRestoreRecovery.Restored };
        var (_, disks) = Create(recovery);

        await disks.LoadAsync(Ct);
        await disks.RefreshCommand.ExecuteAsync(null);

        recovery.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task TheRecoveryRunsBeforeTheInventoryIsRead()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var inventoryReads = new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory));
        var readsBeforeRecovery = -1;
        var recovery = new FakeRecoverPendingSourceRestore
        {
            Result = PendingSourceRestoreRecovery.Restored,
            OnCall = () => readsBeforeRecovery = inventoryReads.CallCount,
        };
        var disks = new DisksStepViewModel(inventoryReads, recovery);

        await disks.LoadAsync(Ct);

        readsBeforeRecovery.ShouldBe(0);
        inventoryReads.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task AFailingRecoveryNeverStopsTheDisksFromLoading()
    {
        using var _ = new EnglishCulture();
        var recovery = new FakeRecoverPendingSourceRestore { Failure = new InvalidOperationException("boom") };
        var (wizard, disks) = Create(recovery);

        await disks.LoadAsync(Ct);

        disks.IsLoaded.ShouldBeTrue();
        disks.IsLoading.ShouldBeFalse();
        disks.Targets.Count.ShouldBe(1);
        wizard.Notice.ShouldBe(Loc.Get("Startup_SourceRestoreNeedsAttention"));
    }

    [Fact]
    public async Task TheStepIsNotValidWhileTheStartupRecoveryRuns()
    {
        using var _ = new EnglishCulture();
        var gate = new TaskCompletionSource();
        var recovery = new FakeRecoverPendingSourceRestore { Gate = gate, Result = PendingSourceRestoreRecovery.Restored };
        var (wizard, disks) = Create(recovery);

        var load = disks.LoadAsync(Ct);

        disks.IsLoading.ShouldBeTrue();
        disks.IsValid.ShouldBeFalse();
        wizard.CanGoNext.ShouldBeFalse();

        gate.SetResult();
        await load;

        disks.IsLoading.ShouldBeFalse();
        disks.IsLoaded.ShouldBeTrue();
    }

    private static (WizardViewModel Wizard, DisksStepViewModel Disks) Create(FakeRecoverPendingSourceRestore recovery)
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var disks = new DisksStepViewModel(new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory)), recovery);
        var review = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));

        return (new WizardViewModel(disks, review), disks);
    }
}
