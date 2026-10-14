using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Tests.TestData;

/// <summary>
/// Constructores de inventarios de prueba para los casos de uso de planificación: un disco del sistema típico de
/// Windows 11 (ESP, MSR, Windows y Recovery al final) y discos de destino de distintos tamaños.
/// Copia adaptada de los datos de prueba del dominio; la capa de aplicación no referencia el proyecto de pruebas del dominio.
/// </summary>
internal static class PlanningTestData
{
    public const long Mib = 1024L * 1024;
    public const long Gib = 1024L * 1024 * 1024;

    /// <summary>Recovery típica de Windows 11: 750 MiB.</summary>
    public const long RecoveryLength = 750 * Mib;

    /// <summary>Primer byte de la partición de Windows en el disco típico (ESP 1..101 MiB, MSR 101..117 MiB).</summary>
    public const long WindowsStart = 117 * Mib;

    public static readonly DateTimeOffset CapturedAt = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public static readonly DiskId SourceId = new(0, G(100));
    public static readonly DiskId TargetId = new(1, G(200));
    public static readonly DiskId OtherTargetId = new(2, G(300));

    /// <summary>GUID de prueba determinista.</summary>
    public static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    /// <summary>Identificador de partición del disco de origen con el número indicado.</summary>
    public static PartitionGuid SourcePartitionGuid(int number) => new(G(1000 + number));

    /// <summary>Generador determinista de GUID nuevos: devuelve G(start), G(start+1), ...</summary>
    public static Func<Guid> SequentialGuids(int start = 5000)
    {
        var next = start - 1;
        return () => G(++next);
    }

    /// <summary>Disco del sistema típico: ESP 100 MiB, MSR 16 MiB, Windows NTFS, [Datos], Recovery 750 MiB al final.</summary>
    public static Disk SystemDisk(
        long size = 256 * Gib,
        long windowsUsed = 40 * Gib)
    {
        var recoveryOffset = size - Mib - RecoveryLength;
        var windowsLength = recoveryOffset - WindowsStart;

        var partitions = new List<Partition>
        {
            new(1, new Extent(Mib, 100 * Mib), GptPartitionType.EfiSystem, SourcePartitionGuid(1), 0UL, "EFI",
                new Volume(FileSystemKind.Fat32, ByteSize.FromMiB(100), ByteSize.FromMiB(30), null, EncryptionState.None, false, null)),
            new(2, new Extent(101 * Mib, 16 * Mib), GptPartitionType.MicrosoftReserved, SourcePartitionGuid(2), 0UL, "MSR", null),
            new(3, new Extent(WindowsStart, windowsLength), GptPartitionType.BasicData, SourcePartitionGuid(3), 0UL, "Windows",
                new Volume(FileSystemKind.Ntfs, new ByteSize(windowsLength), new ByteSize(windowsUsed), new ByteSize(4096),
                    EncryptionState.None, true, null)),
            new(4, new Extent(recoveryOffset, RecoveryLength), GptPartitionType.WindowsRecovery, SourcePartitionGuid(4), 0UL, "Recovery",
                new Volume(FileSystemKind.Ntfs, ByteSize.FromMiB(750), ByteSize.FromMiB(450), new ByteSize(4096),
                    EncryptionState.None, false, null)),
        };

        return new Disk(SourceId, "Modelo sistema", "SYS-0001", new ByteSize(size), new SectorSize(512),
            SectorSize.Bytes4096, BusKind.Nvme, PartitionStyle.Gpt, true, partitions, IsOffline: false, IsReadOnly: false);
    }

    /// <summary>Disco de destino sin particiones (o con una partición de datos si se pide).</summary>
    public static Disk TargetDisk(
        long size = 512 * Gib,
        string serial = "TGT-0002",
        bool withDataPartition = false,
        DiskId? id = null)
    {
        var partitions = new List<Partition>();
        if (withDataPartition)
        {
            partitions.Add(new Partition(1, new Extent(Mib, 10 * Gib), GptPartitionType.BasicData, new PartitionGuid(G(3001)),
                0UL, "Datos previos",
                new Volume(FileSystemKind.Ntfs, new ByteSize(10 * Gib), ByteSize.FromGiB(1), new ByteSize(4096),
                    EncryptionState.None, false, null)));
        }

        return new Disk(id ?? TargetId, "Modelo destino", serial, new ByteSize(size), new SectorSize(512),
            SectorSize.Bytes4096, BusKind.Usb, PartitionStyle.Gpt, false, partitions, IsOffline: false, IsReadOnly: false);
    }

    /// <summary>Disco de destino demasiado pequeño para la partición de Windows del origen (128 GiB).</summary>
    public static Disk SmallTarget() => TargetDisk(size: 128 * Gib, serial: "SMALL-0003", id: OtherTargetId);

    /// <summary>Inventario UEFI con los discos indicados.</summary>
    public static DiskInventory InventoryOf(params Disk[] disks) => new(CapturedAt, FirmwareType.Uefi, disks);

    /// <summary>Hechos por defecto: sin BitLocker, con corriente, instantánea disponible con 20 GiB y ambos destinos libres.</summary>
    public static PlatformFacts Facts() => new(
        new Dictionary<PartitionGuid, EncryptionState>(),
        OnBattery: false,
        BatteryPercent: 100,
        SnapshotAvailable: true,
        SnapshotFreeSpace: ByteSize.FromGiB(20),
        MinimumWindowsSize: null,
        TargetInUse: new Dictionary<DiskId, bool>
        {
            [TargetId] = false,
            [OtherTargetId] = false,
        });
}
