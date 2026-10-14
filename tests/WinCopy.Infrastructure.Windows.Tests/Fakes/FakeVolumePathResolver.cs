using WinCopy.Application.Errors;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Storage;

namespace WinCopy.Infrastructure.Windows.Tests.Fakes;

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
