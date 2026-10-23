namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// A read-only stream that starts with the given bytes and reads as zeros up to a much larger length, without storing them.
/// </summary>
internal sealed class ZeroPaddedStream(byte[] prefix, long length) : Stream
{
    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(buffer.Length, Math.Max(0, length - Position));
        var destination = buffer[..count];
        destination.Clear();

        if (Position < prefix.Length)
            prefix.AsSpan((int)Position, (int)Math.Min(count, prefix.Length - Position)).CopyTo(destination);

        Position += count;
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            _ => length + offset
        };

        return Position;
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position { get; set; }
}