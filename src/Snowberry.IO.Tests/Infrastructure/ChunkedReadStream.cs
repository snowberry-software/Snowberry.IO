namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// A read-only stream over a byte array that returns at most <c>maxBytesPerRead</c> bytes per read call,
/// and optionally reports itself as non-seekable (then <see cref="Length"/> and <see cref="Position"/> throw).
/// </summary>
/// <param name="data">The bytes to read.</param>
/// <param name="maxBytesPerRead">The maximum number of bytes that one read call returns.</param>
/// <param name="canSeek"><see langword="false"/> to report the stream as non-seekable.</param>
internal sealed class ChunkedReadStream(byte[] data, int maxBytesPerRead = int.MaxValue, bool canSeek = true) : Stream
{
    private readonly MemoryStream _inner = new(data);

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _inner.Read(buffer, offset, Math.Min(count, maxBytesPerRead));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return canSeek ? _inner.Seek(offset, origin) : throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();

        base.Dispose(disposing);
    }

    public override bool CanRead => true;

    public override bool CanSeek => canSeek;

    public override bool CanWrite => false;

    public override long Length => canSeek ? _inner.Length : throw new NotSupportedException();

    public override long Position
    {
        get => canSeek ? _inner.Position : throw new NotSupportedException();
        set
        {
            if (!canSeek)
                throw new NotSupportedException();

            _inner.Position = value;
        }
    }
}