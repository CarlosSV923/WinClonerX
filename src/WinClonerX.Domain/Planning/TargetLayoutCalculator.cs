using System.Globalization;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Domain.Planning;

/// <summary>
/// Calcula dónde quedan las particiones del origen en el destino. Función pura: no accede a ningún disco.
/// <see cref="Calculate"/> aplica la estrategia SameOrLarger; <see cref="Decide"/> añade la estrategia ShrinkSource cuando el
/// destino no admite el diseño igual o mayor (FR-021, FR-021b).
/// Reglas: las particiones anteriores a Windows conservan su desplazamiento; Windows empieza en el suyo; las posteriores
/// (Recovery, datos) se colocan al final del disco con su longitud y en su orden; Windows absorbe el espacio entre ambas.
/// Todos los desplazamientos están alineados a 1 MiB.
/// </summary>
public static class TargetLayoutCalculator
{
    /// <summary>Código de fallo cuando el destino no tiene espacio para que Windows conserve su tamaño (o su tamaño reducido).</summary>
    public const string TargetTooSmallCode = "TargetTooSmall";

    /// <summary>Código de fallo cuando ni reduciendo Windows cabe, y el mínimo que admite Windows es el término que manda.</summary>
    public const string ShrinkInsufficientCode = "ShrinkInsufficient";

    /// <summary>Código de fallo cuando el diseño de origen no se puede representar en el destino.</summary>
    public const string InvalidLayoutCode = "InvalidLayout";

    private const long AlignmentBytes = 1024L * 1024;

    /// <summary>Bytes que ocupan al final del disco las entradas GPT de respaldo (128 entradas de 128 bytes).</summary>
    private const long BackupEntriesBytes = 16384;

    private const long PrimaryGptSectors512 = 34;
    private const long PrimaryGptSectors4096 = 6;

    /// <summary>
    /// Calcula una extensión por partición de origen, en el mismo orden (estrategia SameOrLarger). Si Windows no conserva su
    /// longitud, falla con <see cref="TargetTooSmallCode"/> y, como detalle, los bytes que faltan.
    /// </summary>
    public static Result<IReadOnlyList<Extent>, DomainError> Calculate(Disk source, Disk target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var frame = ComputeFrame(source, target);
        if (frame is null)
        {
            return Fail<IReadOnlyList<Extent>>(InvalidLayoutCode, NoWindowsDetail);
        }

        var windowsLength = source.Partitions[frame.WindowsIndex].Extent.Length;
        if (frame.Available < windowsLength)
        {
            return Fail<IReadOnlyList<Extent>>(TargetTooSmallCode, (windowsLength - frame.Available).ToString(CultureInfo.InvariantCulture));
        }

        return Materialize(source, target, frame, frame.Available);
    }

    /// <summary>
    /// Decide el diseño del destino. Primero intenta SameOrLarger: Windows conserva su longitud y tiene el margen. Si no cabe,
    /// y el mínimo que admite Windows es conocido, intenta ShrinkSource: el mínimo y lo ocupado más el margen determinan
    /// el tamaño mínimo elegible; Windows se reduce temporalmente al espacio disponible para Windows en el destino, alineado
    /// hacia abajo a 1 MiB. Si ni siquiera cabe el tamaño mínimo elegible, falla con <see cref="ShrinkInsufficientCode"/> cuando el mínimo es el término que
    /// manda, o con <see cref="TargetTooSmallCode"/> en otro caso; en ambos, el detalle son los bytes que faltan. Sin mínimo
    /// conocido no se reduce y se informa <see cref="TargetTooSmallCode"/> como en la versión 1.
    /// </summary>
    public static Result<LayoutDecision, DomainError> Decide(Disk source, Disk target, CapacityPolicy policy, ByteSize? minimumWindowsSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(policy);

        var frame = ComputeFrame(source, target);
        if (frame is null)
        {
            return Fail<LayoutDecision>(InvalidLayoutCode, NoWindowsDetail);
        }

        var windows = source.Partitions[frame.WindowsIndex];
        var windowsLength = windows.Extent.Length;
        var used = windows.Volume?.Used ?? throw new InvalidOperationException("La partición de Windows del origen no tiene volumen.");
        var required = policy.Required(used).Bytes;
        var available = frame.Available;

        if (available >= windowsLength && available >= required)
        {
            return Decision(SizeStrategy.SameOrLarger, Materialize(source, target, frame, available), null);
        }

        if (minimumWindowsSize is not { } minimum)
        {
            var shortfall = available < windowsLength ? windowsLength - available : required - available;
            return Fail<LayoutDecision>(TargetTooSmallCode, shortfall.ToString(CultureInfo.InvariantCulture));
        }

        // El mínimo y lo ocupado más el margen solo determinan si la reducción es viable.
        var lower = Math.Max(AlignUp(Math.Max(minimum.Bytes, required)), AlignmentBytes);

        if (available >= lower)
        {
            // Reducir lo mínimo mueve menos datos, acelera la reducción y deja mucho margen frente a cambios del mínimo de Windows entre planificación y ejecución.
            var shrunk = available % AlignmentBytes == 0 ? available : AlignDown(available);
            // Aquí Windows del origen es mayor que el destino (available < windowsLength), así que la reducción es estrictamente menor.
            var step = new SourceShrinkStep(windows.UniqueId, new ByteSize(windowsLength), new ByteSize(shrunk), minimum);
            return Decision(SizeStrategy.ShrinkSource, Materialize(source, target, frame, available), step);
        }

        var missing = (lower - available).ToString(CultureInfo.InvariantCulture);
        var code = minimum.Bytes >= required ? ShrinkInsufficientCode : TargetTooSmallCode;

        return Fail<LayoutDecision>(code, missing);
    }

