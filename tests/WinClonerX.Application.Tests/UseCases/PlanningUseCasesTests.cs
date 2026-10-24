using System.Globalization;
using Shouldly;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Tests.Fakes;
using WinClonerX.Application.Tests.TestData;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Application.Tests.TestData.PlanningTestData;

namespace WinClonerX.Application.Tests.UseCases;

public sealed class PlanningUseCasesTests
{
    /// <summary>Sufijo que hay que escribir para confirmar TargetDisk(): los 4 últimos caracteres de "TGT-0002".</summary>
    private const string TargetSuffix = "0002";

    private static readonly CancellationToken Ct = CancellationToken.None;

    private static readonly string[] OverviewEventNames = ["InventoryCaptured", "PreflightEvaluated"];

    private static readonly string[] TargetNotEmptyNames = ["TargetNotEmpty"];

    [Fact]
    public async Task OverviewReportsEligibleLargerTargetAndNeverListsTheSystemDisk()
    {
        var h = new Harness();
        var inventory = InventoryOf(SystemDisk(), TargetDisk(), SmallTarget());
        h.Reader.Inventory = inventory;

        var overview = await h.Overview.ExecuteAsync(Ct);

        overview.Inventory.ShouldBeSameAs(inventory);
        overview.Global.IsBlocked.ShouldBeFalse();
        overview.Targets.Any(t => t.Disk == SourceId).ShouldBeFalse();
        overview.Targets.Select(t => t.Disk).ShouldBe(new[] { TargetId, OtherTargetId });

        var larger = overview.Targets.Single(t => t.Disk == TargetId);
        larger.IsEligible.ShouldBeTrue();
        larger.Strategy.ShouldBe(SizeStrategy.SameOrLarger);

        var smaller = overview.Targets.Single(t => t.Disk == OtherTargetId);
        smaller.IsEligible.ShouldBeFalse();
        smaller.Reasons.Any(r => r.Code == PreflightCode.TargetTooSmall && r.Severity == Severity.Blocking).ShouldBeTrue();
    }

