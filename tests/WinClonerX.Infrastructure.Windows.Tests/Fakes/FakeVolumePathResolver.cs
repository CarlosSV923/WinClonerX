using WinClonerX.Application.Errors;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Storage;

namespace WinClonerX.Infrastructure.Windows.Tests.Fakes;

/// <summary>Returns a fixed volume GUID path, or throws the given failure.</summary>
internal sealed class FakeVolumePathResolver(string volumeGuidPath, PlatformException? failure = null) : IVolumePathResolver
{
    public PartitionGuid? LastPartition { get; private set; }

    public string GetVolumeGuidPath(PartitionGuid partition)
    {
        LastPartition = partition;

        if (failure is not null)
        {
            throw failure;
        }

        return volumeGuidPath;
    }
}
