using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WinCopy.Infrastructure.Windows.Interop.Vss;

/// <summary>
/// IVssAsync from vss.h, IID 507C37B4-CF5B-4e95-B0AF-14EB9767467E. Slot order after IUnknown:
/// Cancel, Wait, QueryStatus. Do not reorder.
/// </summary>
[GeneratedComInterface]
[Guid("507C37B4-CF5B-4e95-B0AF-14EB9767467E")]
internal partial interface IVssAsync
{
    // Slot 1.
    [PreserveSig]
    int Cancel();

    // Slot 2. Blocks for up to dwMilliseconds. VssConstants.INFINITE waits forever.
    [PreserveSig]
    int Wait(uint dwMilliseconds);

    // Slot 3. The HRESULT of the call goes into the return value; pHrResult receives the operation's own status.
    [PreserveSig]
    int QueryStatus(out int pHrResult, nint pReserved);
}
