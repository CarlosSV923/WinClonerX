using WinClonerX.Domain.Inventory;

namespace WinClonerX.App.Controls;

/// <summary>Maps partition roles to the theme brush keys used by the partition bars.</summary>
public static class PartitionVisuals
{
    public const string FreeBrushKey = "Brush.Partition.Free";
    public const string UsedBrushKey = "Brush.Partition.Used";
    public const string SeparatorBrushKey = "Brush.Surface";

    public static string BrushKeyFor(PartitionRole role) => role switch
    {
        PartitionRole.EfiSystem => "Brush.Partition.Efi",
        PartitionRole.MsReserved => "Brush.Partition.Msr",
        PartitionRole.Windows => "Brush.Partition.Windows",
        PartitionRole.Recovery => "Brush.Partition.Recovery",
        PartitionRole.Data => "Brush.Partition.Data",
        _ => "Brush.Partition.Msr",
    };
}
