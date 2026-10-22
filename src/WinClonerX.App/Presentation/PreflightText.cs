using WinClonerX.App.Localization;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;

namespace WinClonerX.App.Presentation;

/// <summary>
/// Turns preflight checks into localized text. Every <see cref="PreflightCode"/> has a <c>Preflight_&lt;Code&gt;</c>
/// resource and every <see cref="Severity"/> a <c>Severity_&lt;Severity&gt;</c> resource.
/// </summary>
public static class PreflightText
{
    /// <summary>What is wrong and what the person can do about it, with the data the check carries.</summary>
    public static string Message(PreflightCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        var key = "Preflight_" + check.Code;
        return check.Code switch
        {
            PreflightCode.TargetTooSmall => Loc.Format(key, SizeFact(check, "Shortfall")),
            PreflightCode.SnapshotSpaceLow => Loc.Format(key, SizeFact(check, "FreeSpace")),
            _ => Loc.Get(key),
        };
    }

    /// <summary>The severity as a short label, so the meaning does not depend on color alone.</summary>
    public static string SeverityLabel(Severity severity) => Loc.Get("Severity_" + severity);

    /// <summary>Severity label followed by the message, for lists of reasons.</summary>
    public static string Reason(PreflightCheck check) =>
        Loc.Format("Preflight_Reason_Format", SeverityLabel(check.Severity), Message(check));

    private static string SizeFact(PreflightCheck check, string name)
    {
        var size = check.Facts.TryGetValue(name, out var value) && value is ByteSize known ? known : default;
        return SizeText.Format(size);
    }
}
