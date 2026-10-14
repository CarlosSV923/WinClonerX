using System.Runtime.InteropServices;

namespace WinCopy.Infrastructure.Windows.Interop.Vss;

/// <summary>COM apartment functions used by the VSS worker thread.</summary>
internal static partial class Ole32
{
    /// <summary>COINIT_MULTITHREADED: join the multithreaded apartment (MTA).</summary>
    internal const uint COINIT_MULTITHREADED = 0x0;

    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();
}
