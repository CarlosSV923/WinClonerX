namespace WinCopy.Application.Ports;

/// <summary>Reconstruye la configuración de arranque del clon (R7).</summary>
public interface IBootConfigurator
{
    /// <summary>Ejecuta bcdboot sobre la raíz de Windows indicada, con la partición EFI indicada y firmware UEFI.</summary>
    Task RebuildAsync(string windowsRoot, string espRoot, CancellationToken ct);
}
