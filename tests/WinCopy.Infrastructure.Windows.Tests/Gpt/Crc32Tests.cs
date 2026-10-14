using WinCopy.Infrastructure.Windows.Gpt;
using Shouldly;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Gpt;

public sealed class Crc32Tests
{
    [Fact]
    public void CheckStringMatchesIeeeCheckValue()
    {
        Crc32.Compute("123456789"u8).ShouldBe(0xCBF43926u);
    }

    [Fact]
    public void EmptyInputGivesZero()
    {
        Crc32.Compute(ReadOnlySpan<byte>.Empty).ShouldBe(0u);
    }
}
