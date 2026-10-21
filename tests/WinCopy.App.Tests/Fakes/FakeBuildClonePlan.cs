using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.App.Tests.Fakes;

/// <summary>Answers plan requests with a delegate and records how it was called.</summary>
internal sealed class FakeBuildClonePlan : IBuildClonePlan
{
    private readonly Func<DiskId, VerificationMode, Result<ClonePlan, PreflightReport>> _build;

    /// <summary>Answers by destination only. The verification mode is recorded but does not change the answer.</summary>
    public FakeBuildClonePlan(Func<DiskId, Result<ClonePlan, PreflightReport>> build)
        : this((target, _) => build(target))
    {
    }

    /// <summary>Answers by destination and verification mode, so a change of the option can produce a different plan.</summary>
    public FakeBuildClonePlan(Func<DiskId, VerificationMode, Result<ClonePlan, PreflightReport>> build)
    {
        _build = build;
    }

    public int CallCount { get; private set; }

    public DiskId? LastTarget { get; private set; }

    public VerificationMode? LastMode { get; private set; }

    public Task<Result<ClonePlan, PreflightReport>> ExecuteAsync(DiskId target, VerificationMode mode, CancellationToken ct)
    {
        CallCount++;
        LastTarget = target;
        LastMode = mode;
        return Task.FromResult(_build(target, mode));
    }
}
