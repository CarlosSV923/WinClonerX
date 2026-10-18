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

/// <summary>Adds, removes and lists mount points. Win32 failures come back as codes, so the callers can decide.</summary>
internal interface IMountPointApi
{
    /// <summary>Mounts the volume at <paramref name="mountRoot"/>. Returns false with the Win32 error code on failure.</summary>
    bool TrySetMountPoint(string mountRoot, string volumeGuidPath, out int win32Error);

    /// <summary>Removes the mount point at <paramref name="mountRoot"/>. Returns false with the Win32 error code on failure.</summary>
    bool TryDeleteMountPoint(string mountRoot, out int win32Error);

    /// <summary>
    /// Lists the mount paths of the volume, in the order Windows returns them: drive roots such as <c>F:\</c> and folder
    /// mount points. Returns false with the Win32 error code on failure.
    /// </summary>
    bool TryGetVolumePathNames(string volumeGuidPath, out IReadOnlyList<string> mountPaths, out int win32Error);
}

/// <summary>
/// Mounts a partition's volume at a free drive letter for the duration of a step (R7, T067). Letters are tried from Z
/// down to D. If a letter fails, the next one is tried. A volume that already has a drive letter (Windows automount
/// gives one to the cloned Windows volume) keeps it, and that letter is used instead.
/// </summary>
/// <remarks>
/// Releasing the mount is the caller's job, through the returned <see cref="TemporaryMount"/>. Release is idempotent
/// and never throws. A failed removal is reported through the optional callback.
/// </remarks>
internal sealed class TemporaryMounter : ITemporaryMounter
{
    // Z down to D. A, B and C are never offered.
    private const string LetterOrder = "ZYXWVUTSRQPONMLKJIHGFED";

    // SetVolumeMountPoint fails with this code when the volume already has a drive letter, and another letter cannot be added.
    private const int ErrorInvalidParameter = 87;

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

        // The letter was not created here, so its release removes nothing.
        string? existingRoot = FindDriveLetterRoot(volumeGuidPath);
        if (existingRoot is not null)
        {
            return new TemporaryMount(existingRoot, new KeptMountRelease());
        }

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

            if (win32Error == ErrorInvalidParameter)
            {
                // Windows gave the volume a letter after the check above (a race with automount). Reuse it.
                string? racedRoot = FindDriveLetterRoot(volumeGuidPath);
                if (racedRoot is not null)
                {
                    return new TemporaryMount(racedRoot, new KeptMountRelease());
                }

                // No letter to reuse, so the error is about the volume, not the letter. The other letters would fail the same way.
                throw new PlatformException(
                    PlatformErrorCode.MountFailed,
                    $"Windows refused {mountRoot} (Win32 {win32Error}) and the volume has no drive letter to reuse. Stopped without trying the other letters.");
            }

            failures.Add($"{mountRoot} (Win32 {win32Error})");
        }

        throw new PlatformException(
            PlatformErrorCode.MountFailed,
            "No drive letter from Z: to D: could be used: " + string.Join(", ", failures));
    }

    /// <summary>
    /// The first drive-letter root (<c>X:\</c>) among the volume's mount paths. Folder mount points are ignored. A failed
    /// query counts as no letter, so the letter loop still runs.
    /// </summary>
    private string? FindDriveLetterRoot(string volumeGuidPath)
    {
        if (!_mountPoints.TryGetVolumePathNames(volumeGuidPath, out IReadOnlyList<string> mountPaths, out _))
        {
            return null;
        }

        return mountPaths.FirstOrDefault(IsDriveLetterRoot);
    }

    private static bool IsDriveLetterRoot(string path) =>
        path.Length == 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';

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

    /// <summary>Release for a drive letter that Windows gave the volume. It did not come from this mounter, so it removes nothing.</summary>
    private sealed class KeptMountRelease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
