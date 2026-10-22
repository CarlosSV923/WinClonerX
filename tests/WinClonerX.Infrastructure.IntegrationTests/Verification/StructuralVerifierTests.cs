using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.IntegrationTests.Cloning;
using WinClonerX.Infrastructure.IntegrationTests.Fixtures;
using WinClonerX.Infrastructure.IntegrationTests.Spikes;
using Xunit;

namespace WinClonerX.Infrastructure.IntegrationTests.Verification;

/// <summary>
/// Task T045. The real <see cref="IStructuralVerifier"/> on clones of a reference system VHDX. One clean clone is made once for
/// the class. Two corrupted clones are made for the failure cases. Needs an elevated process:
/// <c>dotnet test tests/WinClonerX.Infrastructure.IntegrationTests --filter "FullyQualifiedName~StructuralVerifierTests"</c>
/// </summary>
[Trait("Category", "Integration")]
[Collection(IntegrationDisks.Name)]
public sealed class StructuralVerifierTests(CleanCloneFixture clean) : IClassFixture<CleanCloneFixture>
{
    private const long Mib = 1L << 20;

    [Fact]
    public async Task CleanCloneIsPassedByTheRealVerifier()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        CloneScenario scenario = clean.Scenario;

        scenario.Report.Outcome.ShouldBe(CloneOutcome.Completed);

        IStructuralVerifier verifier = scenario.Harness.Services.GetRequiredService<IStructuralVerifier>();
        VerificationResult result = await verifier.VerifyAsync(scenario.Plan, ct);

        result.Passed.ShouldBeTrue(string.Join("; ", result.Issues));
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShiftedGptEntryFailsVerificationNamingThePartition()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        // The MSR entry is moved 1 MiB later and made 1 MiB shorter, so it stays inside its gap. The real adapter commits the
        // altered layout with its CRCs recomputed. The verifier runs inside the orchestrator, after the commit.
        await using CloneScenario scenario = await CloneScenario.RunAsync(ShiftMsrEntryByOneMiB, ct);

        // The failed verification aborts the run and compensates. The target table is then deleted, so the verdict comes from the
        // report, which keeps the verifier result of the run.
        scenario.Report.Outcome.ShouldBe(CloneOutcome.Failed);
        VerificationResult structural = scenario.Report.Structural.ShouldNotBeNull("the verifier must have run");
        structural.Passed.ShouldBeFalse("a shifted GPT entry must not pass");

        int msrNumber = scenario.Plan.Mappings
            .Single(mapping => mapping.Source.Type == GptPartitionType.MicrosoftReserved)
            .Source.Number;

        structural.Issues.Any(issue => issue.Contains($"source partition {msrNumber} ", StringComparison.Ordinal))
            .ShouldBeTrue($"an issue must name source partition {msrNumber}; got: {string.Join(" | ", structural.Issues)}");
    }

    [Fact]
    public async Task MissingBootStoreFailsVerificationWithABcdIssue()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        await using CloneScenario scenario = await CloneScenario.RunAsync(tamperCommittedLayout: null, ct);
        scenario.Report.Outcome.ShouldBe(CloneOutcome.Completed);

        Disk targetDisk = await scenario.ReadTargetDiskAsync(ct);
        int espNumber = targetDisk.Partitions
            .Single(partition => partition.Role == PartitionRole.EfiSystem)
            .Number;

        // Remove the BCD from the clone's ESP through the storage adapter, which checks that the letter is on the target disk.
        using (TemporaryDriveLetter letter = TemporaryDriveLetter.Acquire(VirtualDiskStorageOps.Default, scenario.Target.DiskNumber, espNumber))
        {
            string bcd = Path.Combine(letter.Root, "EFI", "Microsoft", "Boot", "BCD");
            File.Exists(bcd).ShouldBeTrue("the clone's ESP must hold a BCD before the test removes it");
            File.Delete(bcd);
        }

        IStructuralVerifier verifier = scenario.Harness.Services.GetRequiredService<IStructuralVerifier>();
        VerificationResult result = await verifier.VerifyAsync(scenario.Plan, ct);

        result.Passed.ShouldBeFalse("a clone without BCD must not pass");
        result.Issues.Any(issue => issue.Contains("BCD", StringComparison.Ordinal) && issue.Contains("missing", StringComparison.Ordinal))
            .ShouldBeTrue($"an issue must name the missing BCD; got: {string.Join(" | ", result.Issues)}");
    }

    private static TargetLayout ShiftMsrEntryByOneMiB(TargetLayout layout)
    {
        TargetLayoutEntry[] entries = layout.Entries
            .Select(entry => entry.Type == GptPartitionType.MicrosoftReserved
                ? entry with { Extent = new Extent(entry.Extent.Offset + Mib, entry.Extent.Length - Mib) }
                : entry)
            .ToArray();

        return layout with { Entries = entries };
    }
}

