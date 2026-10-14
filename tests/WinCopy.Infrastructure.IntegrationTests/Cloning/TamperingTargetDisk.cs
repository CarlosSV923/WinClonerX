using WinCopy.Application.Ports;
using WinCopy.Domain.Planning;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.IntegrationTests.Cloning;

/// <summary>
/// Test-only factory decorator. It opens the real target disk (<see cref="TargetDiskFactory"/> over <see cref="DiskWriteGuard.Default"/>)
/// and wraps it in <see cref="TamperingTargetDisk"/>, which changes the layout before the real adapter commits it. Used to
/// produce a clone whose GPT entries are wrong, so that the real structural verifier runs against a bad table.
/// </summary>
internal sealed class TamperingTargetDiskFactory(
    TargetDiskFactory inner,
    Func<TargetLayout, TargetLayout> tamperCommittedLayout) : ITargetDiskFactory
{
    public ITargetDisk Open(ClonePlan confirmedPlan)
    {
        ITargetDisk real = inner.Open(confirmedPlan);
        return new TamperingTargetDisk(real, tamperCommittedLayout);
    }
}

/// <summary>Forwards every call to the real <see cref="ITargetDisk"/>. Only <see cref="CommitLayoutAsync"/> is altered.</summary>
internal sealed class TamperingTargetDisk(ITargetDisk inner, Func<TargetLayout, TargetLayout> tamperCommittedLayout) : ITargetDisk
{
    public Task LockAndTakeOfflineAsync(CancellationToken ct) => inner.LockAndTakeOfflineAsync(ct);

    public Task WipeBoundariesAsync(CancellationToken ct) => inner.WipeBoundariesAsync(ct);

    public ValueTask WriteAsync(long diskOffset, ReadOnlyMemory<byte> alignedBuffer, CancellationToken ct) =>
        inner.WriteAsync(diskOffset, alignedBuffer, ct);

    public ValueTask<int> ReadAsync(long diskOffset, Memory<byte> alignedBuffer, CancellationToken ct) =>
        inner.ReadAsync(diskOffset, alignedBuffer, ct);

    public Task CommitLayoutAsync(TargetLayout layout, CancellationToken ct) =>
        inner.CommitLayoutAsync(tamperCommittedLayout(layout), ct);

    public Task BringOnlineAsync(CancellationToken ct) => inner.BringOnlineAsync(ct);

    public Task DeleteLayoutAsync(CancellationToken ct) => inner.DeleteLayoutAsync(ct);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
