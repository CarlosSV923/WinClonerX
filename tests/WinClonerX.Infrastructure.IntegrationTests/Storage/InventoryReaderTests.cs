using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.IntegrationTests.Fixtures;
using WinClonerX.Infrastructure.IntegrationTests.Spikes;
using WinClonerX.Infrastructure.Windows;
using Xunit;

namespace WinClonerX.Infrastructure.IntegrationTests.Storage;

/// <summary>
/// Task T042. The real <see cref="IDiskInventoryReader"/> (no fixture decorator) reads a reference system disk on a 2 GiB VHDX.
/// The reader is read-only, so nothing is written by this test.
/// Needs an elevated process:
/// <c>dotnet test tests/WinClonerX.Infrastructure.IntegrationTests --filter "FullyQualifiedName~InventoryReaderTests"</c>
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationDisks.Name)]
public sealed class InventoryReaderTests
{
    private static readonly ByteSize DiskSize = ByteSize.FromGiB(2);
    private const long TwoGiBBytes = 2L << 30;

    [Fact]
    public async Task RealReaderListsReferenceVhdxWithItsGptLayout()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        VirtualDiskStorageOps ops = VirtualDiskStorageOps.Default;

        await using var fixture = new VirtualDiskFixture();
        ReferenceDisk reference = await ReferenceSystemDisk.CreateAsync(fixture, ops, DiskSize, dataFiles: 0);

        string logDirectory = Path.Combine(fixture.RootFolder, "logs");
        IDiskInventoryReader reader = CreateRealReader(logDirectory);

        DiskInventory inventory = await reader.ReadAsync(ct);

        // The reference disk, as the real reader reports it.
        Disk disk = inventory.Disks
            .SingleOrDefault(candidate => candidate.Id.Number == reference.Disk.DiskNumber)
            .ShouldNotBeNull($"The real inventory must list disk {reference.Disk.DiskNumber}.");

        disk.Bus.ShouldBe(BusKind.FileBackedVirtual);
        disk.PartitionStyle.ShouldBe(PartitionStyle.Gpt);
        disk.Size.Bytes.ShouldBe(TwoGiBBytes);
        disk.IsSystem.ShouldBeFalse("The reference disk is not the running Windows.");

        // The table order is not fixed (Initialize-Disk may place the MSR first), so each partition is matched by its GPT unique id.
        disk.Partitions.Count.ShouldBe(reference.Layout.Count);

        foreach (ListedPartition expected in reference.Layout)
        {
            Partition partition = disk.Partitions
                .SingleOrDefault(candidate => candidate.UniqueId.Value == expected.Partition.GptGuid)
                .ShouldNotBeNull($"the real reader must list the partition {expected.Partition.GptGuid:D}");

            partition.Number.ShouldBe(expected.Partition.PartitionNumber, "partition number");
            partition.Extent.Offset.ShouldBe(expected.Partition.OffsetBytes, "partition offset");
            partition.Extent.Length.ShouldBe(expected.Partition.SizeBytes, "partition length");
            partition.Type.Value.ShouldBe(expected.GptType, "GPT type");

            PartitionRole expectedRole = RoleOf(expected.GptType);
            partition.Role.ShouldBe(expectedRole, $"role of partition {partition.Number}");

            // FAT32 on the ESP, NTFS on the Windows data partition and on Recovery, no volume on the MSR.
            FileSystemKind? expectedFileSystem = expectedRole switch
            {
                PartitionRole.EfiSystem => FileSystemKind.Fat32,
                PartitionRole.Data or PartitionRole.Recovery => FileSystemKind.Ntfs,
                _ => null,
            };

            if (expectedFileSystem is { } fileSystem)
            {
                partition.Volume.ShouldNotBeNull($"the volume of partition {partition.Number}").FileSystem.ShouldBe(fileSystem);
            }
            else
            {
                partition.Volume.ShouldBeNull($"partition {partition.Number} has no volume");
            }
        }

        // The running system disk is present: exactly one system disk, and it is not the VHDX.
        Disk[] systemDisks = inventory.Disks.Where(candidate => candidate.IsSystem).ToArray();
        systemDisks.Length.ShouldBe(1, "exactly one disk must be the system disk");
        systemDisks[0].Id.Number.ShouldNotBe(reference.Disk.DiskNumber, "the system disk must not be the reference VHDX");
    }

    // The real reader reports the "Windows" partition as Data, because the reference disk is not the running Windows.
    private static PartitionRole RoleOf(Guid gptType) =>
        gptType == ReferenceSystemDisk.EspType ? PartitionRole.EfiSystem
        : gptType == ReferenceSystemDisk.MsrType ? PartitionRole.MsReserved
        : gptType == ReferenceSystemDisk.BasicDataType ? PartitionRole.Data
        : gptType == ReferenceSystemDisk.RecoveryType ? PartitionRole.Recovery
        : throw new InvalidOperationException($"Unexpected reference partition type {gptType:B}.");

    private static IDiskInventoryReader CreateRealReader(string logDirectory)
    {
        // Same composition as the harness, with no overrides. Only the reader is resolved.
        using var provider = new ServiceCollection()
            .AddWinClonerXInfrastructure(options => options.LogDirectory = logDirectory)
            .BuildServiceProvider();

        return provider.GetRequiredService<IDiskInventoryReader>();
    }
}
