using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Application.Tests.Fakes;
using System.Security.Cryptography;

namespace WinClonerX.Application.Tests.TestData;

/// <summary>
/// Fake de IVolumeCopier que copia de verdad: lee cada tramo ocupado del origen y lo escribe en el destino en fragmentos de
/// <see cref="ExecuteCloneTestData.ChunkSize"/>. Informa del total escrito dentro de esta llamada, como indica el contrato.
/// <see cref="OnChunk"/> permite a las pruebas simular el paso del tiempo o solicitar una cancelación entre fragmentos.
/// </summary>
internal sealed class FakeVolumeCopier : FakeBase, IVolumeCopier
{
    /// <summary>Copias solicitadas, en orden: origen, destino y desplazamiento de destino de cada una.</summary>
    public List<(IBlockSource Source, ITargetDisk Target, long TargetOffset)> Copies { get; } = [];

    /// <summary>Acción que se ejecuta después de escribir cada fragmento.</summary>
    public Action? OnChunk { get; set; }

    public int DigestSinkCalls { get; private set; }

    /// <summary>Copia los tramos ocupados del origen al destino en el desplazamiento indicado.</summary>
    public async Task CopyAsync(
        IBlockSource source,
        VolumeAllocation allocation,
        ITargetDisk target,
        long targetOffset,
        IProgress<long> bytesWritten,
        CancellationToken ct,
        ICopyDigestSink? digests = null)
    {
        Calls.Add(nameof(CopyAsync));
        Copies.Add((source, target, targetOffset));
        if (digests is not null)
        {
            DigestSinkCalls++;
        }

        if (FailOn.TryGetValue(nameof(CopyAsync), out var failure))
        {
            throw failure;
        }

        var buffer = new byte[ExecuteCloneTestData.ChunkSize];
        long written = 0;
        foreach (var range in allocation.UsedRanges)
        {
            for (var position = range.Offset; position < range.End; position += ExecuteCloneTestData.ChunkSize)
            {
                ct.ThrowIfCancellationRequested();

                var length = (int)Math.Min(ExecuteCloneTestData.ChunkSize, range.End - position);
                var read = await source.ReadAsync(position, buffer.AsMemory(0, length), ct);
                await target.WriteAsync(targetOffset + position, buffer.AsMemory(0, read), ct);
                digests?.Add(new ChunkDigest(targetOffset + position, read, Sha256Digest.FromBytes(SHA256.HashData(buffer.AsSpan(0, read)))));

                written += read;
                bytesWritten.Report(written);
                OnChunk?.Invoke();
            }
        }
    }
}
