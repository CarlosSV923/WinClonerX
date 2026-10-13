using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de ITemporaryMounter que cuenta los montajes activos y las liberaciones.</summary>
public sealed class FakeTemporaryMounter : FakeBase, ITemporaryMounter
{
    /// <summary>Raíz que devuelve MountAsync.</summary>
    public string RootToReturn { get; set; } = @"C:\WinCopy\Mount\";

    /// <summary>Particiones montadas, en orden.</summary>
    public List<PartitionGuid> Mounted { get; } = [];

    /// <summary>Montajes que todavía no se han liberado.</summary>
    public int ActiveMounts { get; private set; }

    /// <summary>Número de liberaciones de montajes.</summary>
    public int ReleaseCount { get; private set; }

    /// <summary>Monta la partición y devuelve un montaje cuya liberación se cuenta.</summary>
    public Task<TemporaryMount> MountAsync(PartitionGuid partition, CancellationToken ct) =>
        Reply(nameof(MountAsync), () =>
        {
            Mounted.Add(partition);
            ActiveMounts++;
            return new TemporaryMount(RootToReturn, new MountHandle(this));
        }, ct);

    private sealed class MountHandle(FakeTemporaryMounter owner) : IAsyncDisposable
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                owner.ActiveMounts--;
                owner.ReleaseCount++;
                owner.Calls.Add("Release");
            }

            return ValueTask.CompletedTask;
        }
    }
}
