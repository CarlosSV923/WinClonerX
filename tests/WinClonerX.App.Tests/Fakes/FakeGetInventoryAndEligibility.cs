using WinClonerX.Application.UseCases.Planning;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>Returns the scripted overviews in order; once the script is exhausted the last one repeats.</summary>
internal sealed class FakeGetInventoryAndEligibility(params InventoryOverview[] results) : IGetInventoryAndEligibility
{
    private readonly InventoryOverview[] _results = results.Length > 0 ? results : throw new ArgumentException("At least one overview.", nameof(results));

    public int CallCount { get; private set; }

    public Action? OnCall { get; set; }

    public Task<InventoryOverview> ExecuteAsync(CancellationToken ct)
    {
        var index = Math.Min(CallCount, _results.Length - 1);
        CallCount++;
        OnCall?.Invoke();
        return Task.FromResult(_results[index]);
    }
}
