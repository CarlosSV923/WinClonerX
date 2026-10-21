using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Application.Tests.Fakes;

namespace WinCopy.Application.Tests.TestData;

/// <summary>
/// Fake de IVolumeComparer. Recorre los tramos ocupados en fragmentos de <see cref="ExecuteCloneTestData.ChunkSize"/>, informa
/// del avance y comprueba la cancelación entre fragmentos, como el adaptador real. No lee datos: el resultado por defecto no
/// tiene diferencias, y <see cref="Overrides"/> inyecta el resultado de una correspondencia, identificada por su desplazamiento
/// de destino. <see cref="OnChunk"/> permite simular una cancelación a mitad de la comparación.
/// </summary>
internal sealed class FakeVolumeComparer : FakeBase, IVolumeComparer
{
    /// <summary>Comparaciones solicitadas, en orden: origen, destino, desplazamiento de destino y tramos.</summary>
    public List<(IBlockSource Source, ITargetDisk Target, long TargetOffset, VolumeAllocation Allocation)> Comparisons { get; } = [];

    /// <summary>Resultado a devolver para la correspondencia cuyo desplazamiento de destino es la clave.</summary>
    public Dictionary<long, RangeComparison> Overrides { get; } = [];

    /// <summary>Acción que se ejecuta después de cada fragmento comparado.</summary>
    public Action? OnChunk { get; set; }

    /// <summary>Compara los tramos ocupados y devuelve el resultado configurado, o el de no diferencias.</summary>
    public async Task<RangeComparison> CompareAsync(
        IBlockSource source,
        VolumeAllocation allocation,
        ITargetDisk target,
        long targetOffset,
        IProgress<long> bytesCompared,
        CancellationToken ct)
    {
        Calls.Add(nameof(CompareAsync));
        Comparisons.Add((source, target, targetOffset, allocation));

        if (FailOn.TryGetValue(nameof(CompareAsync), out var failure))
        {
            throw failure;
        }

        long compared = 0;
        foreach (var range in allocation.UsedRanges)
        {
            for (var position = range.Offset; position < range.End; position += ExecuteCloneTestData.ChunkSize)
            {
                ct.ThrowIfCancellationRequested();

                compared += Math.Min(ExecuteCloneTestData.ChunkSize, range.End - position);
                bytesCompared.Report(compared);
                OnChunk?.Invoke();
            }
        }

        await Task.Yield();

        return Overrides.TryGetValue(targetOffset, out var configured)
            ? configured
            : new RangeComparison(compared, 0, []);
    }
}
