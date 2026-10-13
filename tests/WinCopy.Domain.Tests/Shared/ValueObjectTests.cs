using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Shouldly;
using WinCopy.Domain.Shared;
using Xunit;

#pragma warning disable CA1716 // El espacio de pruebas refleja la carpeta Shared de la especificación.
namespace WinCopy.Domain.Tests.Shared;
#pragma warning restore CA1716

public sealed class ValueObjectTests
{
    private const long Mib = 1024L * 1024;

    [Fact]
    public void ByteSizeRejectsNegativeValues()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ByteSize(-1));
    }

    [Fact]
    public void ByteSizeAcceptsZero()
    {
        new ByteSize(0).Bytes.ShouldBe(0);
    }

    [Fact]
    public void ByteSizeAddsBytes()
    {
        (new ByteSize(2 * Mib) + new ByteSize(3 * Mib)).ShouldBe(new ByteSize(5 * Mib));
    }

    [Fact]
    public void ByteSizeSubtractsBytes()
    {
        (new ByteSize(5 * Mib) - new ByteSize(2 * Mib)).ShouldBe(new ByteSize(3 * Mib));
    }

    [Fact]
    public void ByteSizeSubtractionThatWouldBeNegativeThrows()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _ = new ByteSize(1) - new ByteSize(2));
    }

    [Fact]
    public void ByteSizeComparesByBytes()
    {
        (new ByteSize(1) < new ByteSize(2)).ShouldBeTrue();
        (new ByteSize(2) > new ByteSize(1)).ShouldBeTrue();
        (new ByteSize(2) <= new ByteSize(2)).ShouldBeTrue();
        (new ByteSize(3) >= new ByteSize(4)).ShouldBeFalse();
    }

    [Fact]
    public void ByteSizeFromGiBConvertsToBytes()
    {
        ByteSize.FromGiB(1.5).Bytes.ShouldBe(1_610_612_736L);
    }

    [Fact]
    public void ByteSizeFromGiBRejectsNegativeValues()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ByteSize.FromGiB(-1));
    }

    [Fact]
    public void ByteSizeFromGiBRejectsNaN()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ByteSize.FromGiB(double.NaN));
    }

    [Fact]
    public void ByteSizeFromMiBConvertsToBytes()
    {
        ByteSize.FromMiB(3).Bytes.ShouldBe(3 * Mib);
    }

    [Fact]
    public void ByteSizeFromMiBRejectsNegativeValues()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ByteSize.FromMiB(-1));
    }

    [Fact]
    public void ByteSizeToStringUsesBinaryUnits()
    {
        ByteSize.FromGiB(1.5).ToString().ShouldBe("1.50 GiB");
        ByteSize.FromMiB(512).ToString().ShouldBe("512.00 MiB");
        new ByteSize(1536).ToString().ShouldBe("1.50 KiB");
        new ByteSize(500).ToString().ShouldBe("500 B");
    }

    [Fact]
    public void ByteSizeToDecimalStringUsesPowersOf1000()
    {
        new ByteSize(1_500_000_000).ToDecimalString().ShouldBe("1.50 GB");
        new ByteSize(2_500_000).ToDecimalString().ShouldBe("2.50 MB");
        new ByteSize(999).ToDecimalString().ShouldBe("999 B");
    }

    [Fact]
    public void ByteSizeFormattingIgnoresTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            ByteSize.FromGiB(1.5).ToString().ShouldBe("1.50 GiB");
            ByteSize.FromGiB(1.5).ToDecimalString().ShouldBe("1.61 GB");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(-512)]
    [InlineData(2048)]
    public void SectorSizeRejectsSizesOtherThan512And4096(int bytes)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() => new SectorSize(bytes));
        exception.Message.ShouldContain("Vale 512 o 4096 en la v1");
    }

    [Fact]
    public void SectorSizePredefinedValuesAre512And4096()
    {
        SectorSize.Bytes512.Bytes.ShouldBe(512);
        SectorSize.Bytes4096.Bytes.ShouldBe(4096);
    }

    [Fact]
    public void ExtentEndIsOffsetPlusLength()
    {
        new Extent(1024, 512).End.ShouldBe(1536);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExtentRejectsNonPositiveLength(long length)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Extent(0, length));
    }

    [Fact]
    public void ExtentRejectsNegativeOffset()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Extent(-512, 512));
    }

    [Fact]
    public void ExtentCreateAcceptsOffsetAlignedToTheSector()
    {
        var extent = Extent.Create(4096, 8192, SectorSize.Bytes512);

        extent.Offset.ShouldBe(4096);
        extent.Length.ShouldBe(8192);
    }

    [Fact]
    public void ExtentCreateRejectsOffsetNotAlignedToTheSector()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() => Extent.Create(100, 512, SectorSize.Bytes512));
        exception.Message.ShouldContain("El desplazamiento está alineado al sector y la longitud es > 0");
    }

    [Fact]
    public void ExtentCreateChecksAlignmentAgainstThe4096Sector()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Extent.Create(512, 4096, SectorSize.Bytes4096));
    }

    [Fact]
    public void ExtentCreateRejectsNonPositiveLength()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Extent.Create(0, 0, SectorSize.Bytes512));
    }

    [Fact]
    public void ExtentIsAlignedToRequiresOffsetAndLengthToBeMultiples()
    {
        new Extent(Mib, Mib).IsAlignedTo(Mib).ShouldBeTrue();
        new Extent(512, Mib).IsAlignedTo(Mib).ShouldBeFalse();
        new Extent(0, Mib + 512).IsAlignedTo(Mib).ShouldBeFalse();
    }

    [Fact]
    public void ExtentIsAlignedToRejectsNonPositiveAlignment()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Extent(0, 512).IsAlignedTo(0));
    }

    [Fact]
    public void ExtentAdjacentExtentsDoNotOverlap()
    {
        var first = new Extent(0, 512);
        var second = new Extent(512, 512);

        first.Overlaps(second).ShouldBeFalse();
        second.Overlaps(first).ShouldBeFalse();
    }

    [Fact]
    public void ExtentPartiallyOverlappingExtentsOverlap()
    {
        var first = new Extent(0, 1024);
        var second = new Extent(512, 1024);

        first.Overlaps(second).ShouldBeTrue();
        second.Overlaps(first).ShouldBeTrue();
    }

    [Fact]
    public void ExtentContainedExtentOverlaps()
    {
        new Extent(0, 4096).Overlaps(new Extent(512, 512)).ShouldBeTrue();
    }

    [Fact]
    public void ExtentDisjointExtentsDoNotOverlap()
    {
        new Extent(0, 512).Overlaps(new Extent(1024, 512)).ShouldBeFalse();
    }

    [Fact]
    public void DiskIdIdentityIsTheDiskGuidNotTheNumber()
    {
        var guid = Id(7);

        new DiskId(1, guid).ShouldBe(new DiskId(7, guid));
        (new DiskId(1, guid) == new DiskId(7, guid)).ShouldBeTrue();
        (new DiskId(1, guid) != new DiskId(7, guid)).ShouldBeFalse();
        new DiskId(1, guid).GetHashCode().ShouldBe(new DiskId(7, guid).GetHashCode());
    }

    [Fact]
    public void DiskIdWithDifferentGuidsIsNotEqual()
    {
        (new DiskId(1, Id(1)) == new DiskId(1, Id(2))).ShouldBeFalse();
        new DiskId(1, Id(1)).Equals(new DiskId(1, Id(2))).ShouldBeFalse();
    }

    [Fact]
    public void PartitionGuidRejectsEmptyGuid()
    {
        var exception = Should.Throw<ArgumentException>(() => new PartitionGuid(Guid.Empty));
        exception.Message.ShouldContain("No puede estar vacío");
    }

    [Fact]
    public void PartitionGuidAcceptsANonEmptyGuid()
    {
        new PartitionGuid(Id(5)).Value.ShouldBe(Id(5));
    }

    [Fact]
    public void GptPartitionTypeConstantsMatchTheGptTypeGuids()
    {
        GptPartitionType.EfiSystem.Value.ShouldBe(new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B"));
        GptPartitionType.MicrosoftReserved.Value.ShouldBe(new Guid("E3C9E316-0B5C-4DB8-817D-F92DF00215AE"));
        GptPartitionType.BasicData.Value.ShouldBe(new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7"));
        GptPartitionType.WindowsRecovery.Value.ShouldBe(new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC"));
        GptPartitionType.LdmMetadata.Value.ShouldBe(new Guid("5808C8AA-7E8F-42E0-85D2-E1E90434CFB3"));
        GptPartitionType.LdmData.Value.ShouldBe(new Guid("AF9B60A0-1431-4F62-BC68-3311714A69AD"));
        GptPartitionType.StorageSpaces.Value.ShouldBe(new Guid("E75CAF8F-F680-4CEE-AFA3-B001E56EFC2D"));
    }

    [Fact]
    public void GptPartitionTypeIsLdmIsTrueOnlyForLdmTypes()
    {
        GptPartitionType.LdmMetadata.IsLdm.ShouldBeTrue();
        GptPartitionType.LdmData.IsLdm.ShouldBeTrue();

        GptPartitionType.EfiSystem.IsLdm.ShouldBeFalse();
        GptPartitionType.BasicData.IsLdm.ShouldBeFalse();
        GptPartitionType.StorageSpaces.IsLdm.ShouldBeFalse();
    }

    [Fact]
    public void InventoryFingerprintOfNoPartsIsTheSha256OfTheEmptyString()
    {
        InventoryFingerprint.Compute([]).Value.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void InventoryFingerprintOfASinglePartIsTheSha256OfItsUtf8Bytes()
    {
        InventoryFingerprint.Compute(["abc"]).Value.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void InventoryFingerprintJoinsPartsWithANewlineAndHashesUtf8()
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("a\nñ")));

        InventoryFingerprint.Compute(["a", "ñ"]).Value.ShouldBe(expected);
    }

    [Fact]
    public void InventoryFingerprintValueIs64LowercaseHexCharacters()
    {
        InventoryFingerprint.Compute(["anything"]).Value.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void InventoryFingerprintIsEqualWhenThePartsAreEqual()
    {
        InventoryFingerprint.Compute(["a", "b"]).ShouldBe(InventoryFingerprint.Compute(["a", "b"]));
    }

    [Fact]
    public void InventoryFingerprintDiffersWhenAnyPartChanges()
    {
        InventoryFingerprint.Compute(["a", "b"]).ShouldNotBe(InventoryFingerprint.Compute(["a", "c"]));
    }

    [Fact]
    public void InventoryFingerprintDependsOnTheOrderOfTheParts()
    {
        InventoryFingerprint.Compute(["a", "b"]).ShouldNotBe(InventoryFingerprint.Compute(["b", "a"]));
    }

    [Fact]
    public void InventoryFingerprintRejectsBlankValues()
    {
        Should.Throw<ArgumentException>(() => new InventoryFingerprint(" "));
    }

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");
}
