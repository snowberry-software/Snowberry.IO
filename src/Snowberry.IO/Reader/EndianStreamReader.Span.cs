namespace Snowberry.IO.Reader;

public partial class EndianStreamReader
{
    /// <inheritdoc/>
    protected override int InternalReadBytes(Span<byte> inBuffer)
    {
        ThrowIfDisposed();

        _ = Stream ?? throw new NullReferenceException(nameof(Stream));

        // NOTE(VNC): Important because of region views.
        if (!CanReadData)
            return 0;

#if NETSTANDARD2_0
        byte[] tempBuffer = new byte[inBuffer.Length];
        int read = Stream.Read(tempBuffer, 0, tempBuffer.Length);
        tempBuffer.CopyTo(inBuffer);
        return read;
#else
        return Stream.Read(inBuffer);
#endif
    }

    /// <inheritdoc/>
    protected override void InternalReadBytesExactly(Span<byte> inBuffer)
    {
        ThrowIfDisposed();

        _ = Stream ?? throw new NullReferenceException(nameof(Stream));

        // NOTE(VNC): Important because of region views.
        if (!CanReadData)
            ThrowEndOfStreamException();

#if NET7_0_OR_GREATER
        Stream.ReadExactly(inBuffer);
#else
        int read = InternalReadBytes(inBuffer);

        if (read != inBuffer.Length)
            ThrowEndOfStreamException();
#endif
    }
}
