using WinClonerX.Application.Ports;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Fake de IRawPartitionReader que sirve el mismo contenido para cualquier tramo pedido.</summary>
public sealed class FakeRawPartitionReader : FakeBase, IRawPartitionReader
{
    /// <summary>Contenido de cada origen abierto. No se recorta al tramo pedido.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>Tramos abiertos, en orden.</summary>
    public List<(DiskId Source, Extent Extent)> Opened { get; } = [];

    /// <summary>Crea un origen sobre Data y registra el tramo pedido.</summary>
    public IBlockSource Open(DiskId source, Extent extent)
    {
        Enter(nameof(Open));
        Opened.Add((source, extent));
        return new FakeBlockSource(Data);
    }
}