    /// <summary>Primer byte utilizable del destino: 34 sectores en 512 bytes o 6 en 4096, y nunca antes de 1 MiB.</summary>
    internal static long FirstUsableByte(SectorSize sector)
    {
        var gptSectors = sector.Bytes == 4096 ? PrimaryGptSectors4096 : PrimaryGptSectors512;
        return Math.Max(AlignmentBytes, gptSectors * sector.Bytes);
    }

    /// <summary>Límite exclusivo del área utilizable del destino: deja libres las entradas y la cabecera GPT de respaldo.</summary>
    internal static long LastUsableByte(Disk target) => target.Size.Bytes - (BackupEntriesBytes + target.LogicalSectorSize.Bytes);

    /// <summary>Alineación de las particiones del destino.</summary>
    internal static long Alignment => AlignmentBytes;

    private const string NoWindowsDetail = "Se necesita exactamente una partición de Windows en el origen.";

    /// <summary>Geometría común a SameOrLarger y ShrinkSource: dónde empieza Windows, dónde acaban las posteriores y cuánto espacio queda para Windows.</summary>
    private sealed record Frame(int WindowsIndex, long WindowsStart, long[] Starts, long FirstUsable, long LastUsable, long Available);

    /// <summary>Calcula la geometría, o null si el origen no tiene exactamente una partición de Windows.</summary>
    private static Frame? ComputeFrame(Disk source, Disk target)
    {
        var partitions = source.Partitions;
        var windowsIndexes = Enumerable.Range(0, partitions.Count).Where(i => partitions[i].Role == PartitionRole.Windows).ToArray();
        if (windowsIndexes.Length != 1)
        {
            return null;
        }

        var windowsIndex = windowsIndexes[0];
        var firstUsable = FirstUsableByte(target.LogicalSectorSize);
        var lastUsable = LastUsableByte(target);

        var windowsStart = AlignUp(Math.Max(partitions[windowsIndex].Extent.Offset, firstUsable));

        // Las particiones posteriores a Windows se colocan desde el final hacia atrás, sin cambiar su longitud.
        var starts = new long[partitions.Count];
        var cursor = lastUsable;
        for (var i = partitions.Count - 1; i > windowsIndex; i--)
        {
            starts[i] = AlignDown(cursor - partitions[i].Extent.Length);
            cursor = starts[i];
        }

        var windowsEnd = windowsIndex < partitions.Count - 1 ? starts[windowsIndex + 1] : AlignDown(lastUsable);
        var available = AlignDown(windowsEnd - windowsStart);

        return new Frame(windowsIndex, windowsStart, starts, firstUsable, lastUsable, available);
    }

    /// <summary>Extensiones con Windows de la longitud indicada; falla si alguna no cabe en orden dentro del área utilizable.</summary>
    private static Result<IReadOnlyList<Extent>, DomainError> Materialize(Disk source, Disk target, Frame frame, long windowsLength)
    {
        var partitions = source.Partitions;
        var sector = target.LogicalSectorSize;
        var extents = new Extent[partitions.Count];

        for (var i = 0; i < partitions.Count; i++)
        {
            var offset = i switch
            {
                _ when i < frame.WindowsIndex => AlignUp(Math.Max(partitions[i].Extent.Offset, frame.FirstUsable)),
                _ when i == frame.WindowsIndex => frame.WindowsStart,
                _ => frame.Starts[i],
            };
            var length = i == frame.WindowsIndex ? windowsLength : partitions[i].Extent.Length;

            extents[i] = Extent.Create(offset, length, sector);
            if (offset < frame.FirstUsable || extents[i].End > frame.LastUsable || (i > 0 && offset < extents[i - 1].End))
            {
                return Fail<IReadOnlyList<Extent>>(InvalidLayoutCode, "Las particiones del origen no caben en orden en el destino.");
            }
        }

        return Result<IReadOnlyList<Extent>, DomainError>.Success(extents);
    }

    private static Result<LayoutDecision, DomainError> Decision(
        SizeStrategy strategy,
        Result<IReadOnlyList<Extent>, DomainError> extents,
        SourceShrinkStep? shrink)
    {
        if (!extents.IsSuccess)
        {
            return Result<LayoutDecision, DomainError>.Failure(extents.Error);
        }

        return Result<LayoutDecision, DomainError>.Success(new LayoutDecision(strategy, extents.Value, shrink));
    }

    private static Result<T, DomainError> Fail<T>(string code, string? detail) =>
        Result<T, DomainError>.Failure(new DomainError(code, detail));

    private static long AlignUp(long value) => AlignDown(value + AlignmentBytes - 1);

    private static long AlignDown(long value)
    {
        var quotient = value / AlignmentBytes;
        if (value % AlignmentBytes < 0)
        {
            quotient--;
        }

        return quotient * AlignmentBytes;
    }
}
