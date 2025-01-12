using System.Runtime.CompilerServices;

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    protected abstract int InternalReadBytes(Span<byte> inBuffer);

    protected abstract void InternalReadBytesExactly(Span<byte> inBuffer);

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public int Read(Span<byte> buffer)
    {
        int read = InternalReadBytes(buffer);

        if (IsRegionViewEnabled)
            _viewOffset += read;

        Analyzer?.AnalyzeReadBytes(this, buffer, read);
        return read;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public void ReadExactly(Span<byte> buffer)
    {
        InternalReadBytesExactly(buffer);

        if (IsRegionViewEnabled)
            _viewOffset += buffer.Length;

        Analyzer?.AnalyzeReadBytes(this, buffer, buffer.Length);
    }
}
