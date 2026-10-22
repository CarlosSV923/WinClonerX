using System.Globalization;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Confirmación escrita por la persona que identifica el disco que se borrará (FR-012, R13):
/// los 4 últimos caracteres del número de serie, del GUID, del modelo o de DISCO seguido del número de disco.
/// </summary>
public sealed record TargetConfirmation(DiskId Disk, string TypedSuffix, DateTimeOffset At)
{
    /// <summary>Sufijo que hay que escribir para confirmar el disco de destino, en mayúsculas.</summary>
    public static string ExpectedSuffix(Disk target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var source = GetSource(target);
        var identifier = source == ConfirmationSource.Model
            ? ModelIdentifier(target.Model)
            : ConfirmationIdentifier(target, source);
        var suffix = identifier.Length <= 4 ? identifier : identifier[^4..];

        return suffix.ToUpperInvariant();
    }

    /// <summary>
    /// Determina de dónde sale el sufijo. Si el número de serie no sirve (vacío o formado solo por ceros), se usa el GUID;
    /// si está vacío, se usan los últimos 4 caracteres alfanuméricos del modelo, sin contar espacios ni signos. Si el modelo
    /// tiene menos de 4 caracteres alfanuméricos, se usa "DISCO" seguido del número de disco; sus últimos 4 caracteres identifican
    /// el número incluso cuando tiene menos de 4 dígitos.
    /// </summary>
    public static ConfirmationSource GetSource(Disk target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var normalized = target.SerialNumber.Trim().TrimEnd('.').Trim();
        if (normalized.Length > 0 && normalized.Any(character => character != '0'))
        {
            return ConfirmationSource.SerialNumber;
        }

        if (target.Id.DiskGuid != Guid.Empty)
        {
            return ConfirmationSource.DiskGuid;
        }

        return ModelIdentifier(target.Model).Length >= 4 ? ConfirmationSource.Model : ConfirmationSource.DiskNumber;
    }

    public static string ConfirmationIdentifier(Disk target, ConfirmationSource source) => source switch
    {
        ConfirmationSource.SerialNumber => target.SerialNumber.Trim(),
        ConfirmationSource.DiskGuid => target.Id.DiskGuid.ToString("D", CultureInfo.InvariantCulture),
        ConfirmationSource.Model => target.Model.Trim(),
        ConfirmationSource.DiskNumber => $"DISCO{target.Id.Number.ToString(CultureInfo.InvariantCulture)}",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    public static string ModelIdentifier(string model) => new(model.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Indica si la confirmación corresponde al disco indicado, sin distinguir mayúsculas.</summary>
    public bool Matches(Disk target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Disk == target.Id
            && string.Equals(TypedSuffix.Trim(), ExpectedSuffix(target), StringComparison.OrdinalIgnoreCase);
    }
}

public enum ConfirmationSource
{
    SerialNumber,
    DiskGuid,
    Model,
    DiskNumber,
}
