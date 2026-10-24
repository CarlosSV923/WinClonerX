using Shouldly;
using WinClonerX.Application.Tests.Fakes;
using WinClonerX.Application.Tests.TestData;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using Xunit;
using static WinClonerX.Application.Tests.TestData.ShrinkTestData;

namespace WinClonerX.Application.Tests.UseCases;

/// <summary>Confirmación de planes con reducción del origen (FR-013): la autorización explícita de la reducción.</summary>
public sealed class ConfirmClonePlanShrinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShrinkPlanWithoutTheShrinkConfirmationThrowsAndStaysDraft()
    {
        var h = new ConfirmHarness(ShrinkInventory());
        var plan = ShrinkDraft();
        h.Store.Save(plan);

        await Should.ThrowAsync<InvalidOperationException>(
            () => h.Confirm.ExecuteAsync(plan.Id, WarningsOf(plan), ShrinkTargetSuffix, false, Ct));

        h.Store.TryGet(plan.Id, out var stored).ShouldBeTrue();
        stored!.Status.ShouldBe(PlanStatus.Draft);
        stored.SourceShrinkConfirmation.ShouldBeNull();
        h.Log.Events.Any(e => e.Event == "PlanConfirmed").ShouldBeFalse();
    }

    [Fact]
    public async Task ShrinkPlanWithTheShrinkConfirmationIsConfirmedAndLogsShrinkConfirmed()
    {
        var h = new ConfirmHarness(ShrinkInventory());
        var plan = ShrinkDraft();
        h.Store.Save(plan);

        var confirmed = await h.Confirm.ExecuteAsync(plan.Id, WarningsOf(plan), ShrinkTargetSuffix, true, Ct);

        confirmed.Status.ShouldBe(PlanStatus.Confirmed);
        var shrink = plan.SourceShrink!;
        var confirmation = confirmed.SourceShrinkConfirmation!;
        confirmation.Partition.ShouldBe(shrink.Partition);
        confirmation.ShrunkLength.ShouldBe(shrink.ShrunkLength);
        confirmation.At.ShouldBe(FakeDefaults.Now);

        var logged = h.Log.Events[^1];
        logged.Event.ShouldBe("PlanConfirmed");
        logged.Properties["ShrinkConfirmed"].ShouldBe(true);
    }

    [Fact]
    public async Task NonShrinkPlanWithTheShrinkConfirmationThrowsArgumentExceptionAndStaysDraft()
    {
        var h = new ConfirmHarness(NonShrinkInventory());
        var plan = NonShrinkDraft();
        h.Store.Save(plan);

        await Should.ThrowAsync<ArgumentException>(
            () => h.Confirm.ExecuteAsync(plan.Id, WarningsOf(plan), "0002", true, Ct));

        h.Store.TryGet(plan.Id, out var stored).ShouldBeTrue();
        stored!.Status.ShouldBe(PlanStatus.Draft);
        h.Log.Events.Any(e => e.Event == "PlanConfirmed").ShouldBeFalse();
    }

    [Fact]
    public async Task NonShrinkPlanWithoutTheShrinkConfirmationLogsShrinkConfirmedFalse()
    {
        var h = new ConfirmHarness(NonShrinkInventory());
        var plan = NonShrinkDraft();
        h.Store.Save(plan);

        var confirmed = await h.Confirm.ExecuteAsync(plan.Id, WarningsOf(plan), "0002", false, Ct);

        confirmed.Status.ShouldBe(PlanStatus.Confirmed);
        confirmed.SourceShrinkConfirmation.ShouldBeNull();
        h.Log.Events[^1].Properties["ShrinkConfirmed"].ShouldBe(false);
    }

    private static PreflightCode[] WarningsOf(ClonePlan plan) =>
        plan.Warnings.Select(warning => warning.Code).ToArray();

    /// <summary>Caso de uso de confirmación con sus fakes, cableado como en la aplicación.</summary>
    private sealed class ConfirmHarness
    {
        public ConfirmHarness(DiskInventory inventory)
        {
            Reader.Inventory = inventory;
            Confirm = new ConfirmClonePlan(Store, Reader, Log, Clock);
        }

        public FakeDiskInventoryReader Reader { get; } = new();

        public FakeExecutionLog Log { get; } = new();

        public FakeClock Clock { get; } = new();

        public ClonePlanStore Store { get; } = new();

        public ConfirmClonePlan Confirm { get; }
    }
}
