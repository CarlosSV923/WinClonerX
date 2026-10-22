namespace WinClonerX.Domain.Planning;

/// <summary>Identificador de un plan de clonado. No puede estar vacío.</summary>
public readonly record struct PlanId(Guid Value)
{
    /// <summary>GUID del plan; no puede ser el GUID vacío.</summary>
    public Guid Value { get; } = Value != Guid.Empty
        ? Value
        : throw new ArgumentException("El identificador del plan no puede estar vacío.", nameof(Value));
}
