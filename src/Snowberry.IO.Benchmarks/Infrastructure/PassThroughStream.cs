namespace Snowberry.IO.Benchmarks.Infrastructure;

/// <summary>
/// A read-only stream that forwards every read and seek to another stream.
/// </summary>
/// <remarks>
/// <see cref="BinaryReader"/> reads a <see cref="MemoryStream"/> through a private fast path, so a <see cref="BinaryReader"/> over this
/// stream measures what reading through any other stream costs, which is the lower bound for a reader that wraps the stream.
/// The inner stream is not disposed with this stream.
/// </remarks>
/// <param name="inner">The stream to forward to.</param>
internal sealed class PassThroughStream(Stream inner) : Stream
{
    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return inner.Read(buffer, offset, count);
    }

#if NETCOREAPP2_1_OR_GREATER
    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        return inner.Read(buffer);
    }
#endif

    /// <inheritdoc/>
    public override int ReadByte()
    {
        return inner.ReadByte();
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return inner.Seek(offset, origin);
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
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => inner.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => inner.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }
}