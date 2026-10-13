using WinCopy.Application.Ports;
using WinCopy.Domain.Inventory;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de IDiskInventoryReader con inventario configurable.</summary>
public sealed class FakeDiskInventoryReader : FakeBase, IDiskInventoryReader
{
    /// <summary>Inventario que devuelve ReadAsync.</summary>
    public DiskInventory Inventory { get; set; } = new(FakeDefaults.Now, FirmwareType.Uefi, []);

    /// <summary>Devuelve el inventario configurado.</summary>
    public Task<DiskInventory> ReadAsync(CancellationToken ct) =>
        Reply(nameof(ReadAsync), () => Inventory, ct);
}
