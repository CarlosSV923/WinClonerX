using Shouldly;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Infrastructure.Windows.Logging;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Logging;

public sealed class DiskOperationRecorderTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryExecutionLog _log = new();
    private readonly ManualClock _clock = new(Start);
    private readonly DiskOperationRecorder _recorder;

    public DiskOperationRecorderTests()
    {
        _recorder = new DiskOperationRecorder(_log, _clock);
    }

    [Fact]
    public void SuccessEmitsInformationEventWithClockDuration()
    {
        using (DiskOperationScope scope = _recorder.Begin("disk-1", "Read", offset: 0, length: 1_048_576))
        {
            _clock.Advance(TimeSpan.FromMilliseconds(250));
            scope.Succeed();
        }

        LogEvent e = _log.Events.Single();
        e.Event.ShouldBe("DiskOperation");
        e.Level.ShouldBe(LogLevelKind.Information);
        e.Properties["Result"].ShouldBe("Success");
        e.Properties["Disk"].ShouldBe("disk-1");
        e.Properties["Operation"].ShouldBe("Read");
        ((long)e.Properties["DurationMs"]!).ShouldBe(250);
        e.Properties.ContainsKey("Error").ShouldBeFalse();
    }

    [Fact]
    public void FailEmitsErrorEventWithErrorTypeAndMessage()
    {
        using (DiskOperationScope scope = _recorder.Begin("disk-2", "Write"))
        {
            scope.Fail(new IOException("sector unreadable"));
        }

        LogEvent e = _log.Events.Single();
        e.Level.ShouldBe(LogLevelKind.Error);
        e.Properties["Result"].ShouldBe("Failed");

        var error = (IReadOnlyDictionary<string, object?>)e.Properties["Error"]!;
        error["Type"].ShouldBe(typeof(IOException).FullName);
        error["Message"].ShouldBe("sector unreadable");
    }

    [Fact]
    public void DisposeWithoutResultEmitsAbandoned()
    {
        using (_recorder.Begin("disk-3", "Lock"))
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        LogEvent e = _log.Events.Single();
        e.Level.ShouldBe(LogLevelKind.Information);
        e.Properties["Result"].ShouldBe("Abandoned");
        e.Properties.ContainsKey("Error").ShouldBeFalse();
        ((long)e.Properties["DurationMs"]!).ShouldBe(1000);
    }

    [Fact]
    public void FirstOutcomeWinsAndDisposeEmitsOnlyOnce()
    {
        var scope = _recorder.Begin("disk-4", "Extend");
        scope.Succeed();
        scope.Fail(new InvalidOperationException("too late"));
        scope.Dispose();
        scope.Dispose();

        LogEvent e = _log.Events.Single();
        e.Properties["Result"].ShouldBe("Success");
        e.Level.ShouldBe(LogLevelKind.Information);
    }

    [Theory]
    [InlineData("Format")]
    [InlineData("read")]
    [InlineData("")]
    public void UnknownOperationNameThrows(string operation)
    {
        Should.Throw<ArgumentException>(() => _recorder.Begin("disk-5", operation));

        _log.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    [InlineData("Lock")]
    [InlineData("Offline")]
    [InlineData("Online")]
    [InlineData("CommitLayout")]
    [InlineData("DeleteLayout")]
    [InlineData("Shrink")]
    [InlineData("Restore")]
    [InlineData("Extend")]
    public void EveryDocumentedOperationIsAccepted(string operation)
    {
        using (DiskOperationScope scope = _recorder.Begin("disk-6", operation))
        {
            scope.Succeed();
        }

        _log.Events.Single().Properties["Operation"].ShouldBe(operation);
    }

    [Fact]
    public void OffsetAndLengthArePassedThrough()
    {
        using (_recorder.Begin("disk-7", "Write", offset: 1_048_576, length: 65_536))
        {
        }

        IReadOnlyDictionary<string, object?> properties = _log.Events.Single().Properties;
        ((long?)properties["Offset"]).ShouldBe(1_048_576L);
        ((long?)properties["Length"]).ShouldBe(65_536L);
    }

    [Fact]
    public void MissingOffsetAndLengthAreRecordedAsNull()
    {
        using (_recorder.Begin("disk-8", "Shrink"))
        {
        }

        LogEvent e = _log.Events.Single();
        e.Properties["Offset"].ShouldBeNull();
        e.Properties["Length"].ShouldBeNull();
    }

    private sealed class InMemoryExecutionLog : IExecutionLog
    {
        public List<LogEvent> Events { get; } = [];

        public string CurrentFile => "memory.clef";

        public void Record(LogEvent e)
        {
            Events.Add(e);
        }

        public Task ExportAsync(string destinationPath, bool includeInventory, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
