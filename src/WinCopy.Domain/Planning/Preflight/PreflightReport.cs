namespace WinCopy.Domain.Planning;

/// <summary>Conjunto de comprobaciones previas de una planificación. Está bloqueado si alguna es bloqueante.</summary>
public sealed record PreflightReport(IReadOnlyList<PreflightCheck> Checks)
{
    /// <summary>Indica si alguna comprobación impide el clonado.</summary>
    public bool IsBlocked => Checks.Any(check => check.Severity == Severity.Blocking);

    /// <summary>Comprobaciones con aviso, que requieren aceptación explícita.</summary>
    public IReadOnlyList<PreflightCheck> Warnings => Checks.Where(check => check.Severity == Severity.Warning).ToArray();
}
