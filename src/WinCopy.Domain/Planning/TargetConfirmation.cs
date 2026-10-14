using System.Globalization;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Confirmación escrita por la persona que identifica el disco que se borrará (FR-012, R13):
/// los 4 últimos caracteres del número de serie, o del GUID de disco si no hay número de serie.
/// </summary>
public sealed record TargetConfirmation(DiskId Disk, string TypedSuffix, DateTimeOffset At)
{
    /// <summary>Sufijo que hay que escribir para confirmar el disco de destino, en mayúsculas.</summary>
    public static string ExpectedSuffix(Disk target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var serial = target.SerialNumber.Trim();
        var identifier = serial.Length > 0 ? serial : target.Id.DiskGuid.ToString("N", CultureInfo.InvariantCulture);
        var suffix = identifier.Length <= 4 ? identifier : identifier[^4..];

        return suffix.ToUpperInvariant();
    }

    /// <summary>Indica si la confirmación corresponde al disco indicado, sin distinguir mayúsculas.</summary>
    public bool Matches(Disk target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Disk == target.Id
            && string.Equals(TypedSuffix.Trim(), ExpectedSuffix(target), StringComparison.OrdinalIgnoreCase);
    }
}
