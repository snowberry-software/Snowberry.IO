namespace Snowberry.IO.Reader;

// NOTE(VNC): BinaryReader gained these members in later versions (Read(Span<byte>) in .NET Standard 2.1, a public
// Read7BitEncodedInt/Read7BitEncodedInt64 in .NET 5 and ReadExactly in .NET 7). On the target frameworks where the base
// does not offer them, they are provided here so the public surface is the same on every target framework.
public partial class BaseEndianReader
{
#if NETSTANDARD2_0
    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public int Read(Span<byte> buffer)
    {
        ThrowIfDisposed();
        return _readerStream.ReadReported(buffer);
    }
#endif

#if !NET7_0_OR_GREATER
    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public void ReadExactly(Span<byte> buffer)
    {
        ThrowIfDisposed();

        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = _readerStream.ReadReported(buffer[totalRead..]);

            if (read == 0)
                ThrowEndOfStreamException();

            totalRead += read;
        }
    }
#endif

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes into <paramref name="buffer"/>, starting at <paramref name="offset"/>, like
    /// <c>ReadExactly(Span&lt;byte&gt;)</c>.
    /// </summary>
    /// <remarks>
    /// Without <c>BinaryReader.ReadExactly</c> in the base, the bytes are read straight into the array instead of through the span read,
    /// which copies through an intermediate buffer on .NET Standard 2.0.
    /// </remarks>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to start storing the data.</param>
    /// <param name="count">The number of bytes to read; <paramref name="offset"/> and <paramref name="count"/> are already validated.</param>
    /// <exception cref="EndOfStreamException">The end of the stream is reached before <paramref name="count"/> bytes are read.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    private void ReadExactlyInto(byte[] buffer, int offset, int count)
    {
#if NET7_0_OR_GREATER
        ReadExactly(buffer.AsSpan(offset, count));
#else
        ThrowIfDisposed();
        _readerStream.FillReported(buffer, offset, offset + count);
#endif
    }

#if !NET5_0_OR_GREATER
    /// <inheritdoc/>
    /// <exception cref="FormatException">Thrown when the encoded value uses more than 5 bytes.</exception>
    public new int Read7BitEncodedInt()
    {
        // NOTE(VNC): Mirrors System.IO.BinaryReader.Read7BitEncodedInt of .NET 5 and later, including its overflow guard.
        uint result = 0;

        const int maxBytesWithoutOverflow = 4;
        for (int shift = 0; shift < maxBytesWithoutOverflow * 7; shift += 7)
        {
            byte b = ReadByte();
            result |= (b & 0x7Fu) << shift;

            if (b <= 0x7Fu)
                return (int)result;
        }

        byte lastByte = ReadByte();

        if (lastByte > 0b1111u)
            throw new FormatException("Too many bytes in what should have been a 7-bit encoded 32-bit integer.");

        result |= (uint)lastByte << (maxBytesWithoutOverflow * 7);
        return (int)result;
    }

    /// <inheritdoc/>
    /// <exception cref="FormatException">Thrown when the encoded value uses more than 10 bytes.</exception>
    public long Read7BitEncodedInt64()
    {
        // NOTE(VNC): Mirrors System.IO.BinaryReader.Read7BitEncodedInt64 of .NET 5 and later, including its overflow guard.
        ulong result = 0;

        const int maxBytesWithoutOverflow = 9;
        for (int shift = 0; shift < maxBytesWithoutOverflow * 7; shift += 7)
        {
            byte b = ReadByte();
            result |= (b & 0x7Ful) << shift;

            if (b <= 0x7Fu)
                return (long)result;
        }

        byte lastByte = ReadByte();

        if (lastByte > 0b1u)
            throw new FormatException("Too many bytes in what should have been a 7-bit encoded 64-bit integer.");

        result |= (ulong)lastByte << (maxBytesWithoutOverflow * 7);
        return (long)result;
    }
#endif
}