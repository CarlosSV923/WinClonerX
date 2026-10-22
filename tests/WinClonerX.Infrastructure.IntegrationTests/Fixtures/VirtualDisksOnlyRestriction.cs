using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;

namespace WinClonerX.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// Process-wide restriction. A write is authorized only for a file-backed virtual disk that a fixture of this process
/// attached. It can only deny; it never overrides the system-disk rule in <see cref="DiskWriteGuard"/>.
/// </summary>
internal sealed class VirtualDisksOnlyRestriction : IDiskWriteRestriction
{
    public const string RestrictionName = "IntegrationTests.VirtualDisksOnly";

    public string Name => RestrictionName;

    public DiskWriteDecision Evaluate(DiskWriteCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.BusType != STORAGE_BUS_TYPE.BusTypeFileBackedVirtual)
        {
            return DiskWriteDecision.Deny(
                $"disk {candidate.DiskNumber} has bus type {candidate.BusType}, not FileBackedVirtual.");
        }

        if (!VirtualDiskRegistry.Contains(candidate.DiskNumber))
        {
            return DiskWriteDecision.Deny(
                $"disk {candidate.DiskNumber} was not attached by an integration test fixture.");
        }

        return DiskWriteDecision.Allow;
    }
}
