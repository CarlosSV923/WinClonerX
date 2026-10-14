using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Platform;
using WinCopy.Infrastructure.Windows.Storage;

namespace WinCopy.Infrastructure.Windows.Tests.Platform;

/// <summary>Scripted power status. A failure is thrown from <see cref="GetStatusAsync"/>.</summary>
internal sealed class FakePowerManager : IPowerManager
{
    public PowerStatus Status { get; set; } = new(false, 100);

    public Exception? Failure { get; set; }

    public IDisposable PreventSleep() => new NoLease();

    public Task<PowerStatus> GetStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult(Status);
    }

    private sealed class NoLease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>Scripted service start mode. Null means the service is not installed.</summary>
internal sealed class FakeServiceCatalog : IServiceCatalog
{
    public ServiceStartKind? Kind { get; set; }

    public Exception? Failure { get; set; }

    public List<string> Asked { get; } = [];

    public ServiceStartKind? GetStartKind(string serviceName)
    {
        Asked.Add(serviceName);

        if (Failure is not null)
        {
            throw Failure;
        }

        return Kind;
    }
}

/// <summary>Scripted partition-to-volume mapping. A partition that is not mapped throws, as a missing mount point would.</summary>
internal sealed class FakeVolumePaths : IVolumePathResolver
{
    public Dictionary<PartitionGuid, string> Paths { get; } = [];

    public string GetVolumeGuidPath(PartitionGuid partition)
    {
        if (Paths.TryGetValue(partition, out string? path))
        {
            return path;
        }

        throw new PlatformException(PlatformErrorCode.DeviceNotFound, "No volume GUID path (simulated).");
    }
}

/// <summary>
/// Scripted lock probe. Paths not in <see cref="Results"/> lock successfully. A path in <see cref="Throwing"/> fails to open.
/// Every probed device path is recorded in <see cref="Probed"/>.
/// </summary>
internal sealed class FakeVolumeLockProbe : IVolumeLockProbe
{
    public Dictionary<string, bool> Results { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Throwing { get; } = new(StringComparer.Ordinal);

    public List<string> Probed { get; } = [];

    public bool CanLockExclusively(string volumeDevicePath)
    {
        Probed.Add(volumeDevicePath);

        if (Throwing.Contains(volumeDevicePath))
        {
            throw new InvalidOperationException("The volume cannot be opened (simulated).");
        }

        return !Results.TryGetValue(volumeDevicePath, out bool locked) || locked;
    }
}
