using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

public sealed class ConfirmStepViewModelTests
{
    private const string SerialWithSuffix = "SN-AB7F";

    [Fact]
    public void CloneNowIsDisabledUntilTheSuffixMatches()
    {
        using var _ = new EnglishCulture();
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));
        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.CloneNowCommand.CanExecute(null).ShouldBeFalse();

        vm.TypedSuffix = "AB7E";
        vm.IsSuffixValid.ShouldBeFalse();
        vm.CloneNowCommand.CanExecute(null).ShouldBeFalse();

        vm.TypedSuffix = "AB7F";
        vm.IsSuffixValid.ShouldBeTrue();
        vm.CloneNowCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void TheSuffixMatchIgnoresCase()
    {
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));
        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.TypedSuffix = "ab7f";
        vm.IsSuffixValid.ShouldBeTrue();

        vm.TypedSuffix = "Ab7F";
        vm.IsSuffixValid.ShouldBeTrue();
    }

    [Fact]
    public void TheSuffixIgnoresSurroundingSpaces()
    {
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));
        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.TypedSuffix = "  AB7F ";

        vm.IsSuffixValid.ShouldBeTrue();
    }

    [Fact]
    public void TheSuffixOfTheSerialNumberIsTheLastFourCharacters()
    {
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: "XXXX-0002"));
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));
        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.TypedSuffix = "0002";
        vm.IsSuffixValid.ShouldBeTrue();

        vm.TypedSuffix = "XXXX";
        vm.IsSuffixValid.ShouldBeFalse();
    }

    [Fact]
    public void WithoutASerialNumberTheLastFourCharactersOfTheGuidAreRequested()
    {
        using var _ = new EnglishCulture();
        // The test target id is built from the GUID ...0200, so its suffix is "0200".
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: string.Empty));
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));
        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.UsesGuidFallback.ShouldBeTrue();
        vm.PromptText.ShouldBe(Loc.Format("Confirm_Prompt_Guid", "XXXX"));

        vm.TypedSuffix = "0200";
        vm.IsSuffixValid.ShouldBeTrue();

        vm.TypedSuffix = "0002";
        vm.IsSuffixValid.ShouldBeFalse();
    }

    [Fact]
    public void ThePromptShowsTheLengthOfTheSuffixButNeverItsValue()
    {
        using var _ = new EnglishCulture();
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));

        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.UsesGuidFallback.ShouldBeFalse();
        vm.PromptText.ShouldBe(Loc.Format("Confirm_Prompt_Serial", "XXXX"));
        vm.PromptText.ShouldNotContain("AB7F");
    }

    [Fact]
    public void TheTargetCardShowsTheFullSerialNumber()
    {
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));

        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.TargetCard.ShouldNotBeNull();
        vm.TargetCard.SerialNumber.ShouldBe(SerialWithSuffix);
        vm.TargetCard.Model.ShouldBe(target.Model);
    }

    [Fact]
    public void PrepareForgetsTheSuffixTypedBefore()
    {
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));
        vm.Prepare(target, plan, new HashSet<PreflightCode>());
        vm.TypedSuffix = "AB7F";

        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.TypedSuffix.ShouldBe(string.Empty);
        vm.IsSuffixValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmingSendsThePlanWarningsAndSuffixAndRaisesPlanConfirmed()
    {
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var confirm = new FakeConfirmClonePlan(plan);
        var vm = new ConfirmStepViewModel(confirm);
        var warnings = new HashSet<PreflightCode> { PreflightCode.BitLockerOn };
        vm.Prepare(target, plan, warnings);
        vm.TypedSuffix = "AB7F";
        PlanConfirmedEventArgs? raised = null;
        vm.PlanConfirmed += (_, args) => raised = args;

        await vm.CloneNowCommand.ExecuteAsync(null);

        confirm.CallCount.ShouldBe(1);
        confirm.LastPlanId.ShouldBe(plan.Id);
        confirm.LastAcceptedWarnings!.ShouldBe(warnings);
        confirm.LastTypedSuffix.ShouldBe("AB7F");
        raised.ShouldNotBeNull();
        raised.PlanId.ShouldBe(plan.Id);
        vm.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task APlanThatChangedShowsTheMessageAndAsksToReturnToTheDisks()
    {
        using var _ = new EnglishCulture();
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var confirm = new FakeConfirmClonePlan(plan)
        {
            NextFailure = new PlanInvalidatedException(plan.Id, "DisksChanged"),
        };
        var vm = new ConfirmStepViewModel(confirm);
        vm.Prepare(target, plan, new HashSet<PreflightCode>());
        vm.TypedSuffix = "AB7F";
        ReturnToDisksEventArgs? requested = null;
        PlanConfirmedEventArgs? confirmed = null;
        vm.ReturnToDisksRequested += (_, args) => requested = args;
        vm.PlanConfirmed += (_, args) => confirmed = args;

        await vm.CloneNowCommand.ExecuteAsync(null);

        requested.ShouldNotBeNull();
        requested.Message.ShouldBe("The disks changed; please review again");
        requested.Message.ShouldBe(Loc.Get("Confirm_PlanInvalidated"));
        confirmed.ShouldBeNull();
        vm.TypedSuffix.ShouldBe(string.Empty);
        vm.IsBusy.ShouldBeFalse();
    }

    [Fact]
    public async Task AFailedConfirmationKeepsThePlanAndShowsAMessage()
    {
        using var _ = new EnglishCulture();
        var (target, plan) = Plan(AppFixtures.TargetDisk(serial: SerialWithSuffix));
        var confirm = new FakeConfirmClonePlan(plan)
        {
            NextFailure = new InvalidOperationException("The suffix does not match."),
        };
        var vm = new ConfirmStepViewModel(confirm);
        vm.Prepare(target, plan, new HashSet<PreflightCode>());
        vm.TypedSuffix = "AB7F";
        var raised = false;
        vm.PlanConfirmed += (_, _) => raised = true;
        vm.ReturnToDisksRequested += (_, _) => raised = true;

        await vm.CloneNowCommand.ExecuteAsync(null);

        raised.ShouldBeFalse();
        vm.ErrorMessage.ShouldBe(Loc.Get("Confirm_Failed"));
        vm.IsSuffixValid.ShouldBeTrue();
    }

    [Fact]
    public void CloneNowIsNotAvailableBeforeAPlanIsPrepared()
    {
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(Plan(AppFixtures.TargetDisk()).Plan));

        vm.IsSuffixValid.ShouldBeFalse();
        vm.CloneNowCommand.CanExecute(null).ShouldBeFalse();
    }

    private static (Disk Target, ClonePlan Plan) Plan(Disk target)
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), target);
        return (target, AppFixtures.PlanFor(inventory, target.Id).Value);
    }
}
