using System.ComponentModel;
using Shouldly;
using WinClonerX.App.ViewModels;
using Xunit;

namespace WinClonerX.App.Tests.ViewModels;

public sealed class WizardViewModelTests
{
    [Fact]
    public void HasFiveStepsInOrder()
    {
        var vm = new WizardViewModel();

        vm.Steps.Select(step => step.Kind).ShouldBe(
        [
            WizardStepKind.Disks,
            WizardStepKind.Review,
            WizardStepKind.Confirm,
            WizardStepKind.Progress,
            WizardStepKind.Result,
        ]);
        vm.Steps.Select(step => step.TitleKey).ShouldBe(
        [
            "Wizard_Step_Disks",
            "Wizard_Step_Review",
            "Wizard_Step_Confirm",
            "Wizard_Step_Progress",
            "Wizard_Step_Result",
        ]);
    }

    [Fact]
    public void InitialStepIsDisks()
    {
        var vm = new WizardViewModel();

        vm.CurrentIndex.ShouldBe(0);
        vm.CurrentStep.Kind.ShouldBe(WizardStepKind.Disks);
    }

    [Fact]
    public void NextAdvancesToTheFollowingStep()
    {
        var vm = new WizardViewModel();

        vm.NextCommand.Execute(null);

        vm.CurrentIndex.ShouldBe(1);
        vm.CurrentStep.Kind.ShouldBe(WizardStepKind.Review);
    }

    [Fact]
    public void BackReturnsToThePreviousStep()
    {
        var vm = new WizardViewModel();
        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        vm.BackCommand.Execute(null);

        vm.CurrentStep.Kind.ShouldBe(WizardStepKind.Review);
    }

    [Fact]
    public void BackIsDisabledOnTheFirstStep()
    {
        var vm = new WizardViewModel();

        vm.CanGoBack.ShouldBeFalse();
        vm.BackCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void NextIsDisabledOnTheLastStep()
    {
        var vm = NavigateTo(WizardStepKind.Result);

        vm.CanGoNext.ShouldBeFalse();
        vm.NextCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void BackIsDisabledOnProgress()
    {
        var vm = NavigateTo(WizardStepKind.Progress);

        vm.CurrentStep.Kind.ShouldBe(WizardStepKind.Progress);
        vm.CanGoBack.ShouldBeFalse();
        vm.BackCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void BackIsDisabledOnResult()
    {
        var vm = NavigateTo(WizardStepKind.Result);

        vm.IsResultStep.ShouldBeTrue();
        vm.IsBackVisible.ShouldBeFalse();
        vm.CanGoBack.ShouldBeFalse();
        vm.BackCommand.CanExecute(null).ShouldBeFalse();
        vm.IsNextVisible.ShouldBeFalse();
        vm.NextCommand.CanExecute(null).ShouldBeFalse();
    }

    [Theory]
    [InlineData(WizardStepKind.Disks, true)]
    [InlineData(WizardStepKind.Review, true)]
    [InlineData(WizardStepKind.Confirm, true)]
    [InlineData(WizardStepKind.Progress, true)]
    [InlineData(WizardStepKind.Result, false)]
    public void BackVisibilityChangesOnlyOnResultStep(WizardStepKind kind, bool expected)
    {
        var vm = NavigateTo(kind);

        vm.IsBackVisible.ShouldBe(expected);
        vm.IsResultStep.ShouldBe(kind == WizardStepKind.Result);
    }

    [Fact]
    public void NextIsDisabledWhenCurrentStepIsInvalid()
    {
        var vm = new WizardViewModel();

        vm.IsCurrentStepValid = false;

        vm.CanGoNext.ShouldBeFalse();
        vm.NextCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void NextBecomesEnabledWhenCurrentStepIsValidAgain()
    {
        var vm = new WizardViewModel();
        vm.IsCurrentStepValid = false;

        vm.IsCurrentStepValid = true;

        vm.CanGoNext.ShouldBeTrue();
        vm.NextCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void CurrentStepIsValidByDefault()
    {
        var vm = new WizardViewModel();

        vm.IsCurrentStepValid.ShouldBeTrue();
    }

    [Fact]
    public void StepItemsMarkCurrentAndCompletedSteps()
    {
        var vm = new WizardViewModel();

        vm.NextCommand.Execute(null);
        vm.NextCommand.Execute(null);

        vm.StepItems.Select(item => item.IsCurrent).ShouldBe([false, false, true, false, false]);
        vm.StepItems.Select(item => item.IsCompleted).ShouldBe([true, true, false, false, false]);
        vm.StepItems.Select(item => item.Number).ShouldBe([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void StepItemFlagsUpdateWhenGoingBack()
    {
        var vm = NavigateTo(WizardStepKind.Progress);

        vm.BackCommand.CanExecute(null).ShouldBeFalse();
        vm.NextCommand.Execute(null);
        vm.StepItems.Single(item => item.IsCurrent).Step.Kind.ShouldBe(WizardStepKind.Result);
        vm.StepItems.Select(item => item.IsCompleted).ShouldBe([true, true, true, true, false]);
    }

    [Fact]
    public void RaisesPropertyChangedForCurrentStep()
    {
        var vm = new WizardViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        vm.NextCommand.Execute(null);

        changed.ShouldContain(nameof(WizardViewModel.CurrentStep));
        changed.ShouldContain(nameof(WizardViewModel.CurrentIndex));
        changed.ShouldContain(nameof(WizardViewModel.CanGoBack));
    }

    private static WizardViewModel NavigateTo(WizardStepKind kind)
    {
        var vm = new WizardViewModel();
        while (vm.CurrentStep.Kind != kind)
        {
            vm.NextCommand.Execute(null);
        }

        return vm;
    }
}
