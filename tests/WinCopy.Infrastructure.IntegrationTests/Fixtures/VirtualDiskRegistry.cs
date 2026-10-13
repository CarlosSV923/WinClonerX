namespace WinCopy.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// Disk numbers that the fixtures of this process have attached. <see cref="VirtualDisksOnlyRestriction"/> only
/// authorizes writes to disks in this set.
/// </summary>
internal static class VirtualDiskRegistry
{
    private static readonly object _gate = new();
    private static readonly HashSet<int> _attachedDiskNumbers = [];

    public static void Register(int diskNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

        lock (_gate)
        {
            _attachedDiskNumbers.Add(diskNumber);
        }
    }

    public static void Unregister(int diskNumber)
    {
        lock (_gate)
        {
            _attachedDiskNumbers.Remove(diskNumber);
        }
    }

    public static bool Contains(int diskNumber)
    {
        lock (_gate)
        {
            return _attachedDiskNumbers.Contains(diskNumber);
        }
    }
}
