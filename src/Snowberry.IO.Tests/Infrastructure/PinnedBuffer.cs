using System.Runtime.InteropServices;

namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// Pins a byte array so that its address in the current process stays valid until the buffer is disposed.
/// </summary>
/// <param name="data">The array to pin.</param>
internal sealed class PinnedBuffer(byte[] data) : IDisposable
{
    private GCHandle _handle = GCHandle.Alloc(data, GCHandleType.Pinned);

    public void Dispose()
    {
        if (_handle.IsAllocated)
            _handle.Free();
    }

    /// <summary>
    /// Gets the address of the first byte.
    /// </summary>
    public long Address => _handle.AddrOfPinnedObject().ToInt64();

    /// <summary>
    /// Gets the address of the first byte as a pointer.
    /// </summary>
    public IntPtr Pointer => _handle.AddrOfPinnedObject();
}