using Shouldly;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Journal;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Journal;

// Every test works in its own temporary folder, which is deleted afterwards. No test touches %ProgramData%.
public sealed class FileSourceRestoreJournalTests : IDisposable
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 21, 9, 30, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "WinCopy-journal-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AnEntryRecordedByOneInstanceIsReadBackByAnotherOne()
    {
        var entry = Entry();

        new FileSourceRestoreJournal(_folder).Record(entry);
        var read = new FileSourceRestoreJournal(_folder).Read();

        read.State.ShouldBe(PendingSourceRestoreState.Present);
        read.Entry.ShouldBe(entry);
        read.Detail.ShouldBeNull();
    }

    [Fact]
    public void TheFolderIsCreatedOnTheFirstWrite()
    {
        var journal = new FileSourceRestoreJournal(_folder);
        Directory.Exists(_folder).ShouldBeFalse();

        journal.Record(Entry());

        File.Exists(journal.JournalPath).ShouldBeTrue();
    }

    [Fact]
    public void ReadWithoutAFileIsAbsent()
    {
        new FileSourceRestoreJournal(_folder).Read().State.ShouldBe(PendingSourceRestoreState.Absent);
    }

    [Fact]
    public void ClearRemovesTheFileAndCanBeRepeated()
    {
        var journal = new FileSourceRestoreJournal(_folder);
        journal.Record(Entry());

        journal.Clear();

        File.Exists(journal.JournalPath).ShouldBeFalse();
        journal.Read().State.ShouldBe(PendingSourceRestoreState.Absent);
        Should.NotThrow(() => journal.Clear());
    }

    [Fact]
    public void RecordReplacesThePreviousEntry()
    {
        var journal = new FileSourceRestoreJournal(_folder);
        var second = Entry() with { OriginalLength = ByteSize.FromGiB(300) };
        journal.Record(Entry());

        journal.Record(second);

        journal.Read().Entry.ShouldBe(second);
    }

    [Fact]
    public void AWriteLeavesOnlyTheJournalFileBehind()
    {
        var journal = new FileSourceRestoreJournal(_folder);

        journal.Record(Entry());

        Directory.GetFiles(_folder).Single().ShouldBe(journal.JournalPath);
    }

    [Fact]
    public void AFailedWriteKeepsThePreviousJournal()
    {
        var journal = new FileSourceRestoreJournal(_folder);
        var previous = Entry();
        journal.Record(previous);
        // Un directorio con el nombre del temporal impide escribirlo: la escritura falla antes de tocar el diario.
        Directory.CreateDirectory(Path.Combine(_folder, "pending-source-restore.json.tmp"));

        Assert.ThrowsAny<Exception>(() => journal.Record(Entry() with { OriginalLength = ByteSize.FromGiB(300) }));

        journal.Read().Entry.ShouldBe(previous);
    }

    [Fact]
    public void ACorruptFileIsReportedAsInvalidAndNotThrown()
    {
        WriteJournalFile("{ this is not json");

        var read = new FileSourceRestoreJournal(_folder).Read();

        read.State.ShouldBe(PendingSourceRestoreState.Invalid);
        read.Entry.ShouldBeNull();
        read.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AnEmptyFileIsReportedAsInvalid()
    {
        WriteJournalFile(string.Empty);

        new FileSourceRestoreJournal(_folder).Read().State.ShouldBe(PendingSourceRestoreState.Invalid);
    }

    [Fact]
    public void AnEntryWithAnEmptyPartitionIsReportedAsInvalid()
    {
        WriteJournalFile(
            """{"Version":1,"Partition":"00000000-0000-0000-0000-000000000000","OriginalLengthBytes":1,"Plan":"00000000-0000-0000-0000-000000000002","RecordedAtUtc":"2026-10-21T09:30:00+00:00"}""");

        new FileSourceRestoreJournal(_folder).Read().State.ShouldBe(PendingSourceRestoreState.Invalid);
    }

    [Fact]
    public void ANegativeOriginalLengthIsReportedAsInvalid()
    {
        WriteJournalFile(
            """{"Version":1,"Partition":"00000000-0000-0000-0000-0000000000a3","OriginalLengthBytes":-1,"Plan":"00000000-0000-0000-0000-000000000002","RecordedAtUtc":"2026-10-21T09:30:00+00:00"}""");

        new FileSourceRestoreJournal(_folder).Read().State.ShouldBe(PendingSourceRestoreState.Invalid);
    }

    [Fact]
    public void AnUnknownVersionIsReportedAsInvalid()
    {
        WriteJournalFile(
            """{"Version":2,"Partition":"00000000-0000-0000-0000-0000000000a3","OriginalLengthBytes":1,"Plan":"00000000-0000-0000-0000-000000000002","RecordedAtUtc":"2026-10-21T09:30:00+00:00"}""");

        new FileSourceRestoreJournal(_folder).Read().State.ShouldBe(PendingSourceRestoreState.Invalid);
    }

    private static PendingSourceRestore Entry() => new(
        new PartitionGuid(Guid.Parse("00000000-0000-0000-0000-0000000000a3")),
        ByteSize.FromGiB(256),
        new PlanId(Guid.Parse("00000000-0000-0000-0000-000000000002")),
        RecordedAt);

    private void WriteJournalFile(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, FileSourceRestoreJournal.FileName), content);
    }
}
