using System.Globalization;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Storage;

namespace WinCopy.Infrastructure.Windows.Boot;

/// <summary>Reports the drive roots already in use. The Win32 implementation calls <c>Environment.GetLogicalDrives</c>.</summary>
internal interface ILogicalDriveSource
{
    /// <summary>Drive roots such as <c>C:\</c>, as <c>Environment.GetLogicalDrives</c> returns them.</summary>
    IReadOnlyList<string> GetLogicalDrives();
}

/// <summary>Adds and removes mount points. Win32 failures come back as codes, so the callers can decide.</summary>
internal interface IMountPointApi
{
    /// <summary>Mounts the volume at <paramref name="mountRoot"/>. Returns false with the Win32 error code on failure.</summary>
    bool TrySetMountPoint(string mountRoot, string volumeGuidPath, out int win32Error);

    /// <summary>Removes the mount point at <paramref name="mountRoot"/>. Returns false with the Win32 error code on failure.</summary>
    bool TryDeleteMountPoint(string mountRoot, out int win32Error);
}

/// <summary>
/// Mounts a partition's volume at a free drive letter for the duration of a step (R7, T067). Letters are tried from Z
/// down to D. If a letter fails, the next one is tried.
/// </summary>
/// <remarks>
/// Releasing the mount is the caller's job, through the returned <see cref="TemporaryMount"/>. Release is idempotent
/// and never throws. A failed removal is reported through the optional callback.
/// </remarks>
internal sealed class TemporaryMounter : ITemporaryMounter
{
    // Z down to D. A, B and C are never offered.
    private const string LetterOrder = "ZYXWVUTSRQPONMLKJIHGFED";

    private readonly IVolumePathResolver _resolver;
    private readonly ILogicalDriveSource _drives;
    private readonly IMountPointApi _mountPoints;
    private readonly Action<string>? _onReleaseFailure;

    public TemporaryMounter(
        IVolumePathResolver resolver,
        ILogicalDriveSource drives,
        IMountPointApi mountPoints,
        Action<string>? onReleaseFailure = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(drives);
        ArgumentNullException.ThrowIfNull(mountPoints);

        _resolver = resolver;
        _drives = drives;
        _mountPoints = mountPoints;
        _onReleaseFailure = onReleaseFailure;
    }

    /// <inheritdoc />
    public Task<TemporaryMount> MountAsync(PartitionGuid partition, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() => Mount(partition), ct);
    }

    private TemporaryMount Mount(PartitionGuid partition)
    {
        string volumeGuidPath = _resolver.GetVolumeGuidPath(partition);
        HashSet<char> inUse = DriveLettersInUse();
        var failures = new List<string>();

        foreach (char letter in LetterOrder)
        {
            if (inUse.Contains(letter))
            {
                continue;
            }

            string mountRoot = $"{letter}:\\";
            if (_mountPoints.TrySetMountPoint(mountRoot, volumeGuidPath, out int win32Error))
            {
                return new TemporaryMount(mountRoot, new MountRelease(mountRoot, _mountPoints, _onReleaseFailure));
            }

            failures.Add($"{mountRoot} (Win32 {win32Error})");
        }

        throw new PlatformException(
            PlatformErrorCode.MountFailed,
            "No drive letter from Z: to D: could be used: " + string.Join(", ", failures));
    }

    private HashSet<char> DriveLettersInUse()
    {
        var letters = new HashSet<char>();
        foreach (string root in _drives.GetLogicalDrives())
        {
            if (!string.IsNullOrEmpty(root))
            {
                letters.Add(char.ToUpper(root[0], CultureInfo.InvariantCulture));
            }
        }

        return letters;
    }

    /// <summary>Removes the mount point the first time it is disposed. Later calls do nothing, and nothing throws.</summary>
    private sealed class MountRelease(string mountRoot, IMountPointApi mountPoints, Action<string>? onFailure) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1)
            {
                return ValueTask.CompletedTask;
            }

            try
            {
                if (!mountPoints.TryDeleteMountPoint(mountRoot, out int win32Error))
                {
                    Report($"Could not remove the temporary mount point {mountRoot} (Win32 {win32Error}).", onFailure);
                }
            }
            catch (Exception ex)
            {
                // Release must never throw: it runs in finally blocks, where a throw would hide the real error.
                Report($"Removing the temporary mount point {mountRoot} failed: {ex.Message}", onFailure);
            }

            return ValueTask.CompletedTask;
        }

        private static void Report(string message, Action<string>? onFailure)
        {
            try
            {
                onFailure?.Invoke(message);
            }
            catch (Exception)
            {
                // The callback is for diagnostics only. A failure inside it must not escape from Release.
            }
        }
    }
}
