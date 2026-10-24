namespace WinClonerX.Application.Ports;

/// <summary>Reconstruye la configuración de arranque del clon (R7).</summary>
public interface IBootConfigurator
{
    /// <summary>
    /// Deja el arranque del clon apuntando a sus propios volúmenes. Si la partición EFI ya trae un almacén BCD con cargador por
    /// defecto (copiado en bruto del origen), lo reapunta en su sitio sin añadir entradas. Si no, ejecuta bcdboot sobre la raíz
    /// de Windows indicada, con la partición EFI indicada y firmware UEFI. <paramref name="recoveryRoot"/> es la raíz montada de
    /// la partición de recuperación del clon, o <c>null</c> si el plan no tiene una; sin ella no se reapunta WinRE en el BCD.
    /// </summary>
    Task RebuildAsync(string windowsRoot, string espRoot, string? recoveryRoot, CancellationToken ct);
}
