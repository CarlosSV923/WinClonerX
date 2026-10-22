using Xunit;

namespace WinClonerX.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// The US1 integration classes that attach disks, run clones and read the whole inventory share this collection, and the
/// collection runs one test at a time. Clones use VSS and the Storage cmdlets, which should not overlap with each other.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class IntegrationDisks
{
    public const string Name = "Integration disks (US1)";
}
