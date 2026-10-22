using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Infrastructure.Windows.Tests.Fakes;

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
