using WinClonerX.App.Services;
using WinClonerX.App.ViewModels;
using WinClonerX.App.Views;
using WinClonerX.App;
using WinClonerX.Application.Ports;
using WinClonerX.Application.UseCases;
using WinClonerX.Application.UseCases.Execution;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using System.Windows;

/// <summary>Builds the fixed disks, plans and view models used for each language capture.</summary>
internal sealed class Scenario
{
    private const long MiB = 1024L * 1024;
    private const long GiB = 1024L * MiB;

    private readonly Disk _source = FixtureDisk(
        0,
        new Guid("6c9c2a3e-5d11-4a77-9f3c-8b62e3a901d4"),
        "Contoso NVMe SSD 512GB",
        "CTO-4N8P-1502",
        512 * GiB,
        BusKind.Nvme,
        true,
        180 * GiB);
    private readonly Disk _target = FixtureDisk(
        1,
        new Guid("b742f193-82ac-4f6d-a1e9-37c5d0b28f61"),
        "Fabrikam SATA SSD 1TB",
        "FBK-7Q2M-4815",
        1000 * GiB,
        BusKind.Sata,
        false,
        0);
    private readonly Disk _usb = FixtureDisk(
        2,
        new Guid("2e85c6a1-9d37-48bf-b504-61a7f3c92d8e"),
        "Northwind USB HDD 250GB",
        "NWD-3A9K-2501",
        250 * GiB,
        BusKind.Usb,
        false,
        0);
    private readonly CapturedInfo _info = new();
    private readonly RecordingExecute _execute = new();
    private readonly DeterministicGuidSequence _guids = new();

    public ReviewStepViewModel Review { get; }
    public ConfirmStepViewModel Confirm { get; }
    public WizardViewModel Wizard { get; }
    public MainWindow Window { get; }
    public CapturedInfo Info => _info;
    public RecordingExecute Execute => _execute;
    public ClonePlan ConfirmedPlan { get; private set; } = null!;

    public Scenario()
    {
        var inventory = new DiskInventory(ScreenshotScenario.FixedNow, FirmwareType.Uefi, [_source, _target, _usb]);
        var overview = InventoryOverviewFor(inventory);
        ClonePlan BuildPlan(VerificationMode mode) => ClonePlanner.Plan(
            inventory,
            _target.Id,
            Facts(_target.Id, _usb.Id),
            mode,
            CapacityPolicy.Default,
            _guids.Next).Value;

        var disks = new DisksStepViewModel(new FakeInventory(overview));
        Review = new ReviewStepViewModel(new FakePlan(BuildPlan), _info);
        Confirm = new ConfirmStepViewModel(new FakeConfirm(() =>
        {
            ConfirmedPlan = Review.Plan ?? BuildPlan(VerificationMode.StructuralAndFull);
            return ConfirmedPlan;
        }));
        var progress = new ProgressStepViewModel(_execute, new InlineDispatcher(), new NoConfirmation());
        var result = new ResultStepViewModel(
            new NullExport(),
            new NullFileDialog(),
            new NullFolderOpener(),
            new FixedClock(),
            new NullLifetime());
        Wizard = new WizardViewModel(disks, Review, Confirm, progress, result);
        Window = new MainWindow(Wizard)
        {
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
    }

    public void Load()
    {
        Window.Show();
        _ = ((DisksStepViewModel)Wizard.CurrentPage).LoadAsync(CancellationToken.None);
        ScreenshotDispatcher.Flush();
        ((DisksStepViewModel)Wizard.CurrentPage).SelectTargetCommand.Execute(
            ((DisksStepViewModel)Wizard.CurrentPage).Targets.First(item => item.DiskId == _target.Id));
    }

    private static InventoryOverview InventoryOverviewFor(DiskInventory inventory)
    {
        var facts = Facts(inventory.Disks[1].Id, inventory.Disks[2].Id);
        var (global, targets) = PreflightEvaluator.Evaluate(inventory, facts, CapacityPolicy.Default);
        return new InventoryOverview(inventory, global, targets);
    }

    private static PlatformFacts Facts(DiskId target, DiskId usb) => new(
        new Dictionary<PartitionGuid, EncryptionState>(),
        false,
        null,
        true,
        ByteSize.FromGiB(50),
        null,
        new Dictionary<DiskId, bool>
        {
            [target] = false,
            [usb] = false,
        });

    private static Disk FixtureDisk(
        int number,
        Guid diskGuid,
        string model,
        string serial,
        long size,
        BusKind bus,
        bool system,
        long windowsUsed)
    {
        var id = new DiskId(number, diskGuid);
        if (!system)
        {
            return new Disk(
                id,
                model,
                serial,
                new ByteSize(size),
                new SectorSize(512),
                SectorSize.Bytes4096,
                bus,
                PartitionStyle.Gpt,
                false,
                [],
                false,
                false);
        }

        var recoveryLength = 750 * MiB;
        var windowsStart = 117 * MiB;
        var recoveryOffset = size - MiB - recoveryLength;
        var windowsLength = recoveryOffset - windowsStart;
        var partitions = new List<Partition>
        {
            new(
                1,
                new Extent(MiB, 100 * MiB),
                GptPartitionType.EfiSystem,
                new PartitionGuid(new Guid("c91e4b27-a685-43fc-8d12-5b7a30e9f641")),
                0UL,
                "EFI",
                new Volume(
                    FileSystemKind.Fat32,
                    ByteSize.FromMiB(100),
                    ByteSize.FromMiB(30),
                    null,
                    EncryptionState.None,
                    false,
                    null)),
            new(
                2,
                new Extent(101 * MiB, 16 * MiB),
                GptPartitionType.MicrosoftReserved,
                new PartitionGuid(new Guid("48d2a61c-ef03-4a95-b837-1c6e92f5d04b")),
                0UL,
                "MSR",
                null),
            new(
                3,
                new Extent(windowsStart, windowsLength),
                GptPartitionType.BasicData,
                new PartitionGuid(new Guid("a173d8f4-2c69-41be-95a0-fb3276d1e84c")),
                0UL,
                "Windows",
                new Volume(
                    FileSystemKind.Ntfs,
                    new ByteSize(windowsLength),
                    new ByteSize(windowsUsed),
                    new ByteSize(4096),
                    EncryptionState.None,
                    true,
                    null)),
            new(
                4,
                new Extent(recoveryOffset, recoveryLength),
                GptPartitionType.WindowsRecovery,
                new PartitionGuid(new Guid("f5c83a90-716d-4e2b-a149-38d6c0e72b54")),
                0UL,
                "Recovery",
                new Volume(
                    FileSystemKind.Ntfs,
                    ByteSize.FromMiB(750),
                    ByteSize.FromMiB(450),
                    new ByteSize(4096),
                    EncryptionState.None,
                    false,
                    null)),
        };

        return new Disk(
            id,
            model,
            serial,
            new ByteSize(size),
            new SectorSize(512),
            SectorSize.Bytes4096,
            bus,
            PartitionStyle.Gpt,
            true,
            partitions,
            false,
            false);
    }
}

/// <summary>Supplies the stable identifiers and durations shared by all screenshot captures.</summary>
internal static class ScreenshotScenario
{
    private const long MiB = 1024L * 1024;
    private const long GiB = 1024L * MiB;

