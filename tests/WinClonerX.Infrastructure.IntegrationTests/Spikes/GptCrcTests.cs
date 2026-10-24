using Shouldly;
using Xunit;

namespace WinClonerX.Infrastructure.IntegrationTests.Spikes;

// Pure unit test for spike S1. It touches no disk and needs no elevation. It is not an integration test.
[Trait("Category", "Integration")]
[Trait("Spike", "S1")]
public sealed class GptCrcTests
{
    [Fact]
    public void Crc32OfCheckStringMatchesIeeeCheckValue()
    {
        GptLayout.Crc32("123456789"u8).ShouldBe(0xCBF43926u);
    }
}
