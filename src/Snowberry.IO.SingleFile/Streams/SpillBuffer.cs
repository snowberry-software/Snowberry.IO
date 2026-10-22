using System.Buffers;
using System.Numerics;
using System.Security.Cryptography;

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// A write-only buffer that keeps its data in pooled memory while <paramref name="budget"/> allows it and moves it to a file of
/// <paramref name="files"/> once the budget is exhausted.
/// </summary>
/// <remarks>
/// <para>The memory is a list of segments rented from <see cref="ArrayPool{T}.Shared"/> that start at 16 KiB and double up to 1 MiB, so a small
/// buffer holds little memory and growing never copies data. With an <paramref name="expectedLength"/>, the segments are sized to hold that
/// many bytes with less than 16 KiB to spare instead. The budget is charged for each rented segment; when it cannot cover another one, even of
/// the smallest size, the data moves to a file and the segments are returned.</para>
/// <para>A spilled buffer appends to its file exclusively until <see cref="Complete"/> is called. Disposing returns the segments and their size to
/// the budget, and gives the file region of a buffer that was not completed back to the next buffer that spills.</para>
/// </remarks>
/// <param name="budget">The memory shared by all buffers of one operation.</param>
/// <param name="files">The temporary files shared by all buffers of one operation.</param>
/// <param name="expectedLength">The number of bytes the buffer is expected to receive, or <c>-1</c> if it is not known.</param>
internal sealed class SpillBuffer(MemoryBudget budget, SpillFilePool files, long expectedLength = -1) : Stream
{
    private const int c_CopyChunkSize = 81920;
    private const int c_MaxSegmentLength = 1024 * 1024;
    private const int c_MinSegmentLength = 16 * 1024;

    private readonly List<ArraySegment<byte>> _segments = [];
    private long _capacity;
    private bool _disposed;
    private SpillFile? _file;
    private long _fileOffset;
    private bool _holdsFile;
    private int _lastSegmentUsed;
    private long _length;
    private long _reserved;

    /// <summary>
    /// Gets the number of bytes of budget a buffer with an expected length of <paramref name="length"/> reserves for that many bytes when the
    /// budget allows every segment at its full size.
    /// </summary>
    /// <param name="length">The number of bytes of data.</param>
    /// <returns>The total size of the segments that hold the data, at most 16 KiB more than <paramref name="length"/>.</returns>
    public static long GetMemoryFootprint(long length)
    {
        // The result saturates instead of overflowing.
        if (length > long.MaxValue - c_MaxSegmentLength)
            return long.MaxValue;

        long footprint = length / c_MaxSegmentLength * c_MaxSegmentLength;

        for (long remaining = length - footprint; remaining > 0;)
        {
            int segment = GetSegmentLength(remaining);
            footprint += segment;
            remaining -= segment;
        }

        return footprint;
    }

    /// <summary>
    /// Gets the largest segment length, a power of two from 16 KiB to 1 MiB, that does not exceed <paramref name="remaining"/>, or 16 KiB.
    /// </summary>
    private static int GetSegmentLength(long remaining)
    {
        return (int)Math.Clamp(1L << BitOperations.Log2((ulong)remaining), c_MinSegmentLength, c_MaxSegmentLength);
    }

    /// <summary>
    /// Marks the data as complete, so the file of a spilled buffer can be used by other buffers.
    /// </summary>
    public void Complete()
    {
        if (_holdsFile)
        {
            _holdsFile = false;
            files.Return(_file!);
        }
    }

    /// <summary>
    /// Reads <paramref name="source"/> to its end straight into the segments, feeding every byte to <paramref name="hash"/> (if any).
    /// </summary>
    /// <remarks>Data beyond the budget is copied to the file as <see cref="Write(ReadOnlySpan{byte})"/> does.</remarks>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The stream to read.</param>
    /// <param name="hash">The hash that receives every byte read, or <see langword="null"/> to skip hashing.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The number of bytes read.</returns>
    public async ValueTask<long> CopyFromAsync(bool useAsync, Stream source, IncrementalHash? hash, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long total = 0;

        while (_file == null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_segments.Count == 0 || _lastSegmentUsed == _segments[^1].Count)
            {
                // With the expected length in the segments, the source has to prove that it has more before more memory is taken.
                if (expectedLength >= 0 && _length >= expectedLength)
                {
                    byte[] probe = new byte[1];
                    int extra = useAsync ? await source.ReadAsync(probe, cancellationToken).ConfigureAwait(false) : source.Read(probe);

                    if (extra == 0)
                        return total;

                    hash?.AppendData(probe);
                    await StreamCopy.WriteAsync(useAsync, this, probe, cancellationToken).ConfigureAwait(false);
                    total++;
                    continue;
                }

                if (!TryAddSegment())
                {
                    if (useAsync)
                        await SpillAsync(cancellationToken).ConfigureAwait(false);
                    else
                        Spill();

                    break;
                }
            }

            var segment = _segments[^1];
            var free = segment.AsMemory(_lastSegmentUsed, segment.Count - _lastSegmentUsed);
            int read = useAsync
                ? await source.ReadAtLeastAsync(free, free.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false)
                : source.ReadAtLeast(free.Span, free.Length, throwOnEndOfStream: false);

            hash?.AppendData(free.Span[..read]);
            _lastSegmentUsed += read;
            _length += read;
            total += read;

            // A partial read means the source has ended.
            if (read < free.Length)
                return total;
        }

