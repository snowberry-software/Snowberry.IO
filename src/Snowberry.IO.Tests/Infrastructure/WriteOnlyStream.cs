namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// A write-only, non-seekable stream that keeps what is written in memory.
/// </summary>
internal sealed class WriteOnlyStream : Stream
{
    private readonly MemoryStream _inner = new();

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public byte[] ToArray()
    {
        return _inner.ToArray();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
}