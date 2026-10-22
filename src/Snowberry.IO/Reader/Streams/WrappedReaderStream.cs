using System.Runtime.CompilerServices;

namespace Snowberry.IO.Reader.Streams;

/// <summary>
/// The <see cref="ReaderStream"/> of an <see cref="EndianStreamReader"/>, which reads from the user's stream.
/// </summary>
internal sealed class WrappedReaderStream : ReaderStream
{
#if NETSTANDARD2_0
    /// <summary>
    /// The maximum size of <see cref="_compatReadBuffer"/>; larger span reads are split into several stream reads.
    /// </summary>
    private const int c_MaxCompatReadBufferSize = 81920;

    /// <summary>
    /// Reusable scratch buffer for span reads on target frameworks that lack <c>Stream.Read(Span&lt;byte&gt;)</c>.
    /// </summary>
    private byte[]? _compatReadBuffer;
#endif

    private readonly Stream? _inner;

    /// <summary>
    /// Creates a stream over <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The user's stream, or <see langword="null"/>.</param>
    public WrappedReaderStream(Stream? inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// Gets the user's stream, or <see langword="null"/> when the reader was created without one.
    /// </summary>
    internal Stream? Inner => _inner;

    /// <inheritdoc/>
    public override bool CanSeek => _inner?.CanSeek ?? false;

    /// <inheritdoc/>
    internal override long ActualLength => Source.Length;

    /// <inheritdoc/>
    protected override long SourcePosition
    {
        get => Source.Position;
        set => Source.Position = value;
    }

    /// <inheritdoc/>
    protected override long UnviewedLength => _inner?.Length ?? 0;

    /// <inheritdoc/>
    internal override Stream? DirectSource => _analyzer is null && !_isRegionViewEnabled ? _inner : null;

    private Stream Source => _inner ?? ThrowNoStream();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        int toRead = ClampToRegion(count);
        int read = toRead == 0 ? 0 : Source.Read(buffer, offset, toRead);

        OnRead(buffer, offset, count, read);
        return read;
    }

    /// <inheritdoc/>
    internal override int ReadUnreported(byte[] buffer, int offset, int count)
    {
        int toRead = ClampToRegion(count);
        int read = toRead == 0 ? 0 : Source.Read(buffer, offset, toRead);

        if (_isRegionViewEnabled)
            _viewOffset += read;

        return read;
    }

#if !NETSTANDARD2_0
    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        int toRead = ClampToRegion(buffer.Length);
        int read = toRead == 0 ? 0 : Source.Read(buffer[..toRead]);

        OnRead(buffer, read);
        return read;
    }
#endif

    /// <inheritdoc/>
    public override int ReadByte()
    {
        if (ClampToRegion(1) == 0)
            return OnReadByte(-1);

        return OnReadByte(Source.ReadByte());
    }

    /// <inheritdoc/>
    internal override int ReadUnreported(Span<byte> buffer)
    {
        int toRead = ClampToRegion(buffer.Length);
        int read = toRead == 0 ? 0 : ReadSource(buffer[..toRead]);

        if (_isRegionViewEnabled)
            _viewOffset += read;

        return read;
    }

    /// <summary>
    /// Reads from the user's stream into <paramref name="buffer"/> with a single stream read.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <returns>The number of bytes read.</returns>
    private int ReadSource(Span<byte> buffer)
    {
#if NETSTANDARD2_0
        var stream = Source;
        int bufferSize = Math.Min(buffer.Length, c_MaxCompatReadBufferSize);

        if (_compatReadBuffer == null || _compatReadBuffer.Length < bufferSize)
            _compatReadBuffer = new byte[bufferSize];

        // Large requests are split into buffer-sized reads; stop at the first short read, like a single
        // Stream.Read call would, so a stream that has no more data available right now is not waited on.
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int toRead = Math.Min(buffer.Length - totalRead, _compatReadBuffer.Length);
            int read = stream.Read(_compatReadBuffer, 0, toRead);

            _compatReadBuffer.AsSpan(0, read).CopyTo(buffer[totalRead..]);
            totalRead += read;

            if (read < toRead)
                break;
        }

        return totalRead;
#else
        return Source.Read(buffer);
#endif
    }

    /// <summary>
    /// Throws the <see cref="InvalidOperationException"/> for a reader that was created without a stream.
    /// </summary>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Stream ThrowNoStream()
    {
        throw new InvalidOperationException($"The {nameof(EndianStreamReader.Stream)} is null.");
    }
}