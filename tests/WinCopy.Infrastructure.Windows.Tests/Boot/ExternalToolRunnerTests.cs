using System.ComponentModel;
using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports.Models;
using WinCopy.Infrastructure.Windows.Boot;
using WinCopy.Infrastructure.Windows.Tests.Fakes;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Boot;

// The process is replaced by a fake launcher. No test starts a process.
public sealed class ExternalToolRunnerTests
{
    private const string SystemDirectory = @"C:\FakeWindows\System32";
    private static readonly string[] HelpArguments = ["/?"];

    [Fact]
    public async Task RunsBcdbootByAbsolutePathInSystemDirectory()
    {
        var launcher = new FakeToolLauncher();
        var runner = new ExternalToolRunner(launcher, new RecordingExecutionLog(), SystemDirectory);

        await runner.RunAsync("bcdboot.exe", HelpArguments, CancellationToken.None);

        launcher.Calls.Single().Path.ShouldBe(Path.Combine(SystemDirectory, "bcdboot.exe"));
    }

    [Fact]
    public async Task RunsReagentcByAbsolutePathInSystemDirectory()
    {
        var launcher = new FakeToolLauncher();
        var runner = new ExternalToolRunner(launcher, new RecordingExecutionLog(), SystemDirectory);

        await runner.RunAsync("reagentc.exe", ["/info"], CancellationToken.None);

        launcher.Calls.Single().Path.ShouldBe(Path.Combine(SystemDirectory, "reagentc.exe"));
    }

