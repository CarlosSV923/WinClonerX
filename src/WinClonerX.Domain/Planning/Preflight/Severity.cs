namespace WinClonerX.Domain.Planning;

/// <summary>Resultado de una comprobación previa.</summary>
public enum Severity
{
    /// <summary>La comprobación se supera.</summary>
    Pass,

    /// <summary>Hay que aceptarlo de forma explícita para continuar.</summary>
    Warning,

    /// <summary>Impide el clonado.</summary>
    Blocking,
}
