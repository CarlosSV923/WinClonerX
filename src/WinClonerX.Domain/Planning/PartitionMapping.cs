using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Domain.Planning;

/// <summary>Correspondencia entre una partición de origen y su extensión, identificador nuevo y método de copia en el destino.</summary>
public sealed record PartitionMapping(
    Partition Source,
    Extent TargetExtent,
    PartitionGuid NewUniqueId,
    CopyMethod CopyMethod,
    ResizeKind Resize);
