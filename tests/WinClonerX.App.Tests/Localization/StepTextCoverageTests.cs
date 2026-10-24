using Shouldly;
using WinClonerX.App.Localization;
using WinClonerX.App.Tests.Fakes;
using WinClonerX.Application.Errors;
using WinClonerX.Domain.Execution;
using Xunit;

namespace WinClonerX.App.Tests.Localization;

/// <summary>
/// Every enum value that the wizard turns into text has a resource. Spanish parity is checked by <see cref="ResourceParityTests"/>,
/// so checking English here covers both languages.
/// </summary>
public sealed class StepTextCoverageTests
{
    [Fact]
    public void EveryExecutionPhaseHasAName()
    {
        using var _ = new EnglishCulture();

        foreach (var phase in Enum.GetValues<ExecutionPhase>())
        {
            Loc.Get("Phase_" + phase).StartsWith("[[", StringComparison.Ordinal).ShouldBeFalse($"Missing Phase_{phase}");
        }
    }

    [Fact]
    public void EveryNextStepCodeHasAnInstruction()
    {
        using var _ = new EnglishCulture();

        foreach (var code in Enum.GetValues<NextStepCode>())
        {
            Loc.Get("NextStep_" + code).StartsWith("[[", StringComparison.Ordinal).ShouldBeFalse($"Missing NextStep_{code}");
        }
    }

    [Fact]
    public void EveryPlatformErrorCodeHasAMessage()
    {
        using var _ = new EnglishCulture();

        foreach (var code in Enum.GetValues<PlatformErrorCode>())
        {
            Loc.Get("Error_" + code).StartsWith("[[", StringComparison.Ordinal).ShouldBeFalse($"Missing Error_{code}");
        }
    }
}