    [Fact]
    public async Task OverviewLogsInventoryAndPreflightWithExpectedProperties()
    {
        var h = new Harness();
        h.Reader.Inventory = InventoryOf(SystemDisk(), TargetDisk(), SmallTarget());

        await h.Overview.ExecuteAsync(Ct);

        h.Log.Events.Select(e => e.Event).ShouldBe(OverviewEventNames);

        var captured = h.Log.Events[0];
        captured.Properties["Firmware"].ShouldBe("Uefi");

        var disks = (Dictionary<string, object?>[])captured.Properties["Disks"]!;
        disks.Length.ShouldBe(3);
        disks[0]["Id"].ShouldBe(Text(SourceId.DiskGuid));
        disks[0]["Partitions"].ShouldBe(4);
        disks[0]["SizeBytes"].ShouldBe(256 * Gib);
        disks[1]["Id"].ShouldBe(Text(TargetId.DiskGuid));
        disks[1]["Serial"].ShouldBe("TGT-0002");
        disks[1]["Bus"].ShouldBe("Usb");
        disks[2]["Serial"].ShouldBe("SMALL-0003");

        var preflight = h.Log.Events[1];
        var checks = (Dictionary<string, object?>[])preflight.Properties["Checks"]!;
        checks.Any(c => (string?)c["Code"] == "FirmwareNotUefi" && (string?)c["Severity"] == "Pass" && c["Target"] == null)
            .ShouldBeTrue();
        checks.Any(c => (string?)c["Code"] == "TargetTooSmall" && (string?)c["Severity"] == "Blocking"
            && (string?)c["Target"] == Text(OtherTargetId.DiskGuid))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task BuildSuccessSavesThePlanAndLogsPlanCreated()
    {
        var h = new Harness();
        h.Reader.Inventory = InventoryOf(SystemDisk(), TargetDisk());
        await h.Overview.ExecuteAsync(Ct);

        var result = await h.Build.ExecuteAsync(TargetId, VerificationMode.Structural, Ct);

        result.IsSuccess.ShouldBeTrue();
        var plan = result.Value;
        plan.Status.ShouldBe(PlanStatus.Draft);
        plan.Target.ShouldBe(TargetId);

        h.Store.TryGet(plan.Id, out var stored).ShouldBeTrue();
        stored.ShouldBeSameAs(plan);

        var created = h.Log.Events[^1];
        created.Event.ShouldBe("PlanCreated");
        created.Properties["PlanId"].ShouldBe(Text(plan.Id.Value));
        created.Properties["Source"].ShouldBe(Text(SourceId.DiskGuid));
        created.Properties["Target"].ShouldBe(Text(TargetId.DiskGuid));
        created.Properties["Verification"].ShouldBe("Structural");
        created.Properties["EstimateBytes"].ShouldBe(plan.Estimate.BytesToCopy.Bytes);

        var mappings = (Dictionary<string, object?>[])created.Properties["Mappings"]!;
        mappings.Length.ShouldBe(4);
        mappings.Length.ShouldBe(plan.Mappings.Count);
    }

    [Fact]
    public async Task BuildTooSmallTargetReturnsFailureReportWithTargetTooSmall()
    {
        var h = new Harness();
        h.Reader.Inventory = InventoryOf(SystemDisk(), SmallTarget());
        await h.Overview.ExecuteAsync(Ct);

        var result = await h.Build.ExecuteAsync(OtherTargetId, VerificationMode.Structural, Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Checks.Any(c => c.Code == PreflightCode.TargetTooSmall && c.Severity == Severity.Blocking).ShouldBeTrue();
        h.Log.Events.Any(e => e.Event == "PlanCreated").ShouldBeFalse();
    }

    [Fact]
    public async Task BuildWithoutCapturedInventoryThrows()
    {
        var h = new Harness();

        await Should.ThrowAsync<InvalidOperationException>(
            () => h.Build.ExecuteAsync(TargetId, VerificationMode.Structural, Ct));
    }

    [Fact]
    public async Task ConfirmWithRightSuffixAndAllWarningsAcceptedConfirmsThePlan()
    {
        var (h, plan) = await BuildDraftAsync(TargetDisk(withDataPartition: true));
        plan.Warnings.Select(w => w.Code).ShouldBe(new[] { PreflightCode.TargetNotEmpty });

        var confirmed = await h.Confirm.ExecuteAsync(plan.Id, new[] { PreflightCode.TargetNotEmpty }, TargetSuffix, false, Ct);

        confirmed.Status.ShouldBe(PlanStatus.Confirmed);
        confirmed.Confirmation!.Disk.ShouldBe(TargetId);
        confirmed.Confirmation.At.ShouldBe(FakeDefaults.Now);

        h.Store.TryGet(plan.Id, out var stored).ShouldBeTrue();
        stored!.Status.ShouldBe(PlanStatus.Confirmed);

        var logged = h.Log.Events[^1];
        logged.Event.ShouldBe("PlanConfirmed");
        logged.Properties["PlanId"].ShouldBe(Text(plan.Id.Value));
        logged.Properties["AcceptedWarnings"].ShouldBe(TargetNotEmptyNames);
        logged.Properties["ShrinkConfirmed"].ShouldBe(false);
    }

    [Fact]
    public async Task ConfirmWithWrongSuffixThrowsAndThePlanStaysDraft()
    {
        var (h, plan) = await BuildDraftAsync(TargetDisk(withDataPartition: true));

        await Should.ThrowAsync<InvalidOperationException>(
            () => h.Confirm.ExecuteAsync(plan.Id, new[] { PreflightCode.TargetNotEmpty }, "9999", false, Ct));

        h.Store.TryGet(plan.Id, out var stored).ShouldBeTrue();
        stored!.Status.ShouldBe(PlanStatus.Draft);
        h.Log.Events.Any(e => e.Event == "PlanConfirmed").ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmWithoutAcceptingEveryWarningThrowsAndThePlanStaysDraft()
    {
        var (h, plan) = await BuildDraftAsync(TargetDisk(withDataPartition: true));

        await Should.ThrowAsync<InvalidOperationException>(
            () => h.Confirm.ExecuteAsync(plan.Id, Array.Empty<PreflightCode>(), TargetSuffix, false, Ct));

        h.Store.TryGet(plan.Id, out var stored).ShouldBeTrue();
        stored!.Status.ShouldBe(PlanStatus.Draft);
    }

    [Fact]
    public async Task ConfirmAfterDisksChangedInvalidatesThePlanAndThrows()
    {
        var h = new Harness();
        h.Reader.Inventory = InventoryOf(SystemDisk(), TargetDisk());
        await h.Overview.ExecuteAsync(Ct);
        var plan = (await h.Build.ExecuteAsync(TargetId, VerificationMode.Structural, Ct)).Value;

        // Entre la planificación y la confirmación cambia el diseño del destino: aparece una partición de datos.
        h.Reader.Inventory = InventoryOf(SystemDisk(), TargetDisk(withDataPartition: true));

        var thrown = await Should.ThrowAsync<PlanInvalidatedException>(
            () => h.Confirm.ExecuteAsync(plan.Id, Array.Empty<PreflightCode>(), TargetSuffix, false, Ct));

        thrown.PlanId.ShouldBe(plan.Id);
        thrown.Reason.ShouldBe("DisksChanged");
        plan.Status.ShouldBe(PlanStatus.Invalidated);
        plan.InvalidationReason.ShouldBe("DisksChanged");
        h.Reader.Calls.Count(c => c == nameof(IDiskInventoryReader.ReadAsync)).ShouldBe(2);

        var logged = h.Log.Events[^1];
        logged.Event.ShouldBe("PlanInvalidated");
        logged.Properties["PlanId"].ShouldBe(Text(plan.Id.Value));
        logged.Properties["Reason"].ShouldBe("DisksChanged");
    }

    [Fact]
    public async Task ConfirmUnknownPlanThrows()
    {
        var h = new Harness();

        await Should.ThrowAsync<InvalidOperationException>(
            () => h.Confirm.ExecuteAsync(new PlanId(Guid.NewGuid()), Array.Empty<PreflightCode>(), TargetSuffix, false, Ct));
    }

    /// <summary>Captura el inventario con el origen y el destino indicado, y construye un plan en borrador.</summary>
    private static async Task<(Harness Harness, ClonePlan Plan)> BuildDraftAsync(Disk target)
    {
        var h = new Harness();
        h.Reader.Inventory = InventoryOf(SystemDisk(), target);
        await h.Overview.ExecuteAsync(Ct);

        var built = await h.Build.ExecuteAsync(target.Id, VerificationMode.Structural, Ct);
        return (h, built.Value);
    }

    private static string Text(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>Dependencias de los casos de uso con sus fakes, cableadas como en la aplicación.</summary>
    private sealed class Harness
    {
        public FakeDiskInventoryReader Reader { get; } = new();

        public FakePlatformFactsProvider FactsProvider { get; } = new();

        public FakeExecutionLog Log { get; } = new();

        public FakeClock Clock { get; } = new();

        public ClonePlanStore Store { get; } = new();

        public GetInventoryAndEligibility Overview { get; }

        public BuildClonePlan Build { get; }

        public ConfirmClonePlan Confirm { get; }

        public Harness()
        {
            FactsProvider.Facts = PlanningTestData.Facts();
            Overview = new GetInventoryAndEligibility(Reader, FactsProvider, Store, Log);
            Build = new BuildClonePlan(Store, Log, SequentialGuids());
            Confirm = new ConfirmClonePlan(Store, Reader, Log, Clock);
        }
    }
}
