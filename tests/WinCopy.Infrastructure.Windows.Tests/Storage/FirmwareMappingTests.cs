using Shouldly;
using WinCopy.Domain.Inventory;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.Storage;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Storage;

public sealed class FirmwareMappingTests
{
    // The Win32 enum is internal, so the cases are named as strings and parsed here.
    [Theory]
    [InlineData("Uefi", FirmwareType.Uefi)]
    [InlineData("Bios", FirmwareType.Bios)]
    [InlineData("Unknown", FirmwareType.Unknown)]
    [InlineData("Max", FirmwareType.Unknown)]
    public void MapsTheWin32FirmwareEnum(string raw, FirmwareType expected) =>
        Win32FirmwareInfo.Map(Enum.Parse<FIRMWARE_TYPE>(raw)).ShouldBe(expected);
}
