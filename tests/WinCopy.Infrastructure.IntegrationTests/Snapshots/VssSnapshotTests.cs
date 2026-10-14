using System.ComponentModel;
using System.Management;
using System.Text;
using Shouldly;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.IntegrationTests.Fixtures;
using WinCopy.Infrastructure.IntegrationTests.Spikes;
using WinCopy.Infrastructure.Windows.RawIo;
using WinCopy.Infrastructure.Windows.Snapshots;
using WinCopy.Infrastructure.Windows.Storage;
using Xunit;

namespace WinCopy.Infrastructure.IntegrationTests.Snapshots;

/// <summary>
/// Task T043. Real VSS on the NTFS volume of a VHDX: snapshot, allocation map and read, then deletion when the set is
/// disposed. The volume is reached only through <see cref="VirtualDiskFixture"/> and <see cref="ReferenceSystemDisk"/>.
/// Needs an elevated process:
/// <c>dotnet test tests/WinCopy.Infrastructure.IntegrationTests --filter "FullyQualifiedName~VssSnapshotTests"</c>
/// </summary>
[Trait("Category", "Integration")]
public sealed class VssSnapshotTests
{
    private const int SectorProbeBytes = 4096;
    private const string NtfsOemId = "NTFS    ";
    private const string ShadowCopyDevicePrefix = @"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy";

    private static readonly ByteSize DiskSize = ByteSize.FromGiB(2);
    private static readonly ByteSize MinimumUsed = ByteSize.FromMiB(20);

    [Fact]
    public async Task SnapshotOfVhdxNtfsVolumeIsReadableAndDeleted()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using var fixture = new VirtualDiskFixture();
        ReferenceDisk reference = await ReferenceSystemDisk.CreateAsync(
            fixture, VirtualDiskStorageOps.Default, DiskSize, dataFiles: 20);
        var partition = new PartitionGuid(reference.Windows.GptGuid);

        string devicePath;
        await using (ISnapshotSet set = await CreateService().CreateAsync([partition], ct))
        {
            devicePath = ((VssSnapshotSet)set).DevicePaths[partition];
            devicePath.ShouldStartWith(ShadowCopyDevicePrefix);
            Log($"Snapshot of the Windows volume at {devicePath}.");

            using (IBlockSource source = set.OpenVolume(partition))
            {
                source.Length.Bytes.ShouldBeGreaterThan(0L);

                VolumeAllocation allocation = await source.GetAllocationAsync(ct);
                allocation.UsedRanges.ShouldNotBeEmpty();
                allocation.TotalUsed.Bytes.ShouldBeGreaterThanOrEqualTo(MinimumUsed.Bytes);

                using var probe = new AlignedBuffer(SectorProbeBytes, (nuint)SectorProbeBytes);
                int read = await source.ReadAsync(0, probe.Memory, ct);

                read.ShouldBe(SectorProbeBytes);
                Encoding.ASCII.GetString(probe.Span.Slice(3, NtfsOemId.Length)).ShouldBe(NtfsOemId);
            }
        }

        AssertSnapshotDeleted(devicePath);
    }

    [Fact]
    public async Task TwoSnapshotSessionsInOneProcess()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using var fixture = new VirtualDiskFixture();
        ReferenceDisk reference = await ReferenceSystemDisk.CreateAsync(
            fixture, VirtualDiskStorageOps.Default, DiskSize, dataFiles: 1);
        var partition = new PartitionGuid(reference.Windows.GptGuid);

        // Each session creates its own COM backup object and releases it on dispose. A second session in the same process
        // fails if the first one leaked a COM reference.
        var devicePaths = new List<string>(capacity: 2);
        for (int session = 1; session <= 2; session++)
        {
            string devicePath = await CreateAndDisposeSnapshotAsync(partition, ct);
            Log($"Session {session}: snapshot {devicePath} created and disposed.");
            devicePaths.Add(devicePath);
        }

        devicePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(2);
        foreach (string devicePath in devicePaths)
        {
            AssertSnapshotDeleted(devicePath);
        }
    }

    private static async Task<string> CreateAndDisposeSnapshotAsync(PartitionGuid partition, CancellationToken ct)
    {
        string devicePath;
        await using (ISnapshotSet set = await CreateService().CreateAsync([partition], ct))
        {
            devicePath = ((VssSnapshotSet)set).DevicePaths[partition];
            devicePath.ShouldStartWith(ShadowCopyDevicePrefix);
        }

        return devicePath;
    }

    private static VssSnapshotService CreateService() =>
        new(new StorageVolumePathResolver(), new ComVssBackupFactory(), message => Log($"VSS warning: {message}"));

    /// <summary>
    /// After disposal, Win32_ShadowCopy must no longer list the device, and the device must no longer open. The second check
    /// does not depend on how WMI lists backup-context snapshots.
    /// </summary>
    private static void AssertSnapshotDeleted(string devicePath)
    {
        CountShadowCopiesOnDevice(devicePath).ShouldBe(0, $"Win32_ShadowCopy still lists {devicePath}.");

        Should.Throw<Win32Exception>(
            () => new Win32SnapshotDevice(devicePath).Dispose(),
            $"The snapshot device {devicePath} still opens after disposal.");
    }

    private static int CountShadowCopiesOnDevice(string devicePath)
    {
        string query = "SELECT * FROM Win32_ShadowCopy WHERE DeviceObject = '" + EscapeWqlString(devicePath) + "'";

        using var searcher = new ManagementObjectSearcher(@"root\cimv2", query);
        using ManagementObjectCollection shadows = searcher.Get();
        return shadows.Count;
    }

    // WQL string literals escape the backslash and the single quote.
    private static string EscapeWqlString(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("'", @"\'", StringComparison.Ordinal);

    private static void Log(string message)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(message);
        Console.WriteLine(message);
    }
}
