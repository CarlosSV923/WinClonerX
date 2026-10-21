using System.Text;
using Shouldly;
using WinCopy.Infrastructure.Windows.Boot;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Boot;

// The launcher starts real processes, so only its output decoding is tested here. No test starts a process.
public sealed class ProcessToolLauncherTests
{
    // Code page 850 (Western European OEM): in it, 'ó' is 0xA2. The default ANSI code page reads it as '¢'.
    private const int WesternEuropeanOemCodePage = 850;

    [Fact]
    public void ToolOutputIsDecodedWithTheConsoleCodePage()
    {
        Encoding encoding = ProcessToolLauncher.ResolveOutputEncoding(WesternEuropeanOemCodePage);

        encoding.CodePage.ShouldBe(WesternEuropeanOemCodePage);
        byte[] bytes = [0x6F, 0x70, 0x65, 0x72, 0x61, 0x63, 0x69, 0xA2, 0x6E];

        encoding.GetString(bytes).ShouldBe("operación");
    }

    [Fact]
    public void AnUnavailableCodePageFallsBackToTheDefaultEncoding()
    {
        Encoding encoding = ProcessToolLauncher.ResolveOutputEncoding(99999);

        encoding.CodePage.ShouldBe(Encoding.Default.CodePage);
    }
}
