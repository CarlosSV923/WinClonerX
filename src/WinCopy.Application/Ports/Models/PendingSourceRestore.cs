using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Ports.Models;

/// <summary>
/// Entrada del diario de restauración del origen: la partición de Windows que se va a reducir (o que ya está reducida), su
/// longitud original, el plan que la reduce y el instante en UTC en que se escribió la entrada.
/// </summary>
public sealed record PendingSourceRestore(PartitionGuid Partition, ByteSize OriginalLength, PlanId Plan, DateTimeOffset RecordedAt);
