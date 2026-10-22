using System.Buffers;
using System.Security.Cryptography;

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// A write-only stream that collects the output in a pooled buffer and writes it to the destination in chunks of the buffer's size.
/// </summary>
/// <remarks>
/// Besides writes, callers can read data straight into the free part of the buffer (<see cref="Free"/> and <see cref="Advance"/>), so data from a
/// source reaches the destination with a single copy. Staged data is written only when the buffer is full or by <see cref="FlushBufferAsync"/>.
/// </remarks>
/// <param name="destination">The stream that receives the output.</param>
/// <param name="bufferSize">The size of the buffer and of the writes to <paramref name="destination"/>.</param>
internal sealed class StagingStream(Stream destination, int bufferSize) : Stream
{
    private byte[]? _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
    private int _count;
    private long _length;

    /// <summary>
    /// Marks bytes written into <see cref="Free"/> as part of the output.
    /// </summary>
    /// <param name="count">The number of bytes written at the start of <see cref="Free"/>.</param>
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)count, (uint)(bufferSize - _count), nameof(count));
        _count += count;
        _length += count;
    }

    /// <summary>
    /// Reads <paramref name="source"/> to its end straight into the buffer, feeding every byte to <paramref name="hash"/> (if any).
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The stream to read.</param>
    /// <param name="hash">The hash that receives every byte read, or <see langword="null"/> to skip hashing.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The number of bytes read.</returns>
    public async ValueTask<long> CopyFromAsync(bool useAsync, Stream source, IncrementalHash? hash, CancellationToken cancellationToken)
    {
        long total = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureFreeAsync(useAsync, cancellationToken).ConfigureAwait(false);

            var free = Free;
            int read = useAsync
                ? await source.ReadAtLeastAsync(free, free.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false)
                : source.ReadAtLeast(free.Span, free.Length, throwOnEndOfStream: false);

            hash?.AppendData(free.Span[..read]);
            Advance(read);
            total += read;

            // A partial read means the source has ended.
            if (read < free.Length)
                return total;
        }
    }

    /// <summary>
    /// Writes the buffer to the destination if it is full, so <see cref="Free"/> is not empty.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when <see cref="Free"/> has room.</returns>
    public ValueTask EnsureFreeAsync(bool useAsync, CancellationToken cancellationToken)
    {
        return _count == bufferSize ? FlushBufferAsync(useAsync, cancellationToken) : ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        FlushBufferAsync(useAsync: false, CancellationToken.None).GetAwaiter().GetResult();
        destination.Flush();
    }

    /// <inheritdoc/>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await FlushBufferAsync(useAsync: true, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the staged bytes to the destination without flushing the destination.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the staged bytes are written.</returns>
    public ValueTask FlushBufferAsync(bool useAsync, CancellationToken cancellationToken)
    {
        if (_count == 0)
            return ValueTask.CompletedTask;

        var staged = Buffer.AsMemory(0, _count);
        _count = 0;
        return StreamCopy.WriteAsync(useAsync, destination, staged, cancellationToken);
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
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
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            EnsureFreeAsync(useAsync: false, CancellationToken.None).GetAwaiter().GetResult();

            var free = Free.Span;
            int chunk = Math.Min(free.Length, buffer.Length);
            buffer[..chunk].CopyTo(free);
            Advance(chunk);
            buffer = buffer[chunk..];
        }
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!buffer.IsEmpty)
        {
            await EnsureFreeAsync(useAsync: true, cancellationToken).ConfigureAwait(false);

            var free = Free;
            int chunk = Math.Min(free.Length, buffer.Length);
            buffer[..chunk].CopyTo(free);
            Advance(chunk);
            buffer = buffer[chunk..];
        }
    }

    /// <summary>
    /// Appends <paramref name="count"/> zero bytes.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="count">The number of zero bytes; nothing is written when it is zero or negative.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the bytes are staged.</returns>
    public async ValueTask WriteZerosAsync(bool useAsync, long count, CancellationToken cancellationToken)
    {
        while (count > 0)
        {
            await EnsureFreeAsync(useAsync, cancellationToken).ConfigureAwait(false);

            var free = Free.Span;
            int chunk = (int)Math.Min(free.Length, count);
            free[..chunk].Clear();
            Advance(chunk);
            count -= chunk;
        }
    }

    /// <summary>
    /// Returns the buffer to the pool; staged bytes that were not written are discarded.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Stream.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => _buffer != null;

    /// <summary>
    /// Gets the free part of the buffer, which is empty when the buffer is full.
    /// </summary>
    public Memory<byte> Free => Buffer.AsMemory(_count, bufferSize - _count);

    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    private byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(StagingStream));
}