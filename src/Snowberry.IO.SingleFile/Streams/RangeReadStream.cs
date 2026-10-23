using System.Buffers;
using System.Runtime.CompilerServices;
using Snowberry.IO.SingleFile.Sources;

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// A read-only, seekable stream over a byte range of a <see cref="BundleSource"/>.
/// </summary>
/// <remarks>
/// With a <paramref name="bufferSize"/>, reads smaller than it are served from a buffer that is filled by one positional read, so a reader that
/// asks for small chunks, such as <see cref="System.IO.Compression.DeflateStream"/>, causes few reads of the source. The buffer is rented from
/// <see cref="ArrayPool{T}.Shared"/> on the first such read and returned when the stream is disposed.
/// </remarks>
/// <param name="source">The source to read from.</param>
/// <param name="offset">The offset of the range in <paramref name="source"/>.</param>
/// <param name="length">The length of the range.</param>
/// <param name="createTruncatedException">
/// Creates the exception thrown when <paramref name="source"/> ends before the range does, from the position reached; <see langword="null"/> to
/// end the stream early instead.
/// </param>
/// <param name="bufferSize">The size of the read buffer, or <c>0</c> to read the source for every read.</param>
internal sealed class RangeReadStream(BundleSource source, long offset, long length, Func<long, Exception>? createTruncatedException = null, int bufferSize = 0) : Stream
{
    private byte[]? _buffer;
    private int _bufferCount;
    private long _bufferStart;
    private bool _disposed;
    private long _position;

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
        ObjectDisposedException.ThrowIf(_disposed, this);

        int toRead = Clamp(buffer.Length);

        if (toRead == 0)
            return 0;

        int read = CopyFromBuffer(buffer[..toRead]);

        if (read == 0)
        {
            if (toRead < bufferSize)
            {
                _buffer ??= ArrayPool<byte>.Shared.Rent(bufferSize);
                FillBuffer(source.Read(offset + _position, _buffer.AsSpan(0, Clamp(bufferSize))));
                read = CopyFromBuffer(buffer[..toRead]);
            }
            else
            {
                read = source.Read(offset + _position, buffer[..toRead]);
            }
        }

        if (read == 0)
            ThrowIfTruncated();

        _position += read;
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        int toRead = Clamp(buffer.Length);

        if (toRead == 0)
            return 0;

        int read = CopyFromBuffer(buffer.Span[..toRead]);

        if (read == 0)
        {
            if (toRead < bufferSize)
            {
                _buffer ??= ArrayPool<byte>.Shared.Rent(bufferSize);
                FillBuffer(await source.ReadAsync(offset + _position, _buffer.AsMemory(0, Clamp(bufferSize)), cancellationToken).ConfigureAwait(false));
                read = CopyFromBuffer(buffer.Span[..toRead]);
            }
            else
            {
                read = await source.ReadAsync(offset + _position, buffer[..toRead], cancellationToken).ConfigureAwait(false);
            }
        }

        if (read == 0)
            ThrowIfTruncated();

        _position += read;
        return read;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long basePosition = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => _position,
            SeekOrigin.End => length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        // Seeking before the beginning is an IOException by the Stream contract.
        if (offset < -basePosition)
            throw new IOException("An attempt was made to move the position before the beginning of the stream.");

        _position = offset > long.MaxValue - basePosition ? long.MaxValue : basePosition + offset;
        return _position;
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
        _disposed = true;

        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        base.Dispose(disposing);
    }

    private int Clamp(int requested)
    {
        long remaining = length - _position;
        return remaining <= 0 ? 0 : (int)Math.Min(requested, remaining);
    }

    /// <summary>
    /// Copies buffered bytes at the current position.
    /// </summary>
    /// <returns>The number of bytes copied, or <c>0</c> if the buffer does not hold the current position.</returns>
    private int CopyFromBuffer(Span<byte> destination)
    {
        long index = _position - _bufferStart;

        if (_buffer == null || index < 0 || index >= _bufferCount)
            return 0;

        int count = (int)Math.Min(destination.Length, _bufferCount - index);
        _buffer.AsSpan((int)index, count).CopyTo(destination);
        return count;
    }

    private void FillBuffer(int read)
    {
        _bufferStart = _position;
        _bufferCount = read;
    }

    private void ThrowIfTruncated()
    {
        if (createTruncatedException != null)
            throw createTruncatedException(_position);
    }

    /// <inheritdoc/>
    public override bool CanRead => !_disposed;

    /// <inheritdoc/>
    public override bool CanSeek => !_disposed;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return length;
        }
    }

    /// <inheritdoc/>
    public override long Position
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _position;
        }

        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }
}