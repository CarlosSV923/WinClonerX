using WinCopy.Application.Ports;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de IStructuralVerifier con resultado configurable y registro de los planes verificados.</summary>
public sealed class FakeStructuralVerifier : FakeBase, IStructuralVerifier
{
    /// <summary>Resultado que devuelve VerifyAsync. Por defecto, correcto y sin problemas.</summary>
    public VerificationResult Result { get; set; } = new(true, []);

    /// <summary>Planes verificados, en orden.</summary>
    public List<PlanId> VerifiedPlans { get; } = [];

    /// <summary>Registra el plan verificado y devuelve el resultado configurado.</summary>
    public Task<VerificationResult> VerifyAsync(ClonePlan plan, CancellationToken ct) =>
        Reply(nameof(VerifyAsync), () =>
        {
            VerifiedPlans.Add(plan.Id);
            return Result;
        }, ct);
}
