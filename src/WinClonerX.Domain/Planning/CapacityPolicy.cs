using WinClonerX.Domain.Shared;

namespace WinClonerX.Domain.Planning;

/// <summary>
/// Margen de seguridad sobre el espacio ocupado: el mayor entre un porcentaje de lo ocupado y un mínimo fijo.
/// Ambos valores son configurables para las pruebas; <see cref="Default"/> usa el 10 % y 10 GiB (supuesto de la especificación).
/// </summary>
public sealed class CapacityPolicy
{
    /// <summary>Política de la versión 1: margen = máx(10 % de lo ocupado, 10 GiB).</summary>
    public static CapacityPolicy Default { get; } = new(0.10m, ByteSize.FromGiB(10));

    /// <summary>Crea una política. El porcentaje no puede ser negativo.</summary>
    public CapacityPolicy(decimal percentage, ByteSize minimum)
    {
        if (percentage < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(percentage), percentage, "El porcentaje de margen no puede ser negativo.");
        }

        Percentage = percentage;
        Minimum = minimum;
    }

    /// <summary>Porcentaje de lo ocupado que se reserva como margen.</summary>
    public decimal Percentage { get; }

    /// <summary>Margen mínimo, aunque el porcentaje sea menor.</summary>
    public ByteSize Minimum { get; }

    /// <summary>Margen para un espacio ocupado dado: el mayor entre el porcentaje (redondeado al byte superior) y el mínimo.</summary>
    public ByteSize Margin(ByteSize used)
    {
        var proportional = (long)Math.Ceiling(used.Bytes * Percentage);
        return new ByteSize(Math.Max(proportional, Minimum.Bytes));
    }

    /// <summary>Espacio que debe tener el destino para el espacio ocupado: lo ocupado más el margen.</summary>
    public ByteSize Required(ByteSize used) => used + Margin(used);
}
