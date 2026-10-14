namespace WinCopy.Domain.Planning;

/// <summary>
/// Resultado de una comprobación: su código, su gravedad y los datos que la interfaz necesita para explicarla
/// (por ejemplo, el espacio que falta). El dominio no contiene textos.
/// </summary>
public sealed record PreflightCheck(PreflightCode Code, Severity Severity, IReadOnlyDictionary<string, object> Facts)
{
    /// <summary>Diccionario de datos vacío, para las comprobaciones que no necesitan datos.</summary>
    public static IReadOnlyDictionary<string, object> EmptyFacts() => new Dictionary<string, object>(StringComparer.Ordinal);
}
