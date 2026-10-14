using WinCopy.Domain.Inventory;

namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>Reports the firmware type the machine booted with.</summary>
internal interface IFirmwareInfo
{
    /// <summary>UEFI, BIOS, or Unknown when the OS cannot tell.</summary>
    FirmwareType GetFirmwareType();
}
