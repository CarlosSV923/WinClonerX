using System.Globalization;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Calcula dónde quedan las particiones del origen en el destino (estrategia SameOrLarger).
/// Función pura: no accede a ningún disco.
/// Reglas: las particiones anteriores a Windows conservan su desplazamiento; Windows empieza en el suyo; las posteriores
/// (Recovery, datos) se colocan al final del disco con su longitud y en su orden; Windows absorbe el espacio entre ambas.
/// Todos los desplazamientos están alineados a 1 MiB.
/// </summary>
public static class TargetLayoutCalculator
{
    /// <summary>Código de fallo cuando el destino no tiene espacio para que Windows conserve su tamaño.</summary>
    public const string TargetTooSmallCode = "TargetTooSmall";

    /// <summary>Código de fallo cuando el diseño de origen no se puede representar en el destino.</summary>
    public const string InvalidLayoutCode = "InvalidLayout";

    private const long AlignmentBytes = 1024L * 1024;

    /// <summary>Bytes que ocupan al final del disco las entradas GPT de respaldo (128 entradas de 128 bytes).</summary>
    private const long BackupEntriesBytes = 16384;

    private const long PrimaryGptSectors512 = 34;
    private const long PrimaryGptSectors4096 = 6;

    /// <summary>
    /// Calcula una extensión por partición de origen, en el mismo orden. Si Windows no conserva su longitud,
    /// falla con <see cref="TargetTooSmallCode"/> y, como detalle, los bytes que faltan.
    /// </summary>
    public static Result<IReadOnlyList<Extent>, DomainError> Calculate(Disk source, Disk target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var partitions = source.Partitions;
        var windowsIndexes = Enumerable.Range(0, partitions.Count).Where(i => partitions[i].Role == PartitionRole.Windows).ToArray();
        if (windowsIndexes.Length != 1)
        {
            return Result<IReadOnlyList<Extent>, DomainError>.Failure(
                new DomainError(InvalidLayoutCode, "Se necesita exactamente una partición de Windows en el origen."));
        }

        var windowsIndex = windowsIndexes[0];
        var sector = target.LogicalSectorSize;
        var firstUsable = FirstUsableByte(sector);
        var lastUsable = LastUsableByte(target);

        var windows = partitions[windowsIndex].Extent;
        var windowsStart = AlignUp(Math.Max(windows.Offset, firstUsable));

        // Las particiones posteriores a Windows se colocan desde el final hacia atrás, sin cambiar su longitud.
        var starts = new long[partitions.Count];
        var cursor = lastUsable;
        for (var i = partitions.Count - 1; i > windowsIndex; i--)
        {
            starts[i] = AlignDown(cursor - partitions[i].Extent.Length);
            cursor = starts[i];
        }

        var windowsEnd = windowsIndex < partitions.Count - 1 ? starts[windowsIndex + 1] : AlignDown(lastUsable);
        var windowsLength = AlignDown(windowsEnd - windowsStart);
        if (windowsLength < windows.Length)
        {
            var shortfall = windows.Length - windowsLength;
            return Result<IReadOnlyList<Extent>, DomainError>.Failure(
                new DomainError(TargetTooSmallCode, shortfall.ToString(CultureInfo.InvariantCulture)));
        }

        var extents = new Extent[partitions.Count];
        for (var i = 0; i < partitions.Count; i++)
        {
            var offset = i switch
            {
                _ when i < windowsIndex => AlignUp(Math.Max(partitions[i].Extent.Offset, firstUsable)),
                _ when i == windowsIndex => windowsStart,
                _ => starts[i],
            };
            var length = i == windowsIndex ? windowsLength : partitions[i].Extent.Length;

            extents[i] = Extent.Create(offset, length, sector);
            if (offset < firstUsable || extents[i].End > lastUsable || (i > 0 && offset < extents[i - 1].End))
            {
                return Result<IReadOnlyList<Extent>, DomainError>.Failure(
                    new DomainError(InvalidLayoutCode, "Las particiones del origen no caben en orden en el destino."));
            }
        }

        return Result<IReadOnlyList<Extent>, DomainError>.Success(extents);
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
