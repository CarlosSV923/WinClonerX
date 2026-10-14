using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.App.Tests.Fakes;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;

namespace WinCopy.App.Tests.Presentation;

public sealed class PreflightTextTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public void EveryPreflightCodeHasAMessageInEachLanguage(string culture)
    {
        using var scope = new CultureScope(culture);

        foreach (var code in Enum.GetValues<PreflightCode>())
        {
            var message = PreflightText.Message(CheckFor(code));

            message.ShouldNotBeNullOrWhiteSpace();
            message.Contains("[[").ShouldBeFalse(code.ToString());
            message.Contains('{').ShouldBeFalse(code.ToString());
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("es-ES")]
    public void EverySeverityHasALabelInEachLanguage(string culture)
    {
        using var scope = new CultureScope(culture);

        foreach (var severity in Enum.GetValues<Severity>())
        {
            PreflightText.SeverityLabel(severity).Contains("[[").ShouldBeFalse(severity.ToString());
        }
    }

    [Fact]
    public void ShortfallIsFormattedIntoTheTooSmallMessage()
    {
        using var scope = new CultureScope("en-US");
        var check = new PreflightCheck(
            PreflightCode.TargetTooSmall,
            Severity.Blocking,
            new Dictionary<string, object> { ["Shortfall"] = ByteSize.FromGiB(23) });

        PreflightText.Message(check).ShouldContain("23 GB");
    }

    [Fact]
    public void ReasonPrefixesTheSeverityLabel()
    {
        using var scope = new CultureScope("en-US");
        var check = new PreflightCheck(PreflightCode.TargetInUse, Severity.Blocking, PreflightCheck.EmptyFacts());

        PreflightText.Reason(check).ShouldStartWith(Loc.Get("Severity_Blocking") + ": ");
    }

    private static PreflightCheck CheckFor(PreflightCode code) => new(
        code,
        Severity.Blocking,
        new Dictionary<string, object>
        {
            ["Shortfall"] = ByteSize.FromGiB(1),
            ["FreeSpace"] = ByteSize.FromGiB(1),
        });
}
