using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de IPlatformFactsProvider con hechos configurables y registro de los argumentos recibidos.</summary>
public sealed class FakePlatformFactsProvider : FakeBase, IPlatformFactsProvider
{
    /// <summary>Hechos que devuelve GetAsync.</summary>
    public PlatformFacts Facts { get; set; } = new(
        FirmwareType.Uefi,
        new Dictionary<PartitionGuid, EncryptionState>(),
        new PowerStatus(false, 100),
        true,
        ByteSize.FromGiB(20),
        null,
        false);

    /// <summary>Último inventario recibido.</summary>
    public DiskInventory? LastInventory { get; private set; }

    /// <summary>Último destino candidato recibido.</summary>
    public DiskId? LastCandidateTarget { get; private set; }

    /// <summary>Guarda los argumentos y devuelve los hechos configurados.</summary>
    public Task<PlatformFacts> GetAsync(DiskInventory inventory, DiskId? candidateTarget, CancellationToken ct)
    {
        LastInventory = inventory;
        LastCandidateTarget = candidateTarget;
        return Reply(nameof(GetAsync), () => Facts, ct);
    }
}
