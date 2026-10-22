using System.ComponentModel;
using WinClonerX.Application.Errors;

namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>
/// Traduce los errores de E/S sobre los discos a un <see cref="PlatformErrorCode"/> estable (US3, FR-024).
/// <para>
/// Dos familias de errores de Win32 (winerror.h):
/// </para>
/// <list type="bullet">
/// <item>
/// <description>Desconexión o desaparición del dispositivo: ERROR_NOT_READY (21), ERROR_NO_SUCH_DEVICE (433),
/// ERROR_DEV_NOT_EXIST (55), ERROR_FILE_INVALID (1006, el volumen se ha desmontado debajo de la lectura) y
/// ERROR_DEVICE_NOT_CONNECTED (1167). Se traducen a <see cref="PlatformErrorCode.SourceDeviceRemoved"/> o a
/// <see cref="PlatformErrorCode.TargetDeviceRemoved"/>, según el lado de la copia.</description>
/// </item>
/// <item>
/// <description>Fallo físico o de transferencia: ERROR_CRC (23), ERROR_SECTOR_NOT_FOUND (27), ERROR_READ_FAULT (30),
/// ERROR_WRITE_FAULT (29), ERROR_IO_DEVICE (1117) y ERROR_SEM_TIMEOUT (121). En el origen significan que el disco
/// puede tener problemas físicos (<see cref="PlatformErrorCode.SourceReadError"/>); en el destino, que no se ha podido
/// escribir (<see cref="PlatformErrorCode.TargetWriteError"/>).</description>
/// </item>
/// </list>
/// <para>
/// Cualquier otro error conserva el código que tenía antes de esta clasificación: lectura del origen o escritura del destino.
/// Solo se clasifican las excepciones de Win32 (<see cref="Win32Exception"/>); el resto no tiene código nativo que leer.
/// </para>
/// </summary>
internal static class IoErrorClassifier
{
    // Códigos de Win32 de la familia de desconexión. Los demás códigos de la lista de la documentación son fallos de transferencia.
    private static readonly int[] DeviceGoneErrors =
    [
        21,   // ERROR_NOT_READY
        55,   // ERROR_DEV_NOT_EXIST
        433,  // ERROR_NO_SUCH_DEVICE
        1006, // ERROR_FILE_INVALID
        1167, // ERROR_DEVICE_NOT_CONNECTED
    ];

    /// <summary>Código para un error de lectura del origen (disco de Windows o instantánea).</summary>
    public static PlatformErrorCode ForSourceRead(Exception exception) =>
        IsDeviceGone(exception) ? PlatformErrorCode.SourceDeviceRemoved : PlatformErrorCode.SourceReadError;

    /// <summary>Código para un error de lectura o escritura del destino.</summary>
    public static PlatformErrorCode ForTargetIo(Exception exception) =>
        IsDeviceGone(exception) ? PlatformErrorCode.TargetDeviceRemoved : PlatformErrorCode.TargetWriteError;

    /// <summary>Verdadero si la excepción es un error de Win32 que indica que el dispositivo ya no está.</summary>
    public static bool IsDeviceGone(Exception exception) =>
        exception is Win32Exception win32 && IsDeviceGone(win32.NativeErrorCode);

    /// <summary>Verdadero si el código nativo indica que el dispositivo ya no está.</summary>
    internal static bool IsDeviceGone(int nativeError) => Array.IndexOf(DeviceGoneErrors, nativeError) >= 0;
}
