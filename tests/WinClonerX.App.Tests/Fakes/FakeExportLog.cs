using WinClonerX.Application.UseCases;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>Export fake: records every export it was asked for, or throws <see cref="Failure"/> instead.</summary>
internal sealed class FakeExportLog : IExportLog
{
    /// <summary>Exports requested, in order: destination path and inventory flag.</summary>
    public List<(string DestinationPath, bool IncludeInventory)> Exports { get; } = [];

    /// <summary>Exception thrown by the next export. Null, the default, means the export succeeds.</summary>
    public Exception? Failure { get; set; }

    public Task ExecuteAsync(string destinationPath, bool includeInventory, CancellationToken ct)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Exports.Add((destinationPath, includeInventory));
        return Task.CompletedTask;
    }
}
