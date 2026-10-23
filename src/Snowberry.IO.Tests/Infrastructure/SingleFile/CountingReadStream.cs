namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// A read-only, seekable stream over a byte array that counts the bytes and the read calls.
/// </summary>
/// <param name="data">The bytes to read.</param>
internal sealed class CountingReadStream(byte[] data) : Stream
{
    private readonly MemoryStream _inner = new(data, writable: false);

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        int read = _inner.Read(buffer);
        BytesRead += read;
        Reads++;
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return new ValueTask<int>(Read(buffer.Span));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return Task.FromResult(Read(buffer, offset, count));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _inner.Seek(offset, origin);
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public int Reads { get; private set; }
}