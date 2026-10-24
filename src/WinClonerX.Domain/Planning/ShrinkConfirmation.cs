using WinClonerX.Domain.Shared;

namespace WinClonerX.Domain.Planning;

/// <summary>
/// Confirmación separada de la del destino que autoriza reducir la partición de Windows del origen (FR-013): identifica la
/// partición y la longitud reducida que la persona ha visto.
/// </summary>
public sealed record ShrinkConfirmation(PartitionGuid Partition, ByteSize ShrunkLength, DateTimeOffset At);
