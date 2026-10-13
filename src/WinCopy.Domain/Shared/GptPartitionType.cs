namespace WinCopy.Domain.Shared;

/// <summary>GUID que identifica el tipo de una partición GPT.</summary>
public readonly record struct GptPartitionType(Guid Value)
{
    /// <summary>Partición de sistema EFI.</summary>
    public static GptPartitionType EfiSystem => new(new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B"));

    /// <summary>Partición reservada de Microsoft (MSR).</summary>
    public static GptPartitionType MicrosoftReserved => new(new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE"));

    /// <summary>Partición de datos básicos.</summary>
    public static GptPartitionType BasicData => new(new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"));

    /// <summary>Partición de recuperación de Windows.</summary>
    public static GptPartitionType WindowsRecovery => new(new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC"));

    /// <summary>Metadatos de LDM (gestor de volúmenes lógicos).</summary>
    public static GptPartitionType LdmMetadata => new(new Guid("5808C8AA-7E8F-42E0-85D2-E1E90434CFB3"));

    /// <summary>Datos de LDM (gestor de volúmenes lógicos).</summary>
    public static GptPartitionType LdmData => new(new Guid("AF9B60A0-1431-4F62-BC68-3311714A69AD"));

    /// <summary>Espacios de almacenamiento (Storage Spaces).</summary>
    public static GptPartitionType StorageSpaces => new(new Guid("E75CAF8F-F680-4CEE-AFA3-B001E56EFC2D"));

    /// <summary>Indica si el tipo corresponde a LDM (metadatos o datos).</summary>
    public bool IsLdm => Value == LdmMetadata.Value || Value == LdmData.Value;
}
