using System.Runtime.InteropServices.Marshalling;

namespace WinClonerX.Infrastructure.Windows.Interop.Vss;

/// <summary>
/// Turns an interface pointer that a native call returned through an <c>out nint</c> parameter into a managed proxy.
/// </summary>
internal static unsafe class NativeComPointer
{
    /// <summary>
    /// Wraps <paramref name="pointer"/> in a managed proxy of <typeparamref name="T"/>, then releases the caller's
    /// reference. The caller owns one reference to the pointer, as with any <c>out</c> interface parameter.
    /// <c>ConvertToManaged</c> adds references for the proxy and leaves the caller's reference in place. <c>Free</c>
    /// releases exactly that caller reference, so the proxy's own references keep the native object alive.
    /// Measured with native reference counts: without Free the caller's reference leaks. Returns null for a null
    /// pointer. When the pointer is not null, Free is always called, even if the conversion fails.
    /// </summary>
    internal static T? ConvertAndFree<T>(nint pointer) where T : class
    {
        if (pointer == 0)
        {
            return null;
        }

        try
        {
            return ComInterfaceMarshaller<T>.ConvertToManaged((void*)pointer);
        }
        finally
        {
            ComInterfaceMarshaller<T>.Free((void*)pointer);
        }
    }
}
