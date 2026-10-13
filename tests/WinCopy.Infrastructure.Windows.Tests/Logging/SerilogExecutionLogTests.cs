using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Shouldly;
using WinCopy.Application.Ports.Models;
using WinCopy.Infrastructure.Windows.Logging;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Logging;

// Every test writes ordinary files under a unique temp folder that is deleted in Dispose.
public sealed class SerilogExecutionLogTests : IDisposable
{
    private const string AppVersion = "1.2.3";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"WinCopy.Logging.Tests-{Guid.NewGuid():N}");
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 13, 10, 20, 30, TimeSpan.Zero));
    private readonly Guid _executionId = Guid.NewGuid();
    private readonly List<SerilogExecutionLog> _logs = [];

    public void Dispose()
    {
        foreach (SerilogExecutionLog log in _logs)
        {
            log.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string LogDirectory => Path.Combine(_root, "logs");

    private string ExportDirectory => Path.Combine(_root, "exports");

    [Fact]
    public void FileNameFollowsPatternWithLocalTimeAndExecutionId()
    {
        using SerilogExecutionLog log = CreateLog();

        string localStamp = _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string expectedName = $"winCopy-{localStamp}-{_executionId:N}.clef";

        log.CurrentFile.ShouldBe(Path.Combine(LogDirectory, expectedName));
        Directory.Exists(LogDirectory).ShouldBeTrue();
    }

    [Fact]
    public void EveryLineIsClefJsonWithEnvelopeProperties()
    {
        using SerilogExecutionLog log = CreateLog();
        log.Record(new LogEvent("PhaseStarted", new Dictionary<string, object?> { ["Phase"] = "CopyData" }));
        log.Record(new LogEvent(
            "DiskOperation",
            new Dictionary<string, object?> { ["Disk"] = "disk-1", ["Result"] = "Failed" },
            LogLevelKind.Error));
        log.Dispose();

        string[] lines = ReadLines(log.CurrentFile);

        lines.Length.ShouldBe(2);
        foreach (string line in lines)
        {
            JsonElement root = JsonSerializer.Deserialize<JsonElement>(line);

            root.TryGetProperty("@t", out _).ShouldBeTrue();
            root.GetProperty("@mt").GetString().ShouldBe("{Event}");
            Guid.Parse(root.GetProperty("ExecutionId").GetString()!).ShouldBe(_executionId);
            root.GetProperty("AppVersion").GetString().ShouldBe(AppVersion);
            root.GetProperty("Event").GetString().ShouldNotBeNullOrWhiteSpace();
        }

        JsonElement phase = JsonSerializer.Deserialize<JsonElement>(lines[0]);
        phase.GetProperty("Event").GetString().ShouldBe("PhaseStarted");
        phase.GetProperty("Phase").GetString().ShouldBe("CopyData");
        phase.TryGetProperty("@l", out _).ShouldBeFalse("Information is the default level and is omitted");

        JsonElement operation = JsonSerializer.Deserialize<JsonElement>(lines[1]);
        operation.GetProperty("@l").GetString().ShouldBe("Error");
        operation.GetProperty("Disk").GetString().ShouldBe("disk-1");
    }

    [Fact]
    public void StructuredPropertiesArePreservedIncludingNestedArrays()
    {
        using SerilogExecutionLog log = CreateLog();
        log.Record(new LogEvent(
            "InventoryCaptured",
            new Dictionary<string, object?>
            {
                ["Firmware"] = "Uefi",
                ["Note"] = null,
                ["Disks"] = new[]
                {
                    new DiskSummary(0, "NVMe Samsung", 512_000_000_000),
                    new DiskSummary(1, "USB Disk", 64_000_000_000),
                },
            }));
        log.Dispose();

        JsonElement line = JsonSerializer.Deserialize<JsonElement>(ReadLines(log.CurrentFile).Single());

        line.GetProperty("Firmware").GetString().ShouldBe("Uefi");
        line.GetProperty("Note").ValueKind.ShouldBe(JsonValueKind.Null);

        JsonElement disks = line.GetProperty("Disks");
        disks.ValueKind.ShouldBe(JsonValueKind.Array);
        disks.GetArrayLength().ShouldBe(2);
        disks[1].GetProperty("Model").GetString().ShouldBe("USB Disk");
        disks[1].GetProperty("SizeBytes").GetInt64().ShouldBe(64_000_000_000);
    }

    [Fact]
    public void EventIsOnDiskRightAfterRecordWhileTheLogIsStillOpen()
    {
        using SerilogExecutionLog log = CreateLog();

        log.Record(new LogEvent("ExecutionStarted", new Dictionary<string, object?> { ["PlanId"] = "plan-1" }));

        string[] lines = ReadLines(log.CurrentFile);
        lines.Length.ShouldBe(1);
        JsonSerializer.Deserialize<JsonElement>(lines[0]).GetProperty("Event").GetString()
            .ShouldBe("ExecutionStarted");
    }

    [Fact]
    public async Task ExportWithoutInventoryContainsOnlyTheClefFile()
    {
        using SerilogExecutionLog log = CreateLog();
        log.Record(new LogEvent("InventoryCaptured", new Dictionary<string, object?> { ["Firmware"] = "Uefi" }));
        log.Record(new LogEvent("PhaseStarted", new Dictionary<string, object?> { ["Phase"] = "CopyData" }));
        string archivePath = Path.Combine(ExportDirectory, "export.zip");

        await log.ExportAsync(archivePath, includeInventory: false, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Select(entry => entry.FullName).ShouldBe([Path.GetFileName(log.CurrentFile)]);
        ReadEntryLines(archive.Entries[0]).ShouldBe(ReadLines(log.CurrentFile));
    }

    [Fact]
    public async Task ExportWithInventoryAndInventoryEventAddsInventoryJson()
    {
        using SerilogExecutionLog log = CreateLog();
        log.Record(new LogEvent("PhaseStarted", new Dictionary<string, object?> { ["Phase"] = "CopyData" }));
        log.Record(new LogEvent(
            "InventoryCaptured",
            new Dictionary<string, object?>
            {
                ["Firmware"] = "Uefi",
                ["Disks"] = new[] { new DiskSummary(0, "NVMe Samsung", 512_000_000_000) },
            }));
        string archivePath = Path.Combine(ExportDirectory, "export.zip");

        await log.ExportAsync(archivePath, includeInventory: true, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Select(entry => entry.FullName).ShouldBe(
            [Path.GetFileName(log.CurrentFile), "inventory.json"],
            ignoreOrder: true);

        using JsonDocument inventory = JsonDocument.Parse(ReadEntryText(archive.GetEntry("inventory.json")!));
        inventory.RootElement.GetProperty("Firmware").GetString().ShouldBe("Uefi");
        inventory.RootElement.GetProperty("Disks").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task ExportWithInventoryButNoInventoryEventWritesNoInventoryJson()
    {
        using SerilogExecutionLog log = CreateLog();
        log.Record(new LogEvent("PhaseStarted", new Dictionary<string, object?> { ["Phase"] = "CopyData" }));
        string archivePath = Path.Combine(ExportDirectory, "export.zip");

        await log.ExportAsync(archivePath, includeInventory: true, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Select(entry => entry.FullName).ShouldBe([Path.GetFileName(log.CurrentFile)]);
    }

    [Fact]
    public async Task ExportOverwritesAnExistingArchive()
    {
        using SerilogExecutionLog log = CreateLog();
        log.Record(new LogEvent("PhaseStarted", new Dictionary<string, object?> { ["Phase"] = "CopyData" }));
        Directory.CreateDirectory(ExportDirectory);
        string archivePath = Path.Combine(ExportDirectory, "export.zip");
        await File.WriteAllTextAsync(archivePath, "not a zip", TestContext.Current.CancellationToken);

        await log.ExportAsync(archivePath, includeInventory: false, TestContext.Current.CancellationToken);

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        archive.Entries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RecordIsThreadSafeAndKeepsOneLinePerEvent()
    {
        const int eventCount = 200;
        SerilogExecutionLog log = CreateLog();

        await Task.WhenAll(Enumerable.Range(0, eventCount)
            .Select(index => Task.Run(() => log.Record(new LogEvent(
                "ProgressReported",
                new Dictionary<string, object?> { ["Index"] = index })), TestContext.Current.CancellationToken)));
        log.Dispose();

        string[] lines = ReadLines(log.CurrentFile);
        lines.Length.ShouldBe(eventCount);

        var indexes = lines
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line).GetProperty("Index").GetInt32())
            .ToArray();
        indexes.Distinct().Count().ShouldBe(eventCount);
    }

    private SerilogExecutionLog CreateLog()
    {
        var log = new SerilogExecutionLog(LogDirectory, _executionId, AppVersion, _clock);
        _logs.Add(log);
        return log;
    }

    // Reads the file the way an external viewer would, while the writer still has it open.
    private static string[] ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    private static string[] ReadEntryLines(ZipArchiveEntry entry)
    {
        return ReadEntryText(entry)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
    }

    private static string ReadEntryText(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public sealed record DiskSummary(int Id, string Model, long SizeBytes);
}
