using WinClonerX.Infrastructure.Windows.Boot;

namespace WinClonerX.Infrastructure.Windows.Tests.Boot;

// In-memory hive. Values are byte[] for REG_BINARY and string for any other kind. Nothing touches the registry.
internal sealed class FakeOfflineHive : IOfflineHive
{
    public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Deletes { get; } = [];

    public string? LoadedFrom { get; private set; }

    public string? MountName { get; private set; }

    public int LoadCalls { get; private set; }

    public int UnloadCalls { get; private set; }

    // Open keys seen at each unload. A correct edit has closed every key before it unloads.
    public List<int> OpenKeysAtUnload { get; } = [];

    public int OpenKeys { get; private set; }

    public bool IsLoaded { get; private set; }

    // Failure thrown by Load.
    public Exception? LoadFailure { get; set; }

    // Failure thrown by OpenMountedDevices.
    public Exception? OpenFailure { get; set; }

    // Failure thrown by SetBinary.
    public Exception? SetFailure { get; set; }

    // Unload results, used in order. Once empty, every unload succeeds.
    public Queue<int> UnloadResults { get; } = new();

    public void Load(string hivePath, string mountName)
    {
        LoadCalls++;
        LoadedFrom = hivePath;
        MountName = mountName;

        if (LoadFailure is not null)
        {
            throw LoadFailure;
        }

        IsLoaded = true;
    }

    public IMountedDevicesKey OpenMountedDevices(string mountName)
    {
        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        OpenKeys++;
        return new FakeKey(this);
    }

    public int Unload(string mountName)
    {
        UnloadCalls++;
        OpenKeysAtUnload.Add(OpenKeys);

        int status = UnloadResults.Count > 0 ? UnloadResults.Dequeue() : 0;
        if (status == 0)
        {
            IsLoaded = false;
        }

        return status;
    }

    private sealed class FakeKey(FakeOfflineHive hive) : IMountedDevicesKey
    {
        private bool _disposed;

        public IReadOnlyList<string> GetValueNames() => hive.Values.Keys.ToArray();

        public byte[]? GetBinary(string name) =>
            hive.Values.TryGetValue(name, out object? value) ? value as byte[] : null;

        public void Delete(string name)
        {
            hive.Deletes.Add(name);
            hive.Values.Remove(name);
        }

        public void SetBinary(string name, byte[] data)
        {
            if (hive.SetFailure is not null)
            {
                throw hive.SetFailure;
            }

            hive.Values[name] = data;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                hive.OpenKeys--;
            }
        }
    }
}

// Records the privileges requested. Set Failure to make the request fail.
internal sealed class FakePrivilegeEnabler : IPrivilegeEnabler
{
    public List<string> Requested { get; } = [];

    public Exception? Failure { get; set; }

    public void Enable(IReadOnlyList<string> privilegeNames)
    {
        Requested.AddRange(privilegeNames);

        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
