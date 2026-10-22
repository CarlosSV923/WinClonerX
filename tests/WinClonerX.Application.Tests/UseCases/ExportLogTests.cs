using Shouldly;
using WinClonerX.Application.Tests.Fakes;
using WinClonerX.Application.UseCases;
using Xunit;

namespace WinClonerX.Application.Tests.UseCases;

/// <summary>Exportación del registro (T097): la ruta y el indicador de inventario llegan intactos, y los errores salen al llamador.</summary>
public sealed class ExportLogTests
{
    private const string Destination = @"C:\Users\Test\Documents\WinClonerX-registro-20261020-101530.zip";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PassesThePathAndTheInventoryFlagToTheLog(bool includeInventory)
    {
        var log = new FakeExecutionLog();
        var useCase = new ExportLog(() => log);

        await useCase.ExecuteAsync(Destination, includeInventory, Ct);

        log.Exports.ShouldBe([(Destination, includeInventory)]);
    }

    [Fact]
    public async Task WriteFailureReachesTheCaller()
    {
        var log = new FakeExecutionLog();
        log.FailOn[nameof(FakeExecutionLog.ExportAsync)] = new IOException("The disk is full.");
        var useCase = new ExportLog(() => log);

        var error = await Should.ThrowAsync<IOException>(() => useCase.ExecuteAsync(Destination, true, Ct));

        error.Message.ShouldBe("The disk is full.");
        log.Exports.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancellationReachesTheLog()
    {
        var log = new FakeExecutionLog();
        var useCase = new ExportLog(() => log);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(Destination, false, cancellation.Token));

        log.Exports.ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolvesTheLogOnlyWhenAnExportIsRequested()
    {
        var log = new FakeExecutionLog();
        var resolutions = 0;
        var useCase = new ExportLog(() =>
        {
            resolutions++;
            return log;
        });

        resolutions.ShouldBe(0);

        await useCase.ExecuteAsync(Destination, false, Ct);

        resolutions.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankPathIsRejectedBeforeTheLogIsTouched(string destination)
    {
        var log = new FakeExecutionLog();
        var useCase = new ExportLog(() => log);

        await Should.ThrowAsync<ArgumentException>(() => useCase.ExecuteAsync(destination, false, Ct));

        log.Calls.ShouldBeEmpty();
    }
}
