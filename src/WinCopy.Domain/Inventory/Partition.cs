using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Inventory;

/// <summary>Región de un disco con su número, posición, tipo GPT, identificador y volumen opcional.</summary>
public sealed record Partition(
    int Number,
    Extent Extent,
    GptPartitionType Type,
    PartitionGuid UniqueId,
    ulong Attributes,
    string Name,
    Volume? Volume)
{
    /// <summary>Rol de la partición, derivado de su tipo GPT y de si su volumen es el de Windows.</summary>
    public PartitionRole Role
    {
        get
        {
            if (Type == GptPartitionType.EfiSystem)
            {
                return PartitionRole.EfiSystem;
            }

            if (Type == GptPartitionType.MicrosoftReserved)
            {
                return PartitionRole.MsReserved;
            }

            if (Type == GptPartitionType.WindowsRecovery)
            {
                return PartitionRole.Recovery;
            }

            if (Type == GptPartitionType.BasicData)
            {
                return Volume?.IsWindowsVolume == true ? PartitionRole.Windows : PartitionRole.Data;
            }

            return PartitionRole.Unsupported;
        }
    }
}
