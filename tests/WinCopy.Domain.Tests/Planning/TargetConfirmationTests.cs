using Shouldly;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;
using static WinCopy.Domain.Tests.Planning.PlanningFixtures;

namespace WinCopy.Domain.Tests.Planning;

public sealed class TargetConfirmationTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpectedSuffixIsTheLastFourCharactersOfTheSerial()
    {
        TargetConfirmation.ExpectedSuffix(TargetDisk(serial: "WD-ABCDEF1234")).ShouldBe("1234");
    }

    [Fact]
    public void ExpectedSuffixIsUpperCased()
    {
        TargetConfirmation.ExpectedSuffix(TargetDisk(serial: "wd-abcdef12ab")).ShouldBe("12AB");
    }

    [Fact]
    public void ExpectedSuffixIgnoresSurroundingWhitespace()
    {
        TargetConfirmation.ExpectedSuffix(TargetDisk(serial: "  SN-99XY  ")).ShouldBe("99XY");
    }

    [Fact]
    public void ExpectedSuffixOfAShortSerialIsTheWholeSerial()
    {
        TargetConfirmation.ExpectedSuffix(TargetDisk(serial: "a7")).ShouldBe("A7");
    }

    [Fact]
    public void EmptySerialFallsBackToTheLastFourHexDigitsOfTheDiskGuid()
    {
        var disk = TargetDisk(serial: string.Empty, id: new DiskId(1, new Guid("6F1E0B2C-3D4A-4B5C-8D6E-7F8091A2B3C4")));

        TargetConfirmation.ExpectedSuffix(disk).ShouldBe("B3C4");
    }

    [Fact]
    public void WhitespaceOnlySerialFallsBackToTheDiskGuid()
    {
        var disk = TargetDisk(serial: "   ", id: new DiskId(1, new Guid("6F1E0B2C-3D4A-4B5C-8D6E-7F8091A2B3C4")));

        TargetConfirmation.ExpectedSuffix(disk).ShouldBe("B3C4");
    }

    [Fact]
    public void MatchesIgnoresCase()
    {
        var disk = TargetDisk(serial: "SN-ABCD12ab");

        new TargetConfirmation(disk.Id, "12AB", At).Matches(disk).ShouldBeTrue();
        new TargetConfirmation(disk.Id, "12ab", At).Matches(disk).ShouldBeTrue();
    }

    [Fact]
    public void MatchesRejectsAWrongSuffix()
    {
        var disk = TargetDisk(serial: "SN-ABCD1234");

        new TargetConfirmation(disk.Id, "1235", At).Matches(disk).ShouldBeFalse();
    }

    [Fact]
    public void MatchesRejectsAnEmptySuffix()
    {
        var disk = TargetDisk(serial: "SN-ABCD1234");

        new TargetConfirmation(disk.Id, string.Empty, At).Matches(disk).ShouldBeFalse();
    }

    [Fact]
    public void MatchesUsesTheGuidSuffixWhenTheSerialIsEmpty()
    {
        var disk = TargetDisk(serial: string.Empty, id: new DiskId(1, new Guid("6F1E0B2C-3D4A-4B5C-8D6E-7F8091A2B3C4")));

        new TargetConfirmation(disk.Id, "b3c4", At).Matches(disk).ShouldBeTrue();
        new TargetConfirmation(disk.Id, "0000", At).Matches(disk).ShouldBeFalse();
    }

    [Fact]
    public void MatchesRejectsAConfirmationForAnotherDisk()
    {
        var disk = TargetDisk(serial: "SN-ABCD1234");
        var otherDisk = TargetDisk(serial: "SN-ABCD1234", id: OtherTargetId);

        new TargetConfirmation(otherDisk.Id, "1234", At).Matches(disk).ShouldBeFalse();
    }

    // ----- Números de serie de adaptadores USB que informan ceros (puentes USB) -----

    private static readonly DiskId GuidDisk = new(1, new Guid("6F1E0B2C-3D4A-4B5C-8D6E-7F8091A2B3C4"));

    [Fact]
    public void AnAllZeroSerialFallsBackToTheDiskGuid()
    {
        var disk = TargetDisk(serial: "0000000000000000", id: GuidDisk);

        TargetConfirmation.ExpectedSuffix(disk).ShouldBe("B3C4");
        TargetConfirmation.UsesGuidFallback(disk).ShouldBeTrue();
    }

    [Fact]
    public void AllZerosWithATrailingDotFallBackToTheDiskGuid()
    {
        var disk = TargetDisk(serial: " 0000. ", id: GuidDisk);

        TargetConfirmation.ExpectedSuffix(disk).ShouldBe("B3C4");
        TargetConfirmation.UsesGuidFallback(disk).ShouldBeTrue();
    }

    [Fact]
    public void ASerialOfOnlyDotsFallsBackToTheDiskGuid()
    {
        var disk = TargetDisk(serial: "...", id: GuidDisk);

        TargetConfirmation.UsesGuidFallback(disk).ShouldBeTrue();
        TargetConfirmation.ExpectedSuffix(disk).ShouldBe("B3C4");
    }

    [Fact]
    public void ASerialWithANonZeroCharacterIsStillUsed()
    {
        var disk = TargetDisk(serial: "0000A0", id: GuidDisk);

        TargetConfirmation.UsesGuidFallback(disk).ShouldBeFalse();
        TargetConfirmation.ExpectedSuffix(disk).ShouldBe("00A0");
    }

    [Fact]
    public void AZeroSerialIsNotConfirmedWithZeros()
    {
        var disk = TargetDisk(serial: "0000000000000000", id: GuidDisk);

        new TargetConfirmation(disk.Id, "0000", At).Matches(disk).ShouldBeFalse();
        new TargetConfirmation(disk.Id, "b3c4", At).Matches(disk).ShouldBeTrue();
    }
}
