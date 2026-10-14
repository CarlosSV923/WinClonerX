using WinCopy.Domain.BootRepair;

namespace WinCopy.Application.Ports;

/// <summary>Edita el registro del clon sin arrancarlo: carga su SYSTEM, reescribe MountedDevices y lo descarga (R5).</summary>
public interface IOfflineRegistryEditor
{
    /// <summary>Aplica la reescritura de MountedDevices al Windows indicado. Debe descargar siempre el registro cargado.</summary>
    Task RewriteMountedDevicesAsync(string windowsRoot, MountedDevicesRewrite rewrite, CancellationToken ct);
}
