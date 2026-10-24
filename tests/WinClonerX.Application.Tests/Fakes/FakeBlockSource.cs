using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Fake de IBlockSource que sirve bytes de un arreglo en memoria y una asignación configurable.</summary>
public sealed class FakeBlockSource : FakeBase, IBlockSource
{
    private const int DefaultClusterSize = 4096;
    private readonly byte[] _data;

    /// <summary>Crea un origen sobre los datos indicados. Sin asignación explícita, todo el volumen se considera ocupado.</summary>
    public FakeBlockSource(byte[] data, VolumeAllocation? allocation = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        Allocation = allocation ?? DefaultAllocation(data.Length);
    }

    /// <summary>Asignación que devuelve GetAllocationAsync.</summary>
    public VolumeAllocation Allocation { get; set; }

    /// <summary>Indica si el origen se ha dispuesto.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Longitud de los datos en bytes.</summary>
    public ByteSize Length => new(_data.Length);

    /// <summary>Devuelve la asignación configurada.</summary>
    public Task<VolumeAllocation> GetAllocationAsync(CancellationToken ct) =>
        Reply(nameof(GetAllocationAsync), () => Allocation, ct);

    /// <summary>Copia bytes desde el desplazamiento indicado; al llegar al final devuelve 0.</summary>
    public ValueTask<int> ReadAsync(long offset, Memory<byte> alignedBuffer, CancellationToken ct)
    {
        if (IsDisposed)
        {
            Calls.Add(nameof(ReadAsync));
            return new ValueTask<int>(Task.FromException<int>(new ObjectDisposedException(nameof(FakeBlockSource))));
        }

        return ReplyValue(nameof(ReadAsync), () => CopyTo(offset, alignedBuffer), ct);
    }

    /// <summary>Marca el origen como dispuesto.</summary>
    public void Dispose()
    {
        Calls.Add(nameof(Dispose));
        IsDisposed = true;
    }

    private int CopyTo(long offset, Memory<byte> destination)
    {
        if (offset < 0 || offset >= _data.Length)
        {
            return 0;
        }

        var count = (int)Math.Min(destination.Length, _data.Length - offset);
        _data.AsSpan((int)offset, count).CopyTo(destination.Span);
        return count;
    }

    private static VolumeAllocation DefaultAllocation(int length)
    {
        var clusterSize = new ByteSize(DefaultClusterSize);
        if (length == 0)
        {
            return new VolumeAllocation(clusterSize, []);
        }

        return new VolumeAllocation(clusterSize, [new Extent(0, length)]);
    }
}