/// <summary>Shares one clean clone among the tests of <see cref="StructuralVerifierTests"/>.</summary>
public sealed class CleanCloneFixture : IAsyncLifetime
{
    internal CloneScenario Scenario { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Scenario = await CloneScenario.RunAsync(tamperCommittedLayout: null, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (Scenario is not null)
        {
            await Scenario.DisposeAsync();
        }
    }
}

/// <summary>
/// A reference disk, a 3 GiB target and one clone run through <see cref="CloneHarness"/>. Owns the fixture and the harness, and
/// disposes them in that order.
/// </summary>
internal sealed class CloneScenario : IAsyncDisposable
{
    private static readonly ByteSize SourceSize = ByteSize.FromGiB(2);
    private static readonly ByteSize TargetSize = ByteSize.FromGiB(3);

    private readonly VirtualDiskFixture _disks;

    private CloneScenario(
        VirtualDiskFixture disks,
        CloneHarness harness,
        ReferenceDisk source,
        AttachedVirtualDisk target,
        ClonePlan plan,
        CloneResultReport report)
    {
        _disks = disks;
        Harness = harness;
        Source = source;
        Target = target;
        Plan = plan;
        Report = report;
    }

    public CloneHarness Harness { get; }

    public ReferenceDisk Source { get; }

    public AttachedVirtualDisk Target { get; }

    public ClonePlan Plan { get; }

    public CloneResultReport Report { get; }

    /// <summary>Builds the reference, attaches the target and runs one clone. A failed run still returns its report.</summary>
    public static async Task<CloneScenario> RunAsync(Func<TargetLayout, TargetLayout>? tamperCommittedLayout, CancellationToken ct)
    {
        var disks = new VirtualDiskFixture();
        try
        {
            ReferenceDisk source = await ReferenceSystemDisk.CreateAsync(
                disks, VirtualDiskStorageOps.Default, SourceSize, dataFiles: 4, includeDummyBcd: true);
            AttachedVirtualDisk target = await disks.CreateAndAttachAsync(TargetSize);

            var harness = new CloneHarness(source, Path.Combine(disks.RootFolder, "logs"), tamperCommittedLayout);
            try
            {
                (ClonePlan plan, CloneResultReport report) = await harness.CloneAsync(source, target, ct);
                return new CloneScenario(disks, harness, source, target, plan, report);
            }
            catch
            {
                await harness.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await disks.DisposeAsync();
            throw;
        }
    }

    /// <summary>The target disk as the real reader reports it now, without the fixture decorator.</summary>
    public async Task<Disk> ReadTargetDiskAsync(CancellationToken ct)
    {
        DiskInventory inventory = await Harness.RealReader.ReadAsync(ct);
        return inventory.Disks
            .SingleOrDefault(disk => disk.Id.Number == Target.DiskNumber)
            .ShouldNotBeNull($"the real inventory must list target disk {Target.DiskNumber}");
    }

    public async ValueTask DisposeAsync()
    {
        await Harness.DisposeAsync();
        await _disks.DisposeAsync();
    }
}