    public static readonly DateTimeOffset FixedNow = new(2026, 10, 20, 10, 15, 30, TimeSpan.Zero);
    public static readonly ExecutionId RunId = new(new Guid("d4a739b2-6e18-4c5f-a291-83e7b0d64f2c"));
    public const double ThroughputBytesPerSecond = 230 * MiB;
    public static readonly (ExecutionPhase Phase, TimeSpan Duration)[] WarmupDurations =
    [
        (ExecutionPhase.Revalidate, TimeSpan.FromSeconds(10)),
        (ExecutionPhase.Snapshot, TimeSpan.FromSeconds(20)),
        (ExecutionPhase.PrepareTarget, TimeSpan.FromSeconds(30)),
    ];
    public static readonly (ExecutionPhase Phase, TimeSpan Duration)[] CompletionDurations =
    [
        (ExecutionPhase.FullVerification, TimeSpan.FromMinutes(6)),
        (ExecutionPhase.CommitLayout, TimeSpan.FromSeconds(10)),
        (ExecutionPhase.ExpandFileSystem, TimeSpan.FromSeconds(20)),
        (ExecutionPhase.RepairBoot, TimeSpan.FromSeconds(30)),
        (ExecutionPhase.StructuralVerification, TimeSpan.FromSeconds(20)),
        (ExecutionPhase.Cleanup, TimeSpan.FromSeconds(10)),
    ];

    public static TimeSpan DurationAtThroughput(long byteCount, double bytesPerSecond) =>
        TimeSpan.FromSeconds(Math.Ceiling(byteCount / bytesPerSecond));

    public static TimeSpan CopyDuration(ClonePlan plan) =>
        DurationAtThroughput(plan.Estimate.BytesToCopy.Bytes, ThroughputBytesPerSecond);

    public static CloneResultReport CreateResultReport(ClonePlan plan)
    {
        var duration = CopyDuration(plan)
            + SumDurations(WarmupDurations)
            + SumDurations(CompletionDurations);
        var logFile = $@"C:\ProgramData\WinClonerX\logs\winClonerX-20261020-101530-{RunId.Value.ToString("N")[..8]}.clef";

        return new CloneResultReport(
            CloneOutcome.Completed,
            new VerificationResult(true, []),
            new VerificationResult(true, [], 0, []),
            null,
            RecoveryEnvironmentState.Configured,
            duration,
            logFile,
            [NextStepCode.ShutDownAndReplaceDisk]);
    }

    private static TimeSpan SumDurations((ExecutionPhase Phase, TimeSpan Duration)[] durations) =>
        durations.Aggregate(TimeSpan.Zero, (total, item) => total + item.Duration);
}

/// <summary>Generates repeatable planner GUIDs from a fixed nonzero base value.</summary>
internal sealed class DeterministicGuidSequence
{
    private const long BaseValue = 0x37A31C4E82D9;
    private long _generatedValue = BaseValue;

    public Guid Next() => new($"9d6f2a81-3c57-4e0b-b418-{Interlocked.Increment(ref _generatedValue):X12}");
}
