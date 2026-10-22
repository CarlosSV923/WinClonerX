using System.Runtime.InteropServices;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;

namespace WinClonerX.Infrastructure.Windows.Platform;

/// <summary>Comprueba si un disco sigue presente. La implementación Win32 abre <c>\\.\PhysicalDriveN</c>; los tests usan un fake.</summary>
internal interface IDiskPresenceProbe
{
    /// <summary>Intenta abrir el disco sin acceso. Devuelve el código de error de Win32 si no se puede abrir, o null si está presente.</summary>
    int? OpenError(int diskNumber);
}

/// <summary>
/// Abre <c>\\.\PhysicalDriveN</c> con acceso cero, que no lee ni escribe nada, solo para saber si el disco existe. Así lee
/// WinClonerX también la distribución del disco (Win32DriveLayoutReader).
/// </summary>
internal sealed class Win32DiskPresenceProbe : IDiskPresenceProbe
{
    public int? OpenError(int diskNumber)
    {
        using SafeDeviceHandle handle = Kernel32.CreateFileW(
            PhysicalDisk.PathFor(diskNumber),
            0,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        return handle.IsInvalid ? Marshal.GetLastPInvokeError() : null;
    }
}

/// <summary>
/// Decide qué discos vigilados han desaparecido. Se llama cada vez que el sistema notifica un cambio de interfaz de disco: la
/// notificación no dice qué disco es, así que cada disco vigilado se vuelve a abrir. Un disco se comunica una sola vez.
/// </summary>
/// <remarks>
/// No toca ningún disco para escribir ni para leer datos; no llama a la API de notificaciones, de modo que puede probarse con
/// un <see cref="IDiskPresenceProbe"/> falso.
/// </remarks>
internal sealed class DiskRemovalTracker
{
    // Errores con los que Windows indica que el disco ya no existe. ERROR_FILE_NOT_FOUND y ERROR_PATH_NOT_FOUND son propios de
    // abrir la ruta del disco; el resto son los de la desconexión de IoErrorClassifier.
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    private readonly object _gate = new();
    private readonly int[] _watched;
    private readonly IDiskPresenceProbe _probe;
    private readonly Action<DeviceChange> _onRemoved;
    private readonly HashSet<int> _reported = [];
    private bool _stopped;

    internal DiskRemovalTracker(IReadOnlyCollection<int> diskNumbers, IDiskPresenceProbe probe, Action<DeviceChange> onRemoved)
    {
        ArgumentNullException.ThrowIfNull(diskNumbers);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(onRemoved);

        _watched = diskNumbers.Distinct().ToArray();
        _probe = probe;
        _onRemoved = onRemoved;
    }

    /// <summary>Vuelve a comprobar los discos vigilados que aún no se han dado por retirados, y comunica los que faltan.</summary>
    internal void Check()
    {
        int[] pending;
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            pending = _watched.Where(number => !_reported.Contains(number)).ToArray();
        }

        foreach (int diskNumber in pending)
        {
            int? error = _probe.OpenError(diskNumber);
            if (error is not { } code || !IsAbsent(code))
            {
                continue;
            }

            lock (_gate)
            {
                if (_stopped)
                {
                    return;
                }

                if (!_reported.Add(diskNumber))
                {
                    continue;
                }
            }

            // Fuera del bloqueo: el suscriptor puede tomar sus propios bloqueos.
            _onRemoved(new DeviceChange(DeviceChangeKind.Removed, diskNumber));
        }
    }

    /// <summary>Deja de comunicar cambios. Una comprobación que ya estaba en marcha no comunica nada.</summary>
    internal void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
        }
    }

    /// <summary>Verdadero si el código indica que el disco ya no existe o no responde en absoluto.</summary>
    internal static bool IsAbsent(int nativeError) =>
        nativeError is ErrorFileNotFound or ErrorPathNotFound || IoErrorClassifier.IsDeviceGone(nativeError);
}
