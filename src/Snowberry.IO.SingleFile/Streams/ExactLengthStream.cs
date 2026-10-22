using System.Runtime.CompilerServices;

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// A read-only stream that yields exactly <paramref name="length"/> bytes from an inner stream, and fails if the inner stream
/// produces fewer or more bytes. Used to bound decompression of untrusted entries.
/// </summary>
internal sealed class ExactLengthStream(Stream inner, long length, string entryName) : Stream
{
    private long _position;
    private byte[]? _probeBuffer;

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        long remaining = length - _position;

        if (remaining == 0)
        {
            Span<byte> probe = stackalloc byte[1];

            if (inner.Read(probe) != 0)
                throw TooLong();

            return 0;
        }

        int read = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);

        if (read == 0)
            throw TooShort();

        _position += read;

        // Verify the end as soon as the last declared byte is delivered, so readers that stop at Length still detect extra data.
        if (_position == length)
        {
            Span<byte> probe = stackalloc byte[1];

            if (inner.Read(probe) != 0)
                throw TooLong();
        }

        return read;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
            return 0;

        long remaining = length - _position;

        if (remaining == 0)
        {
            if (await inner.ReadAsync(ProbeBuffer, cancellationToken).ConfigureAwait(false) != 0)
                throw TooLong();

            return 0;
        }

        int read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], cancellationToken).ConfigureAwait(false);

        if (read == 0)
            throw TooShort();

        _position += read;

        if (_position == length && await inner.ReadAsync(ProbeBuffer, cancellationToken).ConfigureAwait(false) != 0)
            throw TooLong();

        return read;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();

        base.Dispose(disposing);
    }

    private InvalidDataException TooLong()
    {
        return new InvalidDataException($"The bundle entry '{entryName}' contains more data than the {length} bytes it declares.");
    }

    private InvalidDataException TooShort()
    {
        return new InvalidDataException($"The bundle entry '{entryName}' ended after {_position} bytes, but declares {length} bytes.");
    }

    /// <inheritdoc/>
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    private byte[] ProbeBuffer => _probeBuffer ??= new byte[1];
}