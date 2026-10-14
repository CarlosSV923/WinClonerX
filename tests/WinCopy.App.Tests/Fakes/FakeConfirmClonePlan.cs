using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Planning;

namespace WinCopy.App.Tests.Fakes;

/// <summary>Confirms the plan it was given, or fails with the scripted exception. Records what it was asked to confirm.</summary>
internal sealed class FakeConfirmClonePlan(ClonePlan plan) : IConfirmClonePlan
{
    public int CallCount { get; private set; }

    public PlanId? LastPlanId { get; private set; }

    public IReadOnlyCollection<PreflightCode>? LastAcceptedWarnings { get; private set; }

    public string? LastTypedSuffix { get; private set; }

    /// <summary>The shrink authorization the view model passed in the last call.</summary>
    public bool? LastConfirmSourceShrink { get; private set; }

    /// <summary>When set, the next call fails with this exception instead of confirming.</summary>
    public Exception? NextFailure { get; set; }

    public Task<ClonePlan> ExecuteAsync(PlanId id, IReadOnlyCollection<PreflightCode> acceptedWarnings, string typedSuffix, bool confirmSourceShrink, CancellationToken ct)
    {
        CallCount++;
        LastPlanId = id;
        LastAcceptedWarnings = acceptedWarnings;
        LastTypedSuffix = typedSuffix;
        LastConfirmSourceShrink = confirmSourceShrink;

        if (NextFailure is { } failure)
        {
            NextFailure = null;
            return Task.FromException<ClonePlan>(failure);
        }

        return Task.FromResult(plan);
    }
}
