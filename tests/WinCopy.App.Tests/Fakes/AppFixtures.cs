using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.App.Tests.Fakes;

/// <summary>
/// Builders for disks, inventories and plans used by the view model tests. A typical UEFI system disk has EFI, MSR,
/// Windows and Recovery partitions; destinations are blank disks. Built on the real domain evaluator and planner.
/// </summary>
internal static class AppFixtures
{
    public const long Mib = 1024L * 1024;
    public const long Gib = 1024L * 1024 * 1024;

    private const long RecoveryLength = 750 * Mib;
    private const long WindowsStart = 117 * Mib;

    public static readonly DateTimeOffset CapturedAt = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public static readonly DiskId SourceId = new(0, G(100));
    public static readonly DiskId TargetId = new(1, G(200));
    public static readonly DiskId OtherTargetId = new(2, G(300));

    public static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    /// <summary>Generator of deterministic GUIDs for the planner.</summary>
    public static Func<Guid> SequentialGuids(int start = 5000)
    {
        var next = start - 1;
        return () => G(++next);
    }

    /// <summary>
    /// System disk: EFI 100 MiB, MSR 16 MiB, Windows NTFS, Recovery 750 MiB at the end. Optionally with BitLocker on Windows.
    /// </summary>
    public static Disk SystemDisk(long size = 256 * Gib, EncryptionState windowsEncryption = EncryptionState.None, long windowsUsed = 40 * Gib)
    {
        var recoveryOffset = size - Mib - RecoveryLength;
        var windowsLength = recoveryOffset - WindowsStart;

        var partitions = new List<Partition>
        {
            new(1, new Extent(Mib, 100 * Mib), GptPartitionType.EfiSystem, new PartitionGuid(G(1001)), 0UL, "EFI",
                new Volume(FileSystemKind.Fat32, ByteSize.FromMiB(100), ByteSize.FromMiB(30), null, EncryptionState.None, false, null)),
            new(2, new Extent(101 * Mib, 16 * Mib), GptPartitionType.MicrosoftReserved, new PartitionGuid(G(1002)), 0UL, "MSR", null),
            new(3, new Extent(WindowsStart, windowsLength), GptPartitionType.BasicData, new PartitionGuid(G(1003)), 0UL, "Windows",
                new Volume(FileSystemKind.Ntfs, new ByteSize(windowsLength), new ByteSize(windowsUsed), new ByteSize(4096),
                    windowsEncryption, true, null)),
            new(4, new Extent(recoveryOffset, RecoveryLength), GptPartitionType.WindowsRecovery, new PartitionGuid(G(1004)), 0UL,
                "Recovery",
                new Volume(FileSystemKind.Ntfs, ByteSize.FromMiB(750), ByteSize.FromMiB(450), new ByteSize(4096),
                    EncryptionState.None, false, null)),
        };

        return new Disk(SourceId, "System model", "SYS-0001", new ByteSize(size), new SectorSize(512), SectorSize.Bytes4096,
            BusKind.Nvme, PartitionStyle.Gpt, true, partitions, IsOffline: false, IsReadOnly: false);
    }

    /// <summary>Blank destination disk, connected over USB by default.</summary>
    public static Disk TargetDisk(long size = 512 * Gib, string serial = "TGT-0002", BusKind bus = BusKind.Usb, DiskId? id = null)
    {
        return new Disk(id ?? TargetId, "Target model", serial, new ByteSize(size), new SectorSize(512), SectorSize.Bytes4096,
            bus, PartitionStyle.Gpt, false, [], IsOffline: false, IsReadOnly: false);
    }

    public static DiskInventory InventoryOf(params Disk[] disks) => new(CapturedAt, FirmwareType.Uefi, disks);

    /// <summary>
    /// A 64 GiB blank destination: smaller than the source's Windows partition, but big enough for the used space plus the
    /// margin, so the planner chooses the ShrinkSource strategy when the minimum Windows size is known (see <see cref="ShrinkFacts"/>).
    /// </summary>
    public static Disk ShrinkTargetDisk(string serial = "TGT-0002") => TargetDisk(size: 64 * Gib, serial: serial);

    /// <summary>Facts for the shrink case: the minimum size Windows supports is 30 GiB, below the 50 GiB it needs with its margin.</summary>
    public static PlatformFacts ShrinkFacts() => Facts(minimumWindowsSize: ByteSize.FromGiB(30));

    /// <summary>Facts with a usable snapshot, plenty of free space, AC power and no destination in use.</summary>
    public static PlatformFacts Facts(bool onBattery = false, bool targetInUse = false, ByteSize? minimumWindowsSize = null) => new(
        new Dictionary<PartitionGuid, EncryptionState>(),
        OnBattery: onBattery,
        BatteryPercent: onBattery ? 80 : null,
        SnapshotAvailable: true,
        SnapshotFreeSpace: ByteSize.FromGiB(20),
        MinimumWindowsSize: minimumWindowsSize,
        TargetInUse: new Dictionary<DiskId, bool>
        {
            [TargetId] = targetInUse,
            [OtherTargetId] = false,
        });

    /// <summary>The overview the use case would return for this inventory: real evaluation with the default policy.</summary>
    public static InventoryOverview Overview(DiskInventory inventory, PlatformFacts? facts = null)
    {
        var (global, targets) = PreflightEvaluator.Evaluate(inventory, facts ?? Facts(), CapacityPolicy.Default);
        return new InventoryOverview(inventory, global, targets);
    }

    /// <summary>Plans the clone with the real planner and deterministic GUIDs.</summary>
    public static Result<ClonePlan, PreflightReport> PlanFor(
        DiskInventory inventory,
        DiskId target,
        PlatformFacts? facts = null,
        VerificationMode mode = VerificationMode.Structural) =>
        ClonePlanner.Plan(inventory, target, facts ?? Facts(), mode, CapacityPolicy.Default, SequentialGuids());
}
