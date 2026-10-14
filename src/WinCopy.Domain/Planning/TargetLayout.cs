using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Partición tal como debe quedar escrita en la tabla GPT del destino: tipo, identificador nuevo, extensión,
/// atributos y nombre. Los tres últimos se copian del origen.
/// </summary>
public sealed record TargetLayoutEntry(GptPartitionType Type, PartitionGuid UniqueId, Extent Extent, ulong Attributes, string Name);

/// <summary>
/// Diseño completo del disco de destino que se confirma con la tabla GPT (R4). Lo produce <see cref="ClonePlan.ToTargetLayout"/>.
/// </summary>
public sealed record TargetLayout(Guid DiskGuid, ByteSize DiskSize, SectorSize LogicalSectorSize, IReadOnlyList<TargetLayoutEntry> Entries);
