using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Storage;
using WinCopy.Infrastructure.Windows.Verification;

namespace WinCopy.Infrastructure.Windows.Tests.Verification;

// Partition table answers. Set Layout for a normal reply, or Failure to make the read throw.
internal sealed class FakeDriveLayoutQuery : IDriveLayoutQuery
{
    public DriveLayout? Layout { get; set; }

    public Exception? Failure { get; set; }

    public List<int> ReadDisks { get; } = [];

    public DriveLayout Read(int diskNumber)
    {
        ReadDisks.Add(diskNumber);

        if (Failure is not null)
        {
            throw Failure;
        }

        return Layout ?? throw new InvalidOperationException("No layout was faked.");
    }
}

// Records the volumes probed. Every path in Unreadable fails the probe.
internal sealed class FakeNtfsVolumeProbe : INtfsVolumeProbe
{
    public List<string> Probed { get; } = [];

    public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

    public void Probe(string volumeGuidPath)
    {
        Probed.Add(volumeGuidPath);

        if (Unreadable.Contains(volumeGuidPath))
        {
            throw new InvalidOperationException("Access is denied.");
        }
    }
}

// Paths that exist. Set Failure to make the check throw.
internal sealed class FakeFileExistence : IFileExistence
{
    public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Checked { get; } = [];

    public Exception? Failure { get; set; }

    public bool Exists(string path)
    {
        Checked.Add(path);

        if (Failure is not null)
        {
            throw Failure;
        }

        return Existing.Contains(path);
    }
}

// Every partition has the volume path VolumePathFor(partition), unless it is listed in Missing.
internal sealed class FakeVolumeResolver : IVolumePathResolver
{
    public HashSet<PartitionGuid> Missing { get; } = [];

    public static string VolumePathFor(PartitionGuid partition) => $@"\\?\Volume{{{partition.Value}}}\";

    public string GetVolumeGuidPath(PartitionGuid partition) =>
        Missing.Contains(partition)
            ? throw new PlatformException(PlatformErrorCode.DeviceNotFound, "No volume is mounted for the partition.")
            : VolumePathFor(partition);
}

// Mounts always succeed at Z:\, unless Failure is set. Every release is recorded.
internal sealed class FakeTemporaryMounter : ITemporaryMounter
{
    private const string MountRoot = @"Z:\";

    public List<PartitionGuid> Mounted { get; } = [];

    public List<string> Released { get; } = [];

    public Exception? Failure { get; set; }

    public Task<TemporaryMount> MountAsync(PartitionGuid partition, CancellationToken ct)
    {
        Mounted.Add(partition);

        if (Failure is not null)
        {
            return Task.FromException<TemporaryMount>(Failure);
        }

        return Task.FromResult(new TemporaryMount(MountRoot, new MountRelease(this)));
    }

    private sealed class MountRelease(FakeTemporaryMounter owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Released.Add(MountRoot);
            return ValueTask.CompletedTask;
        }
    }
}

// The fakes of one verifier, with a clean layout and every file present, so each test changes only what it needs.
internal sealed class VerifierHarness
{
    public VerifierHarness(ClonePlan plan)
    {
        Plan = plan;
        Layout.Layout = VerificationPlan.MatchingLayout(plan);
        Files.Existing.Add(BcdPath);
    }

    public const string BcdPath = @"Z:\EFI\Microsoft\Boot\BCD";

    public ClonePlan Plan { get; }

    public FakeDriveLayoutQuery Layout { get; } = new();

    public FakeNtfsVolumeProbe Ntfs { get; } = new();

    public FakeFileExistence Files { get; } = new();

    public FakeVolumeResolver Resolver { get; } = new();

    public FakeTemporaryMounter Mounter { get; } = new();

    public StructuralVerifier Verifier => new(Layout, Ntfs, Files, Resolver, Mounter);
}
