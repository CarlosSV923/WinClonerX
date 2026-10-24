using Shouldly;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Application.Tests.Fakes;
using WinClonerX.Application.UseCases.Recovery;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Application.Tests.TestData.PlanningTestData;
using static WinClonerX.Application.Tests.TestData.ShrinkTestData;

namespace WinClonerX.Application.Tests.UseCases;

/// <summary>
/// Recuperación al iniciarse (FR-021a): sin entrada no hace nada; con una entrada válida restaura y borra el diario; con una
/// entrada que no encaja con el disco no toca la partición y conserva el diario.
/// </summary>
public sealed class RecoverPendingSourceRestoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ByteSize Original = ByteSize.FromGiB(256);
    private static readonly ByteSize Shrunk = ByteSize.FromGiB(120);

    private readonly FakeDiskInventoryReader _inventory = new();
    private readonly FakeSourceResizer _resizer = new();
    private readonly FakeSourceRestoreJournal _journal;
    private readonly FakeExecutionLog _log = new();
    private readonly RecoverPendingSourceRestore _recovery;
    private int _logResolutions;

    public RecoverPendingSourceRestoreTests()
    {
        _journal = new FakeSourceRestoreJournal(_resizer);
        _recovery = new RecoverPendingSourceRestore(_inventory, _resizer, _journal, () =>
        {
            _logResolutions++;
            return _log;
        });
    }

    [Fact]
    public async Task WithoutAJournalEntryItReportsNothingAndTouchesNothing()
    {
        var result = await _recovery.RecoverAsync(Ct);

        result.ShouldBe(PendingSourceRestoreRecovery.Nothing);
        _resizer.Calls.ShouldBeEmpty();
        _inventory.Calls.ShouldBeEmpty();
        _journal.Calls.Single().ShouldBe(nameof(ISourceRestoreJournal.Read));
        _logResolutions.ShouldBe(0);
    }

    [Fact]
    public async Task AShrunkSourceIsRestoredToItsOriginalLengthAndTheJournalIsCleared()
    {
        Arrange(current: Shrunk);
        _resizer.RestoreEffect = (_, length) => _inventory.Inventory = InventoryWithWindows(length);

        var result = await _recovery.RecoverAsync(Ct);

        result.ShouldBe(PendingSourceRestoreRecovery.Restored);
        var restore = _resizer.Restores.Single();
        restore.Partition.ShouldBe(WindowsPartition);
        restore.Length.ShouldBe(Original);
        _journal.Entry.ShouldBeNull();
        _journal.Cleared.ShouldBe(1);
        _log.Events.Any(e => e.Event == "PendingSourceRestoreCompleted").ShouldBeTrue();
        _log.Events.Any(e => e.Event == "SourceRestoreJournalCleared").ShouldBeTrue();
    }

    [Fact]
    public async Task APartitionAlreadyAtItsOriginalLengthOnlyClearsTheJournal()
    {
        Arrange(current: Original);

        var result = await _recovery.RecoverAsync(Ct);

        result.ShouldBe(PendingSourceRestoreRecovery.AlreadyRestored);
        _resizer.Restores.ShouldBeEmpty();
        _resizer.Calls.ShouldNotContain(nameof(ISourceResizer.RestoreAsync));
        _journal.Entry.ShouldBeNull();
        _journal.Cleared.ShouldBe(1);
    }

    [Fact]
    public async Task AnUnreadableJournalNeedsAttentionWithoutTouchingTheDisk()
    {
        _journal.Corruption = "unexpected end of JSON";

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.JournalUnreadable);
        _resizer.Calls.ShouldBeEmpty();
        _inventory.Calls.ShouldBeEmpty();
        _journal.Calls.ShouldNotContain(nameof(ISourceRestoreJournal.Clear));
    }

    [Fact]
    public async Task AJournalThatCannotBeReadIsReportedAsUnreadableNotThrown()
    {
        _journal.FailOn[nameof(ISourceRestoreJournal.Read)] = new IOException("access denied");

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.JournalUnreadable);
        _resizer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task APartitionThatIsNotOnTheSystemDiskNeedsAttentionAndIsNotResized()
    {
        Arrange(current: Shrunk, partition: SourcePartitionGuid(99));

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.PartitionNotFound);
        _resizer.Calls.ShouldNotContain(nameof(ISourceResizer.RestoreAsync));
        _journal.Entry.ShouldNotBeNull();
        _journal.Calls.ShouldNotContain(nameof(ISourceRestoreJournal.Clear));
    }

    [Fact]
    public async Task APartitionThatIsNotTheWindowsPartitionNeedsAttentionAndIsNotResized()
    {
        _inventory.Inventory = InventoryWithWindows(Shrunk, isWindowsVolume: false);
        _journal.Entry = Entry();

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.NotWindowsPartition);
        _resizer.Calls.ShouldNotContain(nameof(ISourceResizer.RestoreAsync));
        _journal.Entry.ShouldNotBeNull();
    }

    [Fact]
    public async Task AnOriginalLengthSmallerThanTheCurrentOneNeedsAttentionAndIsNotResized()
    {
        Arrange(current: Shrunk, originalLength: ByteSize.FromGiB(100));

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.OriginalNotLarger);
        _resizer.Calls.ShouldNotContain(nameof(ISourceResizer.RestoreAsync));
        _journal.Entry.ShouldNotBeNull();
    }

    [Fact]
    public async Task AnOriginalLengthBeyondTheSupportedMaximumNeedsAttentionAndIsNotResized()
    {
        Arrange(current: Shrunk);
        _resizer.MaximumSize = ByteSize.FromGiB(200);

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.ExceedsSupportedMaximum);
        _resizer.Calls.ShouldNotContain(nameof(ISourceResizer.RestoreAsync));
        _journal.Entry.ShouldNotBeNull();
    }

    [Fact]
    public async Task AFailedMaximumQueryNeedsAttentionAndIsNotResized()
    {
        Arrange(current: Shrunk);
        _resizer.FailOn[nameof(ISourceResizer.GetMaximumSizeAsync)] = new InvalidOperationException("wmi busy");

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.SizeQueryFailed);
        _resizer.Calls.ShouldNotContain(nameof(ISourceResizer.RestoreAsync));
        _journal.Entry.ShouldNotBeNull();
    }

    [Fact]
    public async Task ANoSystemDiskNeedsAttentionAndIsNotResized()
    {
        _inventory.Inventory = InventoryOf();
        _journal.Entry = Entry();

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.InventoryUnavailable);
        _resizer.Calls.ShouldNotContain(nameof(ISourceResizer.RestoreAsync));
        _journal.Entry.ShouldNotBeNull();
    }

    [Fact]
    public async Task AFailedInventoryReadNeedsAttentionAndIsNotResized()
    {
        _journal.Entry = Entry();
        _inventory.FailOn[nameof(IDiskInventoryReader.ReadAsync)] = new IOException("wmi unavailable");

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.InventoryUnavailable);
        _resizer.Calls.ShouldNotContain(nameof(ISourceResizer.RestoreAsync));
        _journal.Entry.ShouldNotBeNull();
    }

    [Fact]
    public async Task AFailedRestoreNeedsAttentionAndKeepsTheJournal()
    {
        Arrange(current: Shrunk);
        _resizer.FailOn[nameof(ISourceResizer.RestoreAsync)] = new InvalidOperationException("vds busy");

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.RestoreFailed);
        _journal.Entry.ShouldNotBeNull();
        _journal.Calls.ShouldNotContain(nameof(ISourceRestoreJournal.Clear));
    }

    [Fact]
    public async Task ARestoreThatDoesNotReachTheOriginalLengthNeedsAttentionAndKeepsTheJournal()
    {
        // La restauración no tiene efecto en el inventario: la verificación posterior ve la partición todavía reducida.
        Arrange(current: Shrunk);

        var result = await _recovery.RecoverAsync(Ct);

        AssertNeedsAttention(result, PendingSourceRestoreIssue.VerificationFailed);
        _resizer.Restores.Count.ShouldBe(1);
        _journal.Entry.ShouldNotBeNull();
        _journal.Calls.ShouldNotContain(nameof(ISourceRestoreJournal.Clear));
    }

    [Fact]
    public async Task AFailedJournalClearAfterARestoreIsAWarningAndStillRestored()
    {
        Arrange(current: Shrunk);
        _resizer.RestoreEffect = (_, length) => _inventory.Inventory = InventoryWithWindows(length);
        _journal.FailOn[nameof(ISourceRestoreJournal.Clear)] = new IOException("file locked");

        var result = await _recovery.RecoverAsync(Ct);

        result.ShouldBe(PendingSourceRestoreRecovery.Restored);
        var warning = _log.Events.Single(e => e.Event == "SourceRestoreJournalClearFailed");
        warning.Level.ShouldBe(LogLevelKind.Warning);
    }

    [Fact]
    public async Task TheFindingAndTheFailureAreLoggedWithTheReasonCode()
    {
        Arrange(current: Shrunk, originalLength: ByteSize.FromGiB(100));

        await _recovery.RecoverAsync(Ct);

        _log.Events.Single(e => e.Event == "PendingSourceRestoreFound").Level.ShouldBe(LogLevelKind.Warning);
        var failed = _log.Events.Single(e => e.Event == "PendingSourceRestoreFailed");
        failed.Level.ShouldBe(LogLevelKind.Warning);
        failed.Properties["Reason"].ShouldBe(nameof(PendingSourceRestoreIssue.OriginalNotLarger));
        failed.Properties.Keys.Intersect(LogEvent.ReservedPropertyNames).ShouldBeEmpty();
    }

    /// <summary>Deja en el diario una entrada válida y en el disco la partición de Windows con la longitud indicada.</summary>
    private void Arrange(ByteSize current, ByteSize? originalLength = null, PartitionGuid? partition = null)
    {
        _inventory.Inventory = InventoryWithWindows(current);
        _journal.Entry = Entry(originalLength, partition);
    }

    private static PendingSourceRestore Entry(ByteSize? originalLength = null, PartitionGuid? partition = null) =>
        new(partition ?? WindowsPartition, originalLength ?? Original, new PlanId(Guid.NewGuid()), FakeDefaults.Now);

    /// <summary>Disco del sistema con la partición de Windows de la longitud indicada, y el resto del diseño típico.</summary>
    private static DiskInventory InventoryWithWindows(ByteSize windowsLength, bool isWindowsVolume = true)
    {
        var partitions = new List<Partition>
        {
            new(1, new Extent(Mib, 100 * Mib), GptPartitionType.EfiSystem, SourcePartitionGuid(1), 0UL, "EFI", null),
            new(3, new Extent(WindowsStart, windowsLength.Bytes), GptPartitionType.BasicData, WindowsPartition, 0UL, "Windows",
                new Volume(FileSystemKind.Ntfs, windowsLength, ByteSize.FromGiB(40), new ByteSize(4096), EncryptionState.None, isWindowsVolume, null)),
        };

        return InventoryOf(new Disk(SourceId, "Modelo sistema", "SYS-0001", ByteSize.FromGiB(256), new SectorSize(512),
            SectorSize.Bytes4096, BusKind.Nvme, PartitionStyle.Gpt, true, partitions, IsOffline: false, IsReadOnly: false));
    }

    private static void AssertNeedsAttention(PendingSourceRestoreRecovery result, PendingSourceRestoreIssue issue)
    {
        result.Outcome.ShouldBe(PendingSourceRestoreOutcome.NeedsAttention);
        result.Issue.ShouldBe(issue);
        result.Detail.ShouldNotBeNullOrWhiteSpace();
    }
}
