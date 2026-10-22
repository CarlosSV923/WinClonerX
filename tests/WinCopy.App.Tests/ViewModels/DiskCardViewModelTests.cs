using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Domain.Shared;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

public sealed class DiskCardViewModelTests
{
    [Fact]
    public void ShowsTheSerialAndGuidWhenBothAreAvailable()
    {
        using var _ = new EnglishCulture();
        var vm = new DiskCardViewModel(AppFixtures.TargetDisk(serial: "SN-1234"));

        vm.SerialText.ShouldBe(Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_Serial"), "SN-1234"));
        vm.GuidText.ShouldBe(Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_Guid"), "00000000-0000-0000-0000-000000000200"));
    }

    [Fact]
    public void ShowsTheGuidWhenTheSerialIsEmpty()
    {
        using var _ = new EnglishCulture();
        var vm = new DiskCardViewModel(AppFixtures.TargetDisk(serial: string.Empty));

        vm.SerialText.ShouldBe(Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_Serial"), Loc.Get("Disk_Serial_NotReported")));
        vm.GuidText.ShouldBe(Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_Guid"), "00000000-0000-0000-0000-000000000200"));
    }

    [Fact]
    public void ShowsTheModelWhenTheSerialAndGuidAreUnavailable()
    {
        using var _ = new EnglishCulture();
        const string model = "WDC WDS9 60G2G0C-00AJM0";
        var disk = AppFixtures.TargetDisk(serial: "0000000000000000", id: new DiskId(7, Guid.Empty), model: model);
        var vm = new DiskCardViewModel(disk);

        vm.SerialText.ShouldBe(Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_Serial"), Loc.Get("Disk_Serial_NotReported")));
        vm.GuidText.ShouldBe(Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_FallbackIdentifier"), model));
    }

    [Fact]
    public void ShowsDiskNumberWhenTheModelHasTooFewAlphanumericCharacters()
    {
        using var _ = new EnglishCulture();
        var disk = AppFixtures.TargetDisk(serial: string.Empty, id: new DiskId(123, Guid.Empty), model: "!!!");
        var vm = new DiskCardViewModel(disk);

        vm.GuidText.ShouldBe(Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_FallbackIdentifier"), "DISCO123"));
    }
}
