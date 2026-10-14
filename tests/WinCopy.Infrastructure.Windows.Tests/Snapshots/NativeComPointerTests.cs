using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Shouldly;
using WinCopy.Infrastructure.Windows.Interop.Vss;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Snapshots;

// A managed IVssAsync exposed to native code. Its results are distinct so the test can tell which slot ran.
[GeneratedComClass]
internal sealed partial class DummyVssAsync : IVssAsync
{
    public uint LastWaitMilliseconds { get; private set; }

    public int Cancel() => 0x1;

    public int Wait(uint dwMilliseconds)
    {
        LastWaitMilliseconds = dwMilliseconds;
        return 0x2;
    }

    public int QueryStatus(out int pHrResult, nint pReserved)
    {
        pHrResult = VssConstants.VSS_S_ASYNC_FINISHED;
        return 0x3;
    }
}

// Checks the ownership model of NativeComPointer.ConvertAndFree with real native pointers and native reference counts.
// No VSS object is involved. Needs AllowUnsafeBlocks in the test project (the generated COM class requires it).
public sealed class NativeComPointerTests
{
    [Fact]
    public unsafe void ConvertAndFreeReleasesExactlyTheCallerReference()
    {
        var wrappers = new StrategyBasedComWrappers();

        // Control: the same conversion without Free, on a second object.
        nint control = wrappers.GetOrCreateComInterfaceForObject(new DummyVssAsync(), CreateComInterfaceFlags.None);
        int controlBefore = ReferenceCount(control);
        IVssAsync? controlProxy = ComInterfaceMarshaller<IVssAsync>.ConvertToManaged((void*)control);
        controlProxy.ShouldNotBeNull();
        int controlAfterConvertOnly = ReferenceCount(control) - controlBefore;

        // Under test: convert, then Free.
        nint pointer = wrappers.GetOrCreateComInterfaceForObject(new DummyVssAsync(), CreateComInterfaceFlags.None);
        int before = ReferenceCount(pointer);
        IVssAsync? proxy = NativeComPointer.ConvertAndFree<IVssAsync>(pointer);
        proxy.ShouldNotBeNull();
        int afterConvertAndFree = ReferenceCount(pointer) - before;

        // Free must release exactly the caller's reference that the convert-only path keeps.
        afterConvertAndFree.ShouldBe(controlAfterConvertOnly - 1);
    }

    [Fact]
    public void ProxyCallsReachTheManagedImplementation()
    {
        var wrappers = new StrategyBasedComWrappers();
        var dummy = new DummyVssAsync();
        nint pointer = wrappers.GetOrCreateComInterfaceForObject(dummy, CreateComInterfaceFlags.None);

        IVssAsync? proxy = NativeComPointer.ConvertAndFree<IVssAsync>(pointer);

        proxy.ShouldNotBeNull();
        proxy.Wait(123).ShouldBe(0x2);
        dummy.LastWaitMilliseconds.ShouldBe(123u);
        proxy.Cancel().ShouldBe(0x1);
        proxy.QueryStatus(out int operationResult, 0).ShouldBe(0x3);
        operationResult.ShouldBe(VssConstants.VSS_S_ASYNC_FINISHED);
    }

    [Fact]
    public void NullPointerReturnsNullWithoutFreeing() =>
        NativeComPointer.ConvertAndFree<IVssAsync>(0).ShouldBeNull();

    // AddRef returns the new count, so the count is read back with a Release.
    private static int ReferenceCount(nint pointer)
    {
        int withExtra = Marshal.AddRef(pointer);
        Marshal.Release(pointer);
        return withExtra - 1;
    }
}
