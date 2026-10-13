using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.IntegrationTests.Fixtures;

internal static class IntegrationTestsModule
{
    /// <summary>
    /// Runs when the test assembly loads, before any test or fixture. It adds the physical-disk protection to
    /// <see cref="DiskWriteGuard.Default"/>, so the protection covers every test in the process, including tests
    /// that never use <see cref="VirtualDiskFixture"/>.
    /// </summary>
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "This is a test executable, not a library. The initializer must run before any test so the restriction is always active.")]
    internal static void AddVirtualDisksOnlyRestriction() =>
        DiskWriteGuard.Default.AddRestriction(new VirtualDisksOnlyRestriction());
}
