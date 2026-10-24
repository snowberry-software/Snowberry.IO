using System.Security.Cryptography;

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// A write-only stream that forwards writes to <paramref name="destination"/> and computes the SHA-256 hash of every page of the first
/// <paramref name="limit"/> bytes written, as a code signature needs them.
/// </summary>
/// <param name="destination">The stream that receives the written bytes; it is not disposed.</param>
/// <param name="limit">The number of bytes to hash; later bytes are only forwarded.</param>
/// <param name="pageSize">The size in bytes of the hashed pages; the last page may be shorter.</param>
internal sealed class PageHashingStream(Stream destination, long limit, int pageSize) : Stream
{
    private const int c_HashSize = 32;

    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _hashes = new byte[checked((int)((limit + pageSize - 1) / pageSize * c_HashSize))];
    private long _position;

    /// <inheritdoc/>
    public override void Flush()
    {
        destination.Flush();
    }

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return destination.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the page hashes, in page order.
    /// </summary>
    /// <returns>The concatenated 32-byte hashes.</returns>
    /// <exception cref="InvalidOperationException">Fewer bytes than the hashed limit have been written.</exception>
    public ReadOnlySpan<byte> GetPageHashes()
    {
        if (_position < limit)
            throw new InvalidOperationException("The hashed content has not been written completely.");

        return _hashes;
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
        Hash(buffer);
        destination.Write(buffer);
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Hash(buffer.Span);
        return destination.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _hash.Dispose();

        base.Dispose(disposing);
    }

    private void Hash(ReadOnlySpan<byte> buffer)
    {
        while (!buffer.IsEmpty && _position < limit)
        {
            long pageEnd = Math.Min(((_position / pageSize) + 1) * pageSize, limit);
            int chunk = (int)Math.Min(buffer.Length, pageEnd - _position);
            _hash.AppendData(buffer[..chunk]);
            _position += chunk;
            buffer = buffer[chunk..];

            if (_position == pageEnd)
                _hash.GetHashAndReset(_hashes.AsSpan((int)((_position - 1) / pageSize * c_HashSize), c_HashSize));
        }

        _position += buffer.Length;
    }

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
}
