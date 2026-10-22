using System.Runtime.CompilerServices;
using Snowberry.IO.Common;

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <inheritdoc/>
    public bool TryReadByte(out byte value)
    {
        if (!TryFillScratch(1))
        {
            value = 0;
            return false;
        }

        value = _scratch[0];
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadDouble(out double value)
    {
        return TryReadDouble(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadDouble(out double value, EndianType endian)
    {
        if (!TryFillScratch(8))
        {
            value = 0;
            return false;
        }

        value = BitConverter.Int64BitsToDouble(DecodeInt64(_scratch, endian));
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadGuid(out Guid value)
    {
        return TryReadGuid(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadGuid(out Guid value, EndianType endian)
    {
        if (!TryFillScratch(16))
        {
            value = Guid.Empty;
            return false;
        }

        value = BinaryEndianConverter.ToGuid(new ReadOnlySpan<byte>(_scratch, 0, 16), 0, endian);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadInt16(out short value)
    {
        return TryReadInt16(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadInt16(out short value, EndianType endian)
    {
        if (!TryFillScratch(2))
        {
            value = 0;
            return false;
        }

        value = DecodeInt16(_scratch, endian);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadInt32(out int value)
    {
        return TryReadInt32(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadInt32(out int value, EndianType endian)
    {
        if (!TryFillScratch(4))
        {
            value = 0;
            return false;
        }

        value = DecodeInt32(_scratch, endian);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadInt64(out long value)
    {
        return TryReadInt64(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadInt64(out long value, EndianType endian)
    {
        if (!TryFillScratch(8))
        {
            value = 0;
            return false;
        }

        value = DecodeInt64(_scratch, endian);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadSByte(out sbyte value)
    {
        if (!TryReadByte(out byte b))
        {
            value = 0;
            return false;
        }

        value = (sbyte)b;
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadSha1(out Sha1 value)
    {
        if (!TryFillScratch(Sha1.c_StructSize))
        {
            value = Sha1.s_Zero;
            return false;
        }

        value = new Sha1(new ReadOnlySpan<byte>(_scratch, 0, Sha1.c_StructSize));
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadSingle(out float value)
    {
        return TryReadSingle(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadSingle(out float value, EndianType endian)
    {
        if (!TryFillScratch(4))
        {
            value = 0;
            return false;
        }

        value = Int32BitsToSingle(DecodeInt32(_scratch, endian));
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadUInt16(out ushort value)
    {
        return TryReadUInt16(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadUInt16(out ushort value, EndianType endian)
    {
        if (!TryFillScratch(2))
        {
            value = 0;
            return false;
        }

        value = (ushort)DecodeInt16(_scratch, endian);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadUInt32(out uint value)
    {
        return TryReadUInt32(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadUInt32(out uint value, EndianType endian)
    {
        if (!TryFillScratch(4))
        {
            value = 0;
            return false;
        }

        value = (uint)DecodeInt32(_scratch, endian);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadUInt64(out ulong value)
    {
        return TryReadUInt64(out value, EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public bool TryReadUInt64(out ulong value, EndianType endian)
    {
        if (!TryFillScratch(8))
        {
            value = 0;
            return false;
        }

        value = (ulong)DecodeInt64(_scratch, endian);
        return true;
    }

    /// <summary>
    /// Reports the first <paramref name="count"/> bytes of the scratch buffer to the <see cref="Analyzer"/>, if one is set.
    /// </summary>
    /// <param name="buffer">The scratch buffer.</param>
    /// <param name="count">The number of bytes that were read.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReportScratch(byte[] buffer, int count)
    {
        _analyzer?.AnalyzeReadBytes(this, buffer, count);
    }

    /// <summary>
    /// Reads <paramref name="count"/> bytes into the scratch buffer, or returns <see langword="false"/> if the end of the stream comes first.
    /// </summary>
    /// <remarks>
    /// Unlike the fixed-size reads, this does not throw at the end of the stream. The <see cref="Position"/> is restored to where it was
    /// before this call only when the underlying data source can seek; otherwise the bytes read before the end of the stream are consumed.
    /// The <see cref="Analyzer"/> receives the bytes once they are consumed: all of them on success, the partial bytes when they cannot be
    /// rewound, and nothing when the position is restored. The common case of one complete read neither queries nor sets the position.
    /// </remarks>
    /// <param name="count">The number of bytes to read, at most the size of the scratch buffer.</param>
    /// <returns><see langword="true"/> if <paramref name="count"/> bytes were read into the scratch buffer; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryFillScratch(int count)
    {
        ThrowIfDisposed();

        byte[] buffer = _scratch;
        var direct = _direct;
        int read = direct is null ? _readerStream.ReadUnreported(buffer, 0, count) : direct.Read(buffer, 0, count);

        if (read != count && !TryFillScratchRest(buffer, read, count))
            return false;

        // The direct stream is only used while there is no analyzer.
        if (direct is null)
            ReportScratch(buffer, count);

        return true;
    }

    /// <summary>
    /// Continues <see cref="TryFillScratch"/> after a first read that returned fewer than <paramref name="count"/> bytes.
    /// </summary>
    /// <remarks>
    /// When the end of the data is reached, the bytes read so far are rewound when the underlying data source can seek, and reported to
    /// the <see cref="Analyzer"/> as consumed otherwise.
    /// </remarks>
    /// <param name="buffer">The scratch buffer.</param>
    /// <param name="read">The number of bytes the first read returned.</param>
    /// <param name="count">The number of bytes to read in total.</param>
    /// <returns><see langword="true"/> if <paramref name="count"/> bytes were read; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryFillScratchRest(byte[] buffer, int read, int count)
    {
        int totalRead = 0;

        while (read != 0)
        {
            totalRead += read;

            if (totalRead == count)
                return true;

            read = _readerStream.ReadUnreported(buffer, totalRead, count - totalRead);
        }

        if (totalRead > 0)
        {
            if (CanSeek)
                _readerStream.Position -= totalRead;
            else
                Analyzer?.AnalyzeReadBytes(this, buffer, totalRead);
        }

        return false;
    }
}