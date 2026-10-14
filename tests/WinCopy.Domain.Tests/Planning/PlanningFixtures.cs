using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Tests.Planning;

/// <summary>
/// Constructores de inventarios de prueba para la planificación: un disco del sistema típico de Windows 11
/// (ESP, MSR, Windows y Recovery al final) y discos de destino de distintos tamaños.
/// </summary>
internal static class PlanningFixtures
{
    public const long Mib = 1024L * 1024;
    public const long Gib = 1024L * 1024 * 1024;

    /// <summary>Recovery típica de Windows 11: 750 MiB.</summary>
    public const long RecoveryLength = 750 * Mib;

    /// <summary>Primer byte de la partición de Windows en el disco típico (ESP 1..101 MiB, MSR 101..117 MiB).</summary>
    public const long WindowsStart = 117 * Mib;

    /// <summary>Bytes que ocupan la cabecera de respaldo y las entradas GPT al final del disco (16 KiB de entradas).</summary>
    public const long GptBackupEntriesBytes = 16384;

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
    /// <param name="size">Capacidad del disco.</param>
    /// <param name="sector">Tamaño de sector lógico (512 o 4096).</param>
    /// <param name="windowsUsed">Espacio ocupado por Windows.</param>
    /// <param name="dataLength">Si es mayor que cero, añade una partición de datos entre Windows y Recovery.</param>
    /// <param name="windowsEncryption">Cifrado del volumen de Windows.</param>
    public static Disk SystemDisk(
        long size = 256 * Gib,
        int sector = 512,
        long windowsUsed = 40 * Gib,
        long dataLength = 0,
        EncryptionState windowsEncryption = EncryptionState.None)
    {
        var recoveryOffset = size - Mib - RecoveryLength;
        var dataOffset = recoveryOffset - dataLength;
        var windowsLength = dataOffset - WindowsStart;

        var partitions = new List<Partition>
        {
            new(1, new Extent(Mib, 100 * Mib), GptPartitionType.EfiSystem, SourcePartitionGuid(1), 0UL, "EFI",
                new Volume(FileSystemKind.Fat32, ByteSize.FromMiB(100), ByteSize.FromMiB(30), null, EncryptionState.None, false, null)),
            new(2, new Extent(101 * Mib, 16 * Mib), GptPartitionType.MicrosoftReserved, SourcePartitionGuid(2), 0UL, "MSR", null),
            new(3, new Extent(WindowsStart, windowsLength), GptPartitionType.BasicData, SourcePartitionGuid(3), 0UL, "Windows",
                new Volume(FileSystemKind.Ntfs, new ByteSize(windowsLength), new ByteSize(windowsUsed), new ByteSize(4096),
                    windowsEncryption, true, null)),
        };

        var number = 4;
        if (dataLength > 0)
        {
            partitions.Add(new Partition(number, new Extent(dataOffset, dataLength), GptPartitionType.BasicData,
                SourcePartitionGuid(number), 0UL, "Datos",
                new Volume(FileSystemKind.Ntfs, new ByteSize(dataLength), new ByteSize(Math.Min(10 * Gib, dataLength)),
                    new ByteSize(4096), EncryptionState.None, false, null)));
            number++;
        }

        partitions.Add(new Partition(number, new Extent(recoveryOffset, RecoveryLength), GptPartitionType.WindowsRecovery,
            SourcePartitionGuid(number), 0UL, "Recovery",
            new Volume(FileSystemKind.Ntfs, ByteSize.FromMiB(750), ByteSize.FromMiB(450), new ByteSize(4096),
                EncryptionState.None, false, null)));

        return new Disk(SourceId, "Modelo sistema", "SYS-0001", new ByteSize(size), new SectorSize(sector),
            SectorSize.Bytes4096, BusKind.Nvme, PartitionStyle.Gpt, true, partitions, IsOffline: false, IsReadOnly: false);
    }

    /// <summary>Disco de destino sin particiones (o con una partición de datos si se pide).</summary>
    public static Disk TargetDisk(
        long size = 512 * Gib,
        string serial = "TGT-0002",
        int sector = 512,
        bool withDataPartition = false,
        BusKind bus = BusKind.Usb,
        bool isReadOnly = false,
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

        return new Disk(id ?? TargetId, "Modelo destino", serial, new ByteSize(size), new SectorSize(sector),
            SectorSize.Bytes4096, bus, PartitionStyle.Gpt, false, partitions, IsOffline: false, IsReadOnly: isReadOnly);
    }

    /// <summary>Copia del disco con otras particiones (Disk.Partitions no admite "with").</summary>
    public static Disk WithPartitions(Disk disk, IEnumerable<Partition> partitions) =>
        new(disk.Id, disk.Model, disk.SerialNumber, disk.Size, disk.LogicalSectorSize, disk.PhysicalSectorSize,
            disk.Bus, disk.PartitionStyle, disk.IsSystem, partitions.ToArray(), disk.IsOffline, disk.IsReadOnly);

    /// <summary>Inventario UEFI con los discos indicados.</summary>
    public static DiskInventory InventoryOf(params Disk[] disks) => new(CapturedAt, FirmwareType.Uefi, disks);

    /// <summary>Hechos por defecto: sin BitLocker, con corriente, instantánea disponible con 20 GiB y destinos libres.</summary>
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

    /// <summary>Plan con los hechos por defecto y el disco de destino típico de 512 GiB.</summary>
    public static Result<ClonePlan, PreflightReport> PlanFor(
        DiskInventory inventory,
        DiskId? target = null,
        PlatformFacts? facts = null,
        VerificationMode mode = VerificationMode.Structural,
        CapacityPolicy? policy = null) =>
        ClonePlanner.Plan(inventory, target ?? TargetId, facts ?? Facts(), mode, policy ?? CapacityPolicy.Default,
            SequentialGuids());

    /// <summary>Comprobación con el código y la gravedad indicados y sin datos.</summary>
    public static PreflightCheck Check(PreflightCode code, Severity severity) =>
        new(code, severity, PreflightCheck.EmptyFacts());
}
