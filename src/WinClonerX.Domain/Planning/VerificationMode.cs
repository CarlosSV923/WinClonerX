namespace WinClonerX.Domain.Planning;

/// <summary>Verificación que se hace al terminar el clonado.</summary>
public enum VerificationMode
{
    /// <summary>Solo la verificación estructural (siempre).</summary>
    Structural,

    /// <summary>Estructural más la comparación completa de los datos (opcional, desactivada por defecto).</summary>
    StructuralAndFull,
}
