using Shouldly;
using WinCopy.App.Presentation;
using WinCopy.App.Tests.Fakes;
using WinCopy.Domain.Shared;
using Xunit;

namespace WinCopy.App.Tests.Presentation;

public sealed class SizeTextTests
{
    [Fact]
    public void BinarySizesAreLabelledWithTheBinaryUnits()
    {
        using var _ = new EnglishCulture();

        SizeText.Format(ByteSize.FromGiB(931.5)).ShouldBe("931.5 GiB");
        SizeText.Format(ByteSize.FromMiB(750)).ShouldBe("750 MiB");
        SizeText.Format(new ByteSize(2048)).ShouldBe("2 KiB");
        SizeText.Format(ByteSize.FromGiB(2048)).ShouldBe("2 TiB");
        SizeText.Format(new ByteSize(512)).ShouldBe("512 B");
    }

    [Fact]
    public void NoDecimalUnitLabelIsEverShown()
    {
        using var _ = new EnglishCulture();

        var samples = new[] { ByteSize.FromGiB(1), ByteSize.FromMiB(5), new ByteSize(3000), ByteSize.FromGiB(4096) };

        foreach (var size in samples)
        {
            var text = SizeText.Format(size);
            text.ShouldNotContain(" GB");
            text.ShouldNotContain(" MB");
            text.ShouldNotContain(" KB");
            text.ShouldNotContain(" TB");
        }
    }

    [Fact]
    public void SpanishUsesAComma()
    {
        using var _ = new CultureScope("es-ES");

        SizeText.Format(ByteSize.FromGiB(931.5)).ShouldBe("931,5 GiB");
        SizeText.Format(ByteSize.FromGiB(1536)).ShouldBe("1,5 TiB");
    }

    [Fact]
    public void EnglishUsesADot()
    {
        using var _ = new CultureScope("en-US");

        SizeText.Format(ByteSize.FromGiB(931.5)).ShouldBe("931.5 GiB");
        SizeText.Format(ByteSize.FromGiB(1536)).ShouldBe("1.5 TiB");
    }
}
