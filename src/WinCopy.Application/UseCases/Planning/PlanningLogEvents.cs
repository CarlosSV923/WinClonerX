using System.Globalization;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.UseCases.Planning;

/// <summary>
/// Construye los eventos de planificación del registro de operación (contracts/log-events.md). Los valores son
/// primitivos, arrays o diccionarios de primitivos, para que se serialicen igual en CLEF y en inventory.json.
/// Los identificadores de disco y de plan se escriben como GUID en formato "D".
/// </summary>
internal static class PlanningLogEvents
{
    /// <summary>Evento de inventario capturado: firmware y, por cada disco, id, modelo, serie, tamaño, bus y número de particiones.</summary>
    public static LogEvent InventoryCaptured(DiskInventory inventory) => new(
        "InventoryCaptured",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Firmware"] = inventory.Firmware.ToString(),
            ["Disks"] = inventory.Disks.Select(DiskEntry).ToArray(),
        });

    /// <summary>Evento de comprobaciones: las globales y las causas de cada destino, indicando a qué disco se refieren.</summary>
    public static LogEvent PreflightEvaluated(PreflightReport global, IEnumerable<TargetEligibility> targets)
    {
        var checks = global.Checks.Select(check => CheckEntry(check, null))
            .Concat(targets.SelectMany(target => target.Reasons.Select(reason => CheckEntry(reason, target.Disk))))
            .ToArray();

        return new LogEvent(
            "PreflightEvaluated",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["Checks"] = checks });
    }

    /// <summary>Evento de plan calculado: origen, destino, correspondencias, verificación y bytes a copiar.</summary>
    public static LogEvent PlanCreated(ClonePlan plan) => new(
        "PlanCreated",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PlanId"] = Text(plan.Id.Value),
            ["Source"] = Text(plan.Source.DiskGuid),
            ["Target"] = Text(plan.Target.DiskGuid),
            ["Mappings"] = plan.Mappings.Select(MappingEntry).ToArray(),
            ["Verification"] = plan.Verification.ToString(),
            ["EstimateBytes"] = plan.Estimate.BytesToCopy.Bytes,
        });

    /// <summary>Evento de plan confirmado: avisos aceptados y si se autorizó la reducción del origen (FR-013).</summary>
    public static LogEvent PlanConfirmed(ClonePlan plan) => new(
        "PlanConfirmed",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PlanId"] = Text(plan.Id.Value),
            ["AcceptedWarnings"] = plan.AcceptedWarnings
                .Select(code => code.ToString())
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray(),
            ["ShrinkConfirmed"] = plan.SourceShrinkConfirmation is not null,
        });

    /// <summary>Evento de plan invalidado porque cambió el estado de los discos entre la planificación y la confirmación.</summary>
    public static LogEvent PlanInvalidated(PlanId id, string reason) => new(
        "PlanInvalidated",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PlanId"] = Text(id.Value),
            ["Reason"] = reason,
        },
        LogLevelKind.Warning);

    private static Dictionary<string, object?> DiskEntry(Disk disk) => new(StringComparer.Ordinal)
    {
        ["Id"] = Text(disk.Id.DiskGuid),
        ["Number"] = disk.Id.Number,
        ["Model"] = disk.Model,
        ["Serial"] = disk.SerialNumber,
        ["SizeBytes"] = disk.Size.Bytes,
        ["Bus"] = disk.Bus.ToString(),
        ["Partitions"] = disk.Partitions.Count,
        ["IsSystem"] = disk.IsSystem,
    };

    private static Dictionary<string, object?> CheckEntry(PreflightCheck check, DiskId? target) => new(StringComparer.Ordinal)
    {
        ["Code"] = check.Code.ToString(),
        ["Severity"] = check.Severity.ToString(),
        ["Facts"] = check.Facts.ToDictionary(pair => pair.Key, pair => FactValue(pair.Value), StringComparer.Ordinal),
        ["Target"] = target is { } id ? Text(id.DiskGuid) : null,
    };

    private static Dictionary<string, object?> MappingEntry(PartitionMapping mapping) => new(StringComparer.Ordinal)
    {
        ["Number"] = mapping.Source.Number,
        ["Role"] = mapping.Source.Role.ToString(),
        ["CopyMethod"] = mapping.CopyMethod.ToString(),
        ["Resize"] = mapping.Resize.ToString(),
        ["SourceOffset"] = mapping.Source.Extent.Offset,
        ["SourceLength"] = mapping.Source.Extent.Length,
        ["TargetOffset"] = mapping.TargetExtent.Offset,
        ["TargetLength"] = mapping.TargetExtent.Length,
        ["NewUniqueId"] = Text(mapping.NewUniqueId.Value),
    };

    /// <summary>Convierte un hecho del dominio a un valor serializable: los tamaños a bytes y los enums a texto.</summary>
    private static object? FactValue(object value) => value switch
    {
        ByteSize size => size.Bytes,
        Enum enumValue => enumValue.ToString(),
        _ => value,
    };

    private static string Text(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
}
