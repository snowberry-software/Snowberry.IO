using System.Runtime.CompilerServices;

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <summary>
    /// Reads a specified number of bytes from the current stream into a buffer.
    /// </summary>
    /// <param name="inBuffer">The span of bytes to store the read bytes.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero if the end of the stream is reached.
    /// </returns>
    /// <remarks>
    /// This method is intended to be implemented by derived classes to provide the actual logic for reading bytes.
    /// </remarks>
    protected abstract int InternalReadBytes(Span<byte> inBuffer);

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public int Read(Span<byte> buffer)
    {
        int read = InternalReadBytes(buffer);

        if (IsRegionViewEnabled)
            _viewOffset += read;

        Analyzer?.AnalyzeReadBytes(this, buffer, read, 0);
        return read;
    }
}
