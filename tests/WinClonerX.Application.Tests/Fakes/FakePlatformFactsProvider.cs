using WinClonerX.Application.Ports;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Fake de IPlatformFactsProvider con hechos configurables y registro de los argumentos recibidos.</summary>
public sealed class FakePlatformFactsProvider : FakeBase, IPlatformFactsProvider
{
    /// <summary>Hechos que devuelve GetAsync.</summary>
    public PlatformFacts Facts { get; set; } = new(
        new Dictionary<PartitionGuid, EncryptionState>(),
        OnBattery: false,
        BatteryPercent: 100,
        SnapshotAvailable: true,
        SnapshotFreeSpace: ByteSize.FromGiB(20),
        MinimumWindowsSize: null,
        TargetInUse: new Dictionary<DiskId, bool>());

    /// <summary>Último inventario recibido.</summary>
    public DiskInventory? LastInventory { get; private set; }

    /// <summary>Guarda el inventario recibido y devuelve los hechos configurados.</summary>
    public Task<PlatformFacts> GetAsync(DiskInventory inventory, CancellationToken ct)
    {
        LastInventory = inventory;
        return Reply(nameof(GetAsync), () => Facts, ct);
    }
}
