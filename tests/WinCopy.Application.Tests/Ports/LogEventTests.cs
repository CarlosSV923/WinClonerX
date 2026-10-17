using Shouldly;
using WinCopy.Application.Ports.Models;
using Xunit;

namespace WinCopy.Application.Tests.Ports;

public sealed class LogEventTests
{
    private static readonly string[] EnvelopeNames = ["Event", "ExecutionId", "AppVersion"];

    [Theory]
    [InlineData("Event")]
    [InlineData("ExecutionId")]
    [InlineData("AppVersion")]
    public void ReservedEnvelopeNameAsPropertyIsRejected(string name)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal) { [name] = "value" };

        var thrown = Should.Throw<ArgumentException>(() => new LogEvent("PhaseStarted", properties));

        thrown.ParamName.ShouldBe("properties");
        thrown.Message.ShouldContain(name);
    }

    [Fact]
    public void ReservedNamesAreTheEnvelopeNames()
    {
        LogEvent.ReservedPropertyNames.ShouldBe(EnvelopeNames);
    }

    [Fact]
    public void NonReservedPropertyNamesAreAccepted()
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal) { ["Phase"] = "CopyData", ["executionid"] = "x" };

        var e = new LogEvent("PhaseStarted", properties);

        e.Properties.Count.ShouldBe(2);
    }

    [Fact]
    public void BlankEventNameIsRejected()
    {
        Should.Throw<ArgumentException>(() => new LogEvent(" ", new Dictionary<string, object?>()));
    }
}
