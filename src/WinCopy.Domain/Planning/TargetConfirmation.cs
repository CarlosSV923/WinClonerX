using System.Globalization;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Confirmación escrita por la persona que identifica el disco que se borrará (FR-012, R13):
/// los 4 últimos caracteres del número de serie, o del GUID de disco si no hay número de serie útil.
/// </summary>
public sealed record TargetConfirmation(DiskId Disk, string TypedSuffix, DateTimeOffset At)
{
    /// <summary>Sufijo que hay que escribir para confirmar el disco de destino, en mayúsculas.</summary>
    public static string ExpectedSuffix(Disk target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var identifier = UsesGuidFallback(target)
            ? target.Id.DiskGuid.ToString("N", CultureInfo.InvariantCulture)
            : target.SerialNumber.Trim();
        var suffix = identifier.Length <= 4 ? identifier : identifier[^4..];

        return suffix.ToUpperInvariant();
    }

    /// <summary>
    /// Indica si el sufijo se toma del GUID de disco porque el número de serie no sirve: vacío, o formado solo por ceros
    /// (algunos puentes USB informan "0000000000000000"). Se ignoran los espacios y los puntos finales antes de decidir.
    /// </summary>
    public static bool UsesGuidFallback(Disk target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var normalized = target.SerialNumber.Trim().TrimEnd('.').Trim();

        return normalized.Length == 0 || normalized.All(character => character == '0');
    }

    /// <summary>Indica si la confirmación corresponde al disco indicado, sin distinguir mayúsculas.</summary>
    public bool Matches(Disk target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Disk == target.Id
            && string.Equals(TypedSuffix.Trim(), ExpectedSuffix(target), StringComparison.OrdinalIgnoreCase);
    }
}
