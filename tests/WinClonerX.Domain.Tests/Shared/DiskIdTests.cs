using Shouldly;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;
using Xunit;

#pragma warning disable CA1716 // El espacio de pruebas refleja la carpeta Shared de la especificación.
namespace WinClonerX.Domain.Tests.Shared;
#pragma warning restore CA1716

public sealed class DiskIdTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameGuidWithDifferentNumbersIsEqual()
    {
        var guid = Id(7);
        var first = new DiskId(1, guid);
        var second = new DiskId(9, guid);

        (first == second).ShouldBeTrue();
        (first != second).ShouldBeFalse();
        first.Equals(second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void DifferentGuidsAreNotEqualEvenWithDifferentNumbers()
    {
        (new DiskId(1, Id(1)) == new DiskId(2, Id(2))).ShouldBeFalse();
        (new DiskId(1, Id(1)) != new DiskId(2, Id(2))).ShouldBeTrue();
    }

    [Fact]
    public void TwoEmptyGuidsWithDifferentNumbersAreNotEqual()
    {
        var first = new DiskId(1, Guid.Empty);
        var second = new DiskId(2, Guid.Empty);

        (first == second).ShouldBeFalse();
        (first != second).ShouldBeTrue();
        first.Equals(second).ShouldBeFalse();
    }

    [Fact]
    public void TwoEmptyGuidsWithTheSameNumberAreEqual()
    {
        var first = new DiskId(3, Guid.Empty);
        var second = new DiskId(3, Guid.Empty);

        (first == second).ShouldBeTrue();
        first.Equals(second).ShouldBeTrue();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void EmptyGuidIsNeverEqualToANonEmptyGuid()
    {
        var empty = new DiskId(1, Guid.Empty);
        var withGuid = new DiskId(1, Id(1));

        (empty == withGuid).ShouldBeFalse();
        (withGuid == empty).ShouldBeFalse();
        (empty != withGuid).ShouldBeTrue();
        empty.Equals(withGuid).ShouldBeFalse();
        withGuid.Equals(empty).ShouldBeFalse();
    }

    [Fact]
    public void EmptyGuidIsNotEqualToANonEmptyGuidWithTheSameNumber()
    {
        (new DiskId(1, Guid.Empty) == new DiskId(1, Id(1))).ShouldBeFalse();
    }

    [Fact]
    public void HashSetHoldsTwoRawDisksWithDifferentNumbers()
    {
        var set = new HashSet<DiskId>
        {
            new(1, Guid.Empty),
            new(2, Guid.Empty),
        };

        set.Count.ShouldBe(2);
    }

    [Fact]
    public void HashSetHoldsOneEntryForTheSameGuidWithDifferentNumbers()
    {
        var set = new HashSet<DiskId>
        {
            new(1, Id(1)),
            new(5, Id(1)),
        };

        set.Count.ShouldBe(1);
    }

    [Fact]
    public void FindDiskReturnsTheRightRawDiskForEachId()
    {
        var first = RawDisk(new DiskId(1, Guid.Empty), "RAW-1");
        var second = RawDisk(new DiskId(2, Guid.Empty), "RAW-2");
        var inventory = new DiskInventory(CapturedAt, FirmwareType.Uefi, [first, second]);

        inventory.FindDisk(new DiskId(1, Guid.Empty)).ShouldBe(first);
        inventory.FindDisk(new DiskId(2, Guid.Empty)).ShouldBe(second);
        inventory.FindDisk(new DiskId(3, Guid.Empty)).ShouldBeNull();
    }

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static Disk RawDisk(DiskId id, string serial) =>
        new(id, "Disco RAW de prueba", serial, ByteSize.FromGiB(256), SectorSize.Bytes512, SectorSize.Bytes4096,
            BusKind.Usb, PartitionStyle.Raw, false, [], IsOffline: false, IsReadOnly: false);
}
