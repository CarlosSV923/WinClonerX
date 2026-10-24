namespace WinClonerX.Domain.Shared;

/// <summary>Identificador único de una partición GPT. No puede estar vacío.</summary>
public readonly record struct PartitionGuid(Guid Value)
{
    /// <summary>GUID de la partición; no puede ser el GUID vacío.</summary>
    public Guid Value { get; } = Value != Guid.Empty
        ? Value
        : throw new ArgumentException("No puede estar vacío.", nameof(Value));
}
