using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Application.Tests.Fakes;

namespace WinClonerX.Application.Tests.TestData;

/// <summary>Fake target verifier that reports configured outcomes and supports cancellation between chunks.</summary>
internal sealed class FakeTargetVerifier : FakeBase, ITargetVerifier
{
    public List<(ITargetDisk Target, long TargetOffset, IReadOnlyList<ChunkDigest> Digests)> Comparisons { get; } = [];
    public Dictionary<long, RangeComparison> Overrides { get; } = [];
    public Action? OnChunk { get; set; }

    public async Task<RangeComparison> VerifyAsync(
        ITargetDisk target,
        IReadOnlyList<ChunkDigest> digests,
        IProgress<long> bytesVerified,
        CancellationToken ct)
    {
        Calls.Add(nameof(VerifyAsync));
        long targetOffset = digests.Count == 0 ? 0 : digests[0].TargetOffset;
        Comparisons.Add((target, targetOffset, digests));
        if (FailOn.TryGetValue(nameof(VerifyAsync), out var failure))
        {
            throw failure;
        }

        long compared = 0;
        foreach (ChunkDigest digest in digests)
        {
            ct.ThrowIfCancellationRequested();
            compared += digest.Length;
            bytesVerified.Report(compared);
            OnChunk?.Invoke();
        }

        await Task.Yield();
        return Overrides.TryGetValue(targetOffset, out var configured)
            ? configured
            : new RangeComparison(compared, 0, [], 0, []);
    }
}
