using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Verification;

namespace WinClonerX.Infrastructure.Windows.Tests.Verification;

// A real ClonePlan made by the public ClonePlanner, from a typical Windows 11 system disk (ESP, MSR, Windows NTFS, Recovery)
// and a 512 GiB target. The layout of the plan is the reference for every test.
internal static class VerificationPlan
{
    private const long Mib = 1024L * 1024;
    private const long Gib = Mib * 1024;
    private const long RecoveryLength = 750 * Mib;
    private const long WindowsStart = 117 * Mib;

    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly DiskId SourceId = new(0, Guid.Parse("00000000-0000-0000-0000-000000000100"));
    private static readonly DiskId TargetId = new(1, Guid.Parse("00000000-0000-0000-0000-000000000200"));

    /// <summary>Index of the Windows partition in <see cref="ClonePlan.Mappings"/>.</summary>
    public const int WindowsIndex = 2;

    /// <summary>Index of the Recovery partition in <see cref="ClonePlan.Mappings"/>.</summary>
    public const int RecoveryIndex = 3;

    /// <summary>Index of the EFI system partition in <see cref="ClonePlan.Mappings"/>.</summary>
    public const int EfiIndex = 0;

    /// <summary>Index of the MSR partition in <see cref="ClonePlan.Mappings"/>. It is not NTFS.</summary>
    public const int MsrIndex = 1;

    public static ClonePlan Create()
    {
        Disk[] disks = [SystemDisk(), TargetDisk()];
        var inventory = new DiskInventory(CapturedAt, FirmwareType.Uefi, disks);

        var result = ClonePlanner.Plan(
            inventory,
            TargetId,
            Facts(),
            VerificationMode.Structural,
            CapacityPolicy.Default,
            SequentialGuids());

        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("The test plan could not be made.");
    }

    /// <summary>The layout a correct write produces: the plan's disk GUID and every mapped partition exactly as planned.</summary>
    public static DriveLayout MatchingLayout(ClonePlan plan) => new(
        DiskPartitionStyle.Gpt,
        plan.NewDiskGuid,
        plan.Mappings
            .Select(mapping => new DriveLayoutPartition(
                mapping.NewUniqueId.Value,
                mapping.Source.Type.Value,
                mapping.TargetExtent.Offset,
                mapping.TargetExtent.Length))
            .ToArray());

    private static Disk SystemDisk()
    {
        const long size = 256 * Gib;
        long recoveryOffset = size - Mib - RecoveryLength;
        long windowsLength = recoveryOffset - WindowsStart;

        var partitions = new List<Partition>
        {
            new(1, new Extent(Mib, 100 * Mib), GptPartitionType.EfiSystem, SourcePartition(1), 0UL, "EFI",
                new Volume(FileSystemKind.Fat32, ByteSize.FromMiB(100), ByteSize.FromMiB(30), null, EncryptionState.None, false, null)),
            new(2, new Extent(101 * Mib, 16 * Mib), GptPartitionType.MicrosoftReserved, SourcePartition(2), 0UL, "MSR", null),
            new(3, new Extent(WindowsStart, windowsLength), GptPartitionType.BasicData, SourcePartition(3), 0UL, "Windows",
                new Volume(FileSystemKind.Ntfs, new ByteSize(windowsLength), ByteSize.FromGiB(40), new ByteSize(4096),
                    EncryptionState.None, true, null)),
            new(4, new Extent(recoveryOffset, RecoveryLength), GptPartitionType.WindowsRecovery, SourcePartition(4), 0UL, "Recovery",
                new Volume(FileSystemKind.Ntfs, ByteSize.FromMiB(750), ByteSize.FromMiB(450), new ByteSize(4096),
                    EncryptionState.None, false, null)),
        };

        return new Disk(SourceId, "Source model", "SYS-0001", new ByteSize(size), new SectorSize(512), SectorSize.Bytes4096,
            BusKind.Nvme, PartitionStyle.Gpt, true, partitions, IsOffline: false, IsReadOnly: false);
    }

    private static Disk TargetDisk() => new(
        TargetId,
        "Target model",
        "TGT-0002",
        new ByteSize(512 * Gib),
        new SectorSize(512),
        SectorSize.Bytes4096,
        BusKind.Usb,
        PartitionStyle.Gpt,
        false,
        [],
        IsOffline: false,
        IsReadOnly: false);

    private static PlatformFacts Facts() => new(
        new Dictionary<PartitionGuid, EncryptionState>(),
        OnBattery: false,
        BatteryPercent: 100,
        SnapshotAvailable: true,
        SnapshotFreeSpace: ByteSize.FromGiB(20),
        MinimumWindowsSize: null,
        TargetInUse: new Dictionary<DiskId, bool> { [TargetId] = false });

    private static PartitionGuid SourcePartition(int number) => new(G(1000 + number));

    // Deterministic GUIDs: the plan id, the disk GUID, then one GUID per partition.
    private static Func<Guid> SequentialGuids()
    {
        int next = 5000 - 1;
        return () => G(++next);
    }

    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");
}