        return total + await StreamCopy.CopyAsync(useAsync, source, this, hash, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Flush()
    {
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (!buffer.IsEmpty)
        {
            if (_file != null)
            {
                RandomAccess.Write(_file.Handle, buffer, _file.Length);
                Appended(buffer.Length);
                return;
            }

            int copied = AppendToMemory(buffer);

            if (copied == 0)
                Spill();

            buffer = buffer[copied..];
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (!buffer.IsEmpty)
        {
            if (_file != null)
            {
                await RandomAccess.WriteAsync(_file.Handle, buffer, _file.Length, cancellationToken).ConfigureAwait(false);
                Appended(buffer.Length);
                return;
            }

            int copied = AppendToMemory(buffer.Span);

            if (copied == 0)
                await SpillAsync(cancellationToken).ConfigureAwait(false);

            buffer = buffer[copied..];
        }
    }

    /// <summary>
    /// Writes the data to <paramref name="destination"/>: memory segments directly, and spilled data in chunks read from the file.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The number of bytes written, which is less than <see cref="Length"/> only when the temporary file was truncated.</returns>
    public async ValueTask<long> WriteToAsync(bool useAsync, Stream destination, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_file == null)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var segment = _segments[i];
                int used = i == _segments.Count - 1 ? _lastSegmentUsed : segment.Count;
                await StreamCopy.WriteAsync(useAsync, destination, segment.AsMemory(0, used), cancellationToken).ConfigureAwait(false);
            }

            return _length;
        }

        byte[] chunk = ArrayPool<byte>.Shared.Rent((int)Math.Min(_length, c_CopyChunkSize));

        try
        {
            long copied = 0;

            while (copied < _length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = chunk.AsMemory(0, (int)Math.Min(chunk.Length, _length - copied));
                long offset = _fileOffset + copied;
                int read = useAsync
                    ? await RandomAccess.ReadAsync(_file.Handle, target, offset, cancellationToken).ConfigureAwait(false)
                    : RandomAccess.Read(_file.Handle, target.Span, offset);

                if (read == 0)
                    break;

                await StreamCopy.WriteAsync(useAsync, destination, chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
            }

            return copied;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            ReturnSegments();

            // Nothing was appended after this buffer's data while it held the file, so the next buffer can overwrite it.
            if (_holdsFile)
            {
                _holdsFile = false;
                _file!.Length = _fileOffset;
                files.Return(_file);
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Copies as much of <paramref name="data"/> as fits into the last segment, renting a new segment when it is full.
    /// </summary>
    /// <returns>The number of bytes copied, or <c>0</c> if the budget cannot cover another segment.</returns>
    private int AppendToMemory(ReadOnlySpan<byte> data)
    {
        if ((_segments.Count == 0 || _lastSegmentUsed == _segments[^1].Count) && !TryAddSegment())
            return 0;

        var segment = _segments[^1];
        int count = Math.Min(data.Length, segment.Count - _lastSegmentUsed);
        data[..count].CopyTo(segment.AsSpan(_lastSegmentUsed));
        _lastSegmentUsed += count;
        _length += count;
        return count;
    }

    private void Appended(int count)
    {
        _file!.Length += count;
        _length += count;
    }

    private void BeginSpill()
    {
        _file = files.Rent();
        _holdsFile = true;
        _fileOffset = _file.Length;
    }

    private void ReturnSegments()
    {
        foreach (var segment in _segments)
            ArrayPool<byte>.Shared.Return(segment.Array!);

        _segments.Clear();
        _capacity = 0;
        _lastSegmentUsed = 0;
        budget.Release(_reserved);
        _reserved = 0;
    }

    private ReadOnlyMemory<byte> SegmentData(int index)
    {
        var segment = _segments[index];
        return segment.AsMemory(0, index == _segments.Count - 1 ? _lastSegmentUsed : segment.Count);
    }

    private void Spill()
    {
        BeginSpill();

        for (int i = 0; i < _segments.Count; i++)
        {
            var data = SegmentData(i);
            RandomAccess.Write(_file!.Handle, data.Span, _file.Length);
            _file.Length += data.Length;
        }

        ReturnSegments();
    }

    private async ValueTask SpillAsync(CancellationToken cancellationToken)
    {
        BeginSpill();

        for (int i = 0; i < _segments.Count; i++)
        {
            var data = SegmentData(i);
            await RandomAccess.WriteAsync(_file!.Handle, data, _file.Length, cancellationToken).ConfigureAwait(false);
            _file.Length += data.Length;
        }

        ReturnSegments();
    }

    /// <summary>
    /// Rents the next segment: sized to what is left of the expected length, or else twice as large as the previous one up to 1 MiB; smaller if
    /// the budget cannot cover that.
    /// </summary>
    private bool TryAddSegment()
    {
        long expectedRemaining = expectedLength - _capacity;
        int length = expectedLength >= 0 && expectedRemaining > 0
            ? GetSegmentLength(expectedRemaining)
            : Math.Min(c_MinSegmentLength << Math.Min(_segments.Count, 6), c_MaxSegmentLength);

        for (; length >= c_MinSegmentLength; length /= 2)
        {
            if (!budget.TryReserve(length))
                continue;

            _reserved += length;
            _capacity += length;
            _segments.Add(new ArraySegment<byte>(ArrayPool<byte>.Shared.Rent(length), 0, length));
            _lastSegmentUsed = 0;
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => !_disposed;

    /// <summary>
    /// Gets a value indicating whether the data was moved to a temporary file.
    /// </summary>
    public bool IsSpilled => _file != null;

    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Gets the number of bytes of the budget the buffer holds: the size of its segments until it spills, and <c>0</c> after.
    /// </summary>
    public long ReservedMemory => _reserved;
}