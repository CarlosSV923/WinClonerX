using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;

namespace WinCopy.Infrastructure.Windows.Tests.Fakes;

/// <summary>Keeps every recorded event in memory.</summary>
internal sealed class RecordingExecutionLog : IExecutionLog
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
