using WinClonerX.App.Localization;
using WinClonerX.Domain.Inventory;

namespace WinClonerX.App.Presentation;

/// <summary>Localized names of the domain enumerations shown in the interface.</summary>
public static class Labels
{
    /// <summary>Name of a partition role, as shown in partition bars and tooltips.</summary>
    public static string Role(PartitionRole role) => Loc.Get(role switch
    {
        PartitionRole.EfiSystem => "Partition_Role_EfiSystem",
        PartitionRole.MsReserved => "Partition_Role_MsReserved",
        PartitionRole.Windows => "Partition_Role_Windows",
        PartitionRole.Recovery => "Partition_Role_Recovery",
        PartitionRole.Data => "Partition_Role_Data",
        _ => "Partition_Role_Unsupported",
    });

    /// <summary>Name of the free (unallocated) space in a partition bar.</summary>
    public static string FreeSpace => Loc.Get("Partition_Role_Free");

    /// <summary>Connection label: internal or USB (and virtual for file-backed disks).</summary>
    public static string Bus(BusKind bus) => Loc.Get(bus switch
    {
        BusKind.Usb => "Disks_Bus_Usb",
        BusKind.FileBackedVirtual => "Disks_Bus_Virtual",
        _ => "Disks_Bus_Internal",
    });
}
