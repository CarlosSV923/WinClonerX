using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.App.Tests.Fakes;

/// <summary>Answers plan requests with a delegate and records how it was called.</summary>
internal sealed class FakeBuildClonePlan(Func<DiskId, Result<ClonePlan, PreflightReport>> build) : IBuildClonePlan
{
    public int CallCount { get; private set; }

    public DiskId? LastTarget { get; private set; }

    public VerificationMode? LastMode { get; private set; }

    public Task<Result<ClonePlan, PreflightReport>> ExecuteAsync(DiskId target, VerificationMode mode, CancellationToken ct)
    {
        CallCount++;
        LastTarget = target;
        LastMode = mode;
        return Task.FromResult(build(target));
    }
}