    [Fact]
    public async Task PassesArgumentsAsAnArgumentListWithoutJoiningThem()
    {
        var launcher = new FakeToolLauncher();
        var runner = new ExternalToolRunner(launcher, new RecordingExecutionLog(), SystemDirectory);
        string[] arguments = [@"C:\Windows", "/s", @"S:\", "/f", "UEFI", @"C:\Path With Spaces\x", "\"quoted\" & more"];

        await runner.RunAsync("bcdboot.exe", arguments, CancellationToken.None);

        launcher.Calls.Single().Arguments.ShouldBe(arguments);
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("bcdboot")]
    [InlineData("BCDBOOT.EXE")]
    [InlineData("diskpart.exe")]
    public async Task RejectsToolsOutsideTheAllowlist(string toolFileName)
    {
        var launcher = new FakeToolLauncher();
        var runner = new ExternalToolRunner(launcher, new RecordingExecutionLog(), SystemDirectory);

        await Should.ThrowAsync<ArgumentException>(() => runner.RunAsync(toolFileName, [], CancellationToken.None));

        launcher.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(@"..\bcdboot.exe")]
    [InlineData(@"sub\bcdboot.exe")]
    [InlineData("sub/bcdboot.exe")]
    [InlineData(@"C:\Evil\bcdboot.exe")]
    [InlineData("bcdboot.exe:stream")]
    [InlineData("..bcdboot.exe")]
    [InlineData(@"bcdboot.exe\..")]
    public async Task RejectsNamesWithPathSeparatorsColonsOrDotDot(string toolFileName)
    {
        var launcher = new FakeToolLauncher();
        var runner = new ExternalToolRunner(launcher, new RecordingExecutionLog(), SystemDirectory);

        await Should.ThrowAsync<ArgumentException>(() => runner.RunAsync(toolFileName, [], CancellationToken.None));

        launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RejectsAnEmptyName()
    {
        var runner = new ExternalToolRunner(new FakeToolLauncher(), new RecordingExecutionLog(), SystemDirectory);

        await Should.ThrowAsync<ArgumentException>(() => runner.RunAsync(string.Empty, [], CancellationToken.None));
    }

    [Fact]
    public async Task LogsAnExternalToolRunEventWithTheOutcome()
    {
        var launcher = new FakeToolLauncher { Result = new ToolResult(0, "boot files created", "warn text") };
        var log = new RecordingExecutionLog();
        var runner = new ExternalToolRunner(launcher, log, SystemDirectory);

        ToolResult result = await runner.RunAsync("bcdboot.exe", [@"C:\Windows", "/s", "S:"], CancellationToken.None);

        result.ExitCode.ShouldBe(0);
        result.StdOut.ShouldBe("boot files created");
        result.StdErr.ShouldBe("warn text");

        LogEvent e = log.Events.Single();
        e.Event.ShouldBe("ExternalToolRun");
        e.Level.ShouldBe(LogLevelKind.Information);
        e.Properties["Tool"].ShouldBe("bcdboot");
        string[] expectedArguments = [@"C:\Windows", "/s", "S:"];
        ((string[])e.Properties["Arguments"]!).ShouldBe(expectedArguments);
        e.Properties["ExitCode"].ShouldBe(0);
        e.Properties["StdOut"].ShouldBe("boot files created");
        e.Properties["StdErr"].ShouldBe("warn text");
    }

    [Fact]
    public async Task NonZeroExitCodeIsReturnedAndLoggedAsWarning()
    {
        var launcher = new FakeToolLauncher { Result = new ToolResult(1, string.Empty, "access is denied") };
        var log = new RecordingExecutionLog();
        var runner = new ExternalToolRunner(launcher, log, SystemDirectory);

        ToolResult result = await runner.RunAsync("reagentc.exe", ["/setreimage"], CancellationToken.None);

        result.ExitCode.ShouldBe(1);
        LogEvent e = log.Events.Single();
        e.Level.ShouldBe(LogLevelKind.Warning);
        e.Properties["Tool"].ShouldBe("reagentc");
        e.Properties["ExitCode"].ShouldBe(1);
    }

    [Fact]
    public async Task StartFailureOfBcdbootIsBootConfigFailedAndLoggedAsError()
    {
        var launcher = new FakeToolLauncher { Failure = new Win32Exception(2) };
        var log = new RecordingExecutionLog();
        var runner = new ExternalToolRunner(launcher, log, SystemDirectory);

        var ex = await Should.ThrowAsync<PlatformException>(
            () => runner.RunAsync("bcdboot.exe", HelpArguments, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        LogEvent e = log.Events.Single();
        e.Level.ShouldBe(LogLevelKind.Error);
        e.Properties["ExitCode"].ShouldBeNull();
    }

    [Fact]
    public async Task StartFailureOfReagentcIsRecoveryConfigFailed()
    {
        var launcher = new FakeToolLauncher { Failure = new Win32Exception(2) };
        var runner = new ExternalToolRunner(launcher, new RecordingExecutionLog(), SystemDirectory);

        var ex = await Should.ThrowAsync<PlatformException>(
            () => runner.RunAsync("reagentc.exe", ["/info"], CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RecoveryConfigFailed);
    }

    [Fact]
    public async Task CancellationIsLoggedAndRethrown()
    {
        var launcher = new FakeToolLauncher { Failure = new OperationCanceledException() };
        var log = new RecordingExecutionLog();
        var runner = new ExternalToolRunner(launcher, log, SystemDirectory);

        await Should.ThrowAsync<OperationCanceledException>(
            () => runner.RunAsync("bcdboot.exe", HelpArguments, CancellationToken.None));

        log.Events.Single().Level.ShouldBe(LogLevelKind.Error);
    }

    [Fact]
    public async Task CancelledTokenDoesNotStartTheTool()
    {
        var launcher = new FakeToolLauncher();
        var runner = new ExternalToolRunner(launcher, new RecordingExecutionLog(), SystemDirectory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => runner.RunAsync("bcdboot.exe", HelpArguments, cts.Token));

        launcher.Calls.ShouldBeEmpty();
    }

    private sealed class FakeToolLauncher : IToolProcessLauncher
    {
        public List<(string Path, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public ToolResult Result { get; init; } = new(0, string.Empty, string.Empty);

        public Exception? Failure { get; init; }

        public Task<ToolResult> RunAsync(string absolutePath, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Calls.Add((absolutePath, arguments));

            return Failure is null
                ? Task.FromResult(Result)
                : Task.FromException<ToolResult>(Failure);
        }
    }
}
