namespace WinCopy.Domain.Execution;

/// <summary>Identificador de una ejecución de clonado. No puede estar vacío.</summary>
public readonly record struct ExecutionId(Guid Value)
{
    /// <summary>GUID de la ejecución; no puede ser el GUID vacío.</summary>
    public Guid Value { get; } = Value != Guid.Empty
        ? Value
        : throw new ArgumentException("El identificador de la ejecución no puede estar vacío.", nameof(Value));
}
