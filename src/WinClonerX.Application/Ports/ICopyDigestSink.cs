using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Application.Ports;

/// <summary>Recibe los digests de los chunks después de escribirlos en el destino.</summary>
public interface ICopyDigestSink
{
    void Add(ChunkDigest digest);
}
