using Shouldly;
using WinClonerX.Infrastructure.Windows.Boot;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Boot;

// Pure parsing. No process is started and nothing is written.
public sealed class BcdEnumParserTests
{
    private const string LoaderGuid = "{4c2e0d7a-3f1b-4a8e-9b6d-2f7a1c3e5b01}";
    private const string OptionsGuid = "{f0e1d2c3-b4a5-4968-8776-655443322110}";

    [Fact]
    public void ParsesAnEnglishHeaderAndItsElements()
    {
        string output = Lines(
            "Windows Boot Loader",
            "-------------------",
            $"identifier              {LoaderGuid}",
            @"device                  partition=\Device\HarddiskVolume2",
            "description             Windows 11",
            "hypervisorlaunchtype    Auto",
            "recoverysequence        {a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d}");

        BcdEntry entry = BcdEnumParser.Parse(output);

        entry.Identifier.ShouldBe(LoaderGuid);
        entry.ElementOrNull("device").ShouldBe(@"partition=\Device\HarddiskVolume2");
        entry.ElementOrNull("description").ShouldBe("Windows 11");
        entry.ElementOrNull("hypervisorlaunchtype").ShouldBe("Auto");
    }

    [Fact]
    public void SpanishHeadersGiveTheSameEnglishElementNames()
    {
        string english = Lines(
            "Windows Boot Loader",
            "-------------------",
            $"identifier              {LoaderGuid}",
            "locale                  es-MX",
            @"osdevice                partition=\Device\HarddiskVolume2");

        string spanish = Lines(
            "Cargador de arranque de Windows",
            "--------------------------------",
            $"identificador           {LoaderGuid}",
            "locale                  es-MX",
            @"osdevice                partition=\Device\HarddiskVolume2");

        BcdEntry fromEnglish = BcdEnumParser.Parse(english);
        BcdEntry fromSpanish = BcdEnumParser.Parse(spanish);

        fromSpanish.Identifier.ShouldBe(fromEnglish.Identifier);
        fromSpanish.Elements.ShouldBe(fromEnglish.Elements);
    }

    [Fact]
    public void AnEntryWithoutResumeObjectReportsNoValueForIt()
    {
        string output = Lines(
            "Windows Boot Loader",
            "-------------------",
            $"identifier              {LoaderGuid}",
            "description             Windows 11");

        BcdEnumParser.Parse(output).ElementOrNull("resumeobject").ShouldBeNull();
    }

    [Fact]
    public void MultiValuedElementsKeepTheirFirstValueAndSkipTheContinuationLines()
    {
        string output = Lines(
            "Windows Boot Manager",
            "--------------------",
            "identifier              {9dea862c-5cdd-4e70-acc1-f32b344d4795}",
            $"displayorder            {LoaderGuid}",
            $"                        {OptionsGuid}",
            "timeout                 30");

        BcdEntry entry = BcdEnumParser.Parse(output);

        entry.ElementOrNull("displayorder").ShouldBe(LoaderGuid);
        entry.ElementOrNull("timeout").ShouldBe("30");
    }

    [Fact]
    public void AnEntryWhoseFirstLineIsNotAnIdentifierIsMalformed()
    {
        string output = Lines(
            "Windows Boot Loader",
            "-------------------",
            "device                  partition=C:");

        Should.Throw<FormatException>(() => BcdEnumParser.Parse(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Error: the boot configuration data store could not be opened.")]
    [InlineData("identifier not-a-braced-value")]
    public void OutputWithoutAnIdentifierIsMalformed(string output) =>
        Should.Throw<FormatException>(() => BcdEnumParser.Parse(output));

    [Fact]
    public void TwoEntriesInOneAnswerAreMalformed()
    {
        string output = Lines(
            "Windows Boot Manager",
            "--------------------",
            "identifier              {9dea862c-5cdd-4e70-acc1-f32b344d4795}",
            "timeout                 30",
            string.Empty,
            "Windows Boot Loader",
            "-------------------",
            $"identifier              {LoaderGuid}",
            "timeout                 30");

        Should.Throw<FormatException>(() => BcdEnumParser.Parse(output));
    }

    [Fact]
    public void ARepeatedElementIsMalformed()
    {
        string output = Lines(
            "Windows Boot Loader",
            "-------------------",
            $"identifier              {LoaderGuid}",
            @"device                  partition=C:",
            @"device                  partition=D:");

        Should.Throw<FormatException>(() => BcdEnumParser.Parse(output));
    }

    [Fact]
    public void ReadsARamdiskDeviceAndItsOptionsGuid()
    {
        RamdiskDevice ramdisk = BcdEnumParser.ParseRamdiskDevice(
            $@"ramdisk=[\Device\HarddiskVolume4]\Recovery\WindowsRE\Winre.wim,{OptionsGuid}");

        ramdisk.WimPath.ShouldBe(@"\Recovery\WindowsRE\Winre.wim");
        ramdisk.OptionsIdentifier.ShouldBe(OptionsGuid);
    }

    [Fact]
    public void ARamdiskPathWithSpacesIsKeptWhole()
    {
        RamdiskDevice ramdisk = BcdEnumParser.ParseRamdiskDevice(
            $@"ramdisk=[\Device\HarddiskVolume4]\Recovery Files\Windows RE\Winre.wim,{OptionsGuid}");

        ramdisk.WimPath.ShouldBe(@"\Recovery Files\Windows RE\Winre.wim");
        ramdisk.OptionsIdentifier.ShouldBe(OptionsGuid);
    }

    [Theory]
    [InlineData(@"partition=\Device\HarddiskVolume4")]
    [InlineData(@"ramdisk=[\Device\HarddiskVolume4]\Recovery\WindowsRE\Winre.wim")]
    [InlineData(@"ramdisk=[\Device\HarddiskVolume4]Winre.wim,{f0e1d2c3-b4a5-4968-8776-655443322110}")]
    [InlineData(@"ramdisk=[\Device\HarddiskVolume4]\Recovery\Winre.wim,not-a-guid")]
    [InlineData(@"ramdisk=[\Device\HarddiskVolume4")]
    [InlineData(@"ramdisk=[\Device\HarddiskVolume4]\,{f0e1d2c3-b4a5-4968-8776-655443322110}")]
    public void MalformedRamdiskDevicesAreRefused(string device) =>
        Should.Throw<FormatException>(() => BcdEnumParser.ParseRamdiskDevice(device));

    [Theory]
    [InlineData("{4c2e0d7a-3f1b-4a8e-9b6d-2f7a1c3e5b01}", true)]
    [InlineData("{current}", false)]
    [InlineData("4c2e0d7a-3f1b-4a8e-9b6d-2f7a1c3e5b01", false)]
    [InlineData("{4c2e0d7a-3f1b-4a8e-9b6d-2f7a1c3e5b0}", false)]
    public void OnlyBracedGuidsAreIdentifiers(string value, bool expected) =>
        BcdEnumParser.IsGuid(value).ShouldBe(expected);

    private static string Lines(params string[] lines) => string.Join("\r\n", lines);
}
