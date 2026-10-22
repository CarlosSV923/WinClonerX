namespace WinClonerX.App.ViewModels;

/// <summary>Pasos del asistente, en orden. Ver contracts/ui-wizard.md.</summary>
public enum WizardStepKind
{
    Disks,
    Review,
    Confirm,
    Progress,
    Result,
}

/// <summary>One wizard step. <paramref name="TitleKey"/> is a resource key in Strings.resx.</summary>
public sealed record WizardStep(WizardStepKind Kind, string TitleKey);
