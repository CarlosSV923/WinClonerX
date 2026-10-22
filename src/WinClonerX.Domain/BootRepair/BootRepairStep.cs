namespace WinClonerX.Domain.BootRepair;

/// <summary>Paso de la reparación de arranque, en el orden en que se ejecuta.</summary>
public enum BootRepairStep
{
    /// <summary>Reescribir MountedDevices del clon fuera de línea para que Windows monte el volumen como C:.</summary>
    RewriteMountedDevices,

    /// <summary>Crear la BCD del clon con bcdboot, apuntando a su volumen de Windows.</summary>
    RebuildBcd,

    /// <summary>Configurar WinRE con la partición de recuperación del clon. Solo si el origen la tenía.</summary>
    ReconfigureWinRe,
}
