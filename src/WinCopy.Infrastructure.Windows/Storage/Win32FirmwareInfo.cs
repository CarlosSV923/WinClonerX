using WinCopy.Domain.Inventory;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>Win32 implementation of <see cref="IFirmwareInfo"/>, using <c>GetFirmwareType</c>.</summary>
internal sealed class Win32FirmwareInfo : IFirmwareInfo
{
    public FirmwareType GetFirmwareType()
    {
        // A failed call means the firmware type is unknown. The evaluator then blocks the clone.
        if (!Kernel32.GetFirmwareType(out FIRMWARE_TYPE raw))
        {
            return FirmwareType.Unknown;
        }

        return Map(raw);
    }

    /// <summary>Maps the Win32 enum. Anything other than UEFI or BIOS is Unknown.</summary>
    internal static FirmwareType Map(FIRMWARE_TYPE raw) => raw switch
    {
        FIRMWARE_TYPE.Uefi => FirmwareType.Uefi,
        FIRMWARE_TYPE.Bios => FirmwareType.Bios,
        _ => FirmwareType.Unknown,
    };
}
