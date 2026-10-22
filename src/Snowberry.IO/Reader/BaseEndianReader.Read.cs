using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Reader.Streams;

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadByteSafe()
    {
        ThrowIfDisposed();

        var direct = _direct;
        return direct is null ? _readerStream.ReadByte() : direct.ReadByte();
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public void ReadExactly(byte[] buffer, int offset, int byteCount)
    {
        _ = buffer ?? throw new ArgumentNullException(nameof(buffer));
        var span = buffer.AsSpan(offset, byteCount);

        ThrowIfDisposed();

        // A request for zero bytes is trivially satisfied, regardless of the stream's state.
        if (span.IsEmpty)
            return;

        if (IsRegionViewEnabled && byteCount > Length - Position)
            ThrowEndOfStreamException();

        if (!CanSeek)
        {
            // Nothing to roll back to on a non-seekable stream; read directly.
            ReadExactlyInto(buffer, offset, byteCount);
            return;
        }

        long startPosition = _readerStream.Position;

        try
        {
            ReadExactlyInto(buffer, offset, byteCount);
        }
        catch (EndOfStreamException)
        {
            // NOTE(VNC): Keeps Position consistent with "no bytes were consumed" after a partial/failed exact read.
            _readerStream.Position = startPosition;
            throw;
        }
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInInternalBuffer(int byteCount, int offset)
    {
        return Read(_buffer, offset, byteCount);
    }

    /// <inheritdoc/>
    public string? ReadLine()
    {
        ThrowIfDisposed();

        if (!CanReadData)
            return null;

        long startPosition = Position;
        string line = ReadTerminated(long.MaxValue, isLine: true, out long consumed, out bool terminatorFound);

        // Rewind to right after the line break (the last read may have read past it); without one, the data was read to its end.
        if (terminatorFound)
            Position = startPosition + consumed;

        return line;
    }

    /// <summary>
    /// The number of bytes that a string read requests after its first <see cref="c_MaxCharBytesSize"/> bytes; it doubles with every
    /// further read up to <see cref="c_MaxStringReadSize"/>.
    /// </summary>
    private const int c_FirstStringGrowthSize = 1024;

    /// <summary>
    /// The maximum number of bytes that a single read of a string requests.
    /// </summary>
    private const int c_MaxStringReadSize = 64 * 1024;

    /// <summary>
    /// The maximum number of bytes of a string that are collected and decoded at once; the bytes of a longer string are decoded in
    /// pieces into <see cref="_stringBuilder"/>.
    /// </summary>
#if NETSTANDARD2_0
    // NOTE(VNC): ArrayPool<byte>.Shared of System.Buffers, which .NET Framework uses, does not pool arrays larger than 1 MiB,
    // so a larger buffer would be allocated on every read.
    private const int c_MaxSinglePassStringSize = 1024 * 1024;
#else
    private const int c_MaxSinglePassStringSize = 16 * 1024 * 1024;
#endif

    /// <summary>
    /// The number of bytes that are passed to a decoder at once when the bytes of a string are decoded in pieces.
    /// </summary>
    private const int c_DecodePieceSize = 16 * 1024;

    /// <summary>
    /// The size of a memory page; the string reads of process memory after the first <see cref="c_MaxCharBytesSize"/> bytes end at
    /// a page boundary.
    /// </summary>
    private const int c_MemoryPageSize = 4096;

    /// <summary>
    /// The CR code unit encoded with <see cref="_encoding"/>, created on the first <see cref="ReadLine"/> call.
    /// </summary>
    private byte[]? _crUnit;

    /// <summary>
    /// The LF code unit encoded with <see cref="_encoding"/>, created on the first <see cref="ReadLine"/> call.
    /// </summary>
    private byte[]? _lfUnit;

    /// <summary>
    /// Fills <paramref name="unit"/> with the code unit that follows the bytes of the current chunk and reports it to the
    /// <see cref="Analyzer"/>.
    /// </summary>
    /// <param name="available">The bytes already read past the line terminator.</param>
    /// <param name="unit">The buffer that receives the next code unit.</param>
    /// <returns><see langword="true"/> if a whole code unit was available; otherwise, <see langword="false"/>.</returns>
    private bool TryPeekUnit(ReadOnlySpan<byte> available, Span<byte> unit)
    {
        if (available.Length >= unit.Length)
        {
            available[..unit.Length].CopyTo(unit);
        }
        else
        {
            available.CopyTo(unit);
            int filled = available.Length;

            while (filled < unit.Length)
            {
                int read = _readerStream.ReadUnreported(unit[filled..]);

                if (read == 0)
                    return false;

                filled += read;
            }
        }

        Analyzer?.AnalyzeReadBytes(this, unit, unit.Length);
        return true;
    }

    /// <summary>
    /// Finds the byte offset of the first aligned CR or LF code unit within <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The data to search; its length is a multiple of <paramref name="unitSize"/>.</param>
    /// <param name="unitSize">The size, in bytes, of a single code unit (1, 2 or 4).</param>
    /// <param name="crUnit">The encoded CR code unit.</param>
    /// <param name="lfUnit">The encoded LF code unit.</param>
    /// <param name="isCr">When this method returns, <see langword="true"/> if the found terminator is a CR.</param>
    /// <returns>The byte offset of the terminator, or -1 if none was found.</returns>
    private static int FindLineTerminator(ReadOnlySpan<byte> data, int unitSize, ReadOnlySpan<byte> crUnit, ReadOnlySpan<byte> lfUnit, out bool isCr)
    {
        int index;

        if (unitSize == 1)
        {
            index = data.IndexOfAny(crUnit[0], lfUnit[0]);
            isCr = index >= 0 && data[index] == crUnit[0];
            return index;
        }

        // NOTE(VNC): The data and the encoded terminators are both read as code units in native byte order, so a unit
        // matches exactly when its bytes match, whatever the byte order of the encoding is.
        if (unitSize == 2)
        {
            var units = MemoryMarshal.Cast<byte, char>(data);
            char cr = MemoryMarshal.Read<char>(crUnit);

            index = units.IndexOfAny(cr, MemoryMarshal.Read<char>(lfUnit));
            isCr = index >= 0 && units[index] == cr;
        }
        else
        {
            var units = MemoryMarshal.Cast<byte, uint>(data);
            uint cr = MemoryMarshal.Read<uint>(crUnit);

            index = units.IndexOfAny(cr, MemoryMarshal.Read<uint>(lfUnit));
            isCr = index >= 0 && units[index] == cr;
        }

        return index < 0 ? -1 : index * unitSize;
    }

    /// <inheritdoc/>
    public virtual string ReadCString()
    {
        ThrowIfDisposed();

        if (!CanReadData)
            ThrowEndOfStreamException();

        long startPosition = Position;
        string text = ReadTerminated(long.MaxValue, isLine: false, out long consumed, out bool terminatorFound);

        if (!terminatorFound)
            ThrowEndOfStreamException();

        Position = startPosition + consumed;
        return text;
    }

    /// <inheritdoc/>
    public string ReadSizedCString(int size, bool adjustPosition = true)
    {
        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Size must be non-negative.");

        long remaining = Length - Position;

        if (size > remaining)
            throw new ArgumentOutOfRangeException(nameof(size), "Size can't be greater than the remaining bytes in the stream.");

        if (size == 0)
            return string.Empty;

        long startPosition = Position;
        string text = ReadTerminated(size, isLine: false, out long consumed, out bool terminatorFound);

        if (!terminatorFound && consumed < size)
            ThrowEndOfStreamException();

        // Land on start + size when requested, otherwise right after the terminator, regardless of how many
        // bytes past the terminator the last read requested.
        Position = startPosition + (adjustPosition ? size : consumed);
        return text;
    }

    /// <summary>
    /// Reads code units of <see cref="Encoding"/> until a null code unit (a CR or LF code unit for a line), <paramref name="maxBytes"/>
    /// bytes or the end of the data is reached, and decodes the bytes before the terminator.
    /// </summary>
    /// <remarks>
    /// <para>The first <see cref="c_MaxCharBytesSize"/> bytes are read into a reused buffer. A longer string continues in a pooled buffer
    /// whose reads grow from <see cref="c_FirstStringGrowthSize"/> to <see cref="c_MaxStringReadSize"/> bytes, so the string is decoded
    /// as a whole (see <see cref="DecodeWhole"/>); that gives the same characters as decoding the bytes in
    /// pieces with a <see cref="Decoder"/>, which only strings longer than <see cref="c_MaxSinglePassStringSize"/> bytes do.</para>
    /// <para>The reads can end past the terminator, so the caller is responsible for setting <see cref="Position"/> afterwards.</para>
    /// </remarks>
    /// <param name="maxBytes">The maximum number of bytes to read.</param>
    /// <param name="isLine"><see langword="true"/> to stop at a CR or LF code unit and to consume an LF that follows a CR.</param>
    /// <param name="consumed">When this method returns, the number of bytes consumed, including the terminator when one was found.</param>
    /// <param name="terminatorFound">When this method returns, <see langword="true"/> if a terminator was found.</param>
    /// <returns>The decoded string, without the terminator.</returns>
    private string ReadTerminated(long maxBytes, bool isLine, out long consumed, out bool terminatorFound)
    {
        // NOTE(VNC): Terminators must be matched per aligned code unit of the configured encoding
        // (1 byte for single-byte/variable-width encodings, 2 bytes for UTF-16, 4 bytes for UTF-32);
        // a plain single-byte search would misfire on the high/low half of a non-zero UTF-16/UTF-32 code unit.
        int unitSize = CodeUnitSize;

        // NOTE(VNC): Using the encoding itself to produce the terminator's byte representation
        // automatically accounts for its byte order (little-endian/big-endian), instead of guessing it.
        byte[] crUnit = isLine ? _crUnit ??= _encoding.GetBytes("\r") : [];
        byte[] lfUnit = isLine ? _lfUnit ??= _encoding.GetBytes("\n") : [];

        byte[] chunk = _terminatedChunk ??= new byte[c_MaxCharBytesSize];
        int valid = 0;

        // The number of bytes at the start of chunk that were searched for the terminator: every whole code unit read so far
        // (a short read can stop in the middle of one).
        int searched = 0;

        while (true)
        {
            if (valid == chunk.Length && maxBytes > valid)
                return ReadTerminatedPooled(chunk, searched, maxBytes, isLine, crUnit, lfUnit, out consumed, out terminatorFound);

            int toRead = (int)Math.Min(chunk.Length - valid, maxBytes - valid);
            int read = toRead == 0 ? 0 : ReadStringBytes(chunk, valid, toRead);
            valid += read;

            int aligned = valid - (valid % unitSize);
            int terminatorIndex = AnalyzeAndFindTerminator(chunk, searched, aligned - searched, unitSize, isLine, crUnit, lfUnit, out bool isCr);

            if (terminatorIndex >= 0)
            {
                terminatorIndex += searched;
                consumed = GetTerminatorEnd(chunk.AsSpan(0, valid), terminatorIndex, unitSize, isCr, lfUnit);
                terminatorFound = true;
                return DecodeWhole(chunk, terminatorIndex);
            }

            searched = aligned;

            if (read == 0)
            {
                // End of data or maxBytes reached: decode the rest, including a trailing partial code unit.
                AnalyzeRemainder(chunk.AsSpan(aligned, valid - aligned));
                consumed = valid;
                terminatorFound = false;
                return DecodeWhole(chunk, valid);
            }
        }
    }

    /// <summary>
    /// The buffer of the first <see cref="c_MaxCharBytesSize"/> bytes of <see cref="ReadTerminated"/>, created on first use.
    /// </summary>
    private byte[]? _terminatedChunk;

    /// <summary>
    /// Reads bytes of a string without reporting them to the <see cref="Analyzer"/>, straight from the user's stream while neither an
    /// analyzer nor a region view is active.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="offset">The offset in <paramref name="buffer"/> at which to store the bytes.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read, or zero at the end of the data or region.</returns>
    private int ReadStringBytes(byte[] buffer, int offset, int count)
    {
        var direct = _direct;
        return direct is null ? _readerStream.ReadUnreported(buffer, offset, count) : direct.Read(buffer, offset, count);
    }

    /// <summary>
    /// Continues <see cref="ReadTerminated"/> once its first buffer is full, in a pooled buffer that grows with the string.
    /// </summary>
    /// <param name="chunk">The bytes read so far.</param>
    /// <param name="searched">The number of bytes at the start of <paramref name="chunk"/> that were searched for the terminator.</param>
    /// <param name="maxBytes">The maximum number of bytes to read, including <paramref name="chunk"/>.</param>
    /// <param name="isLine"><see langword="true"/> to stop at a CR or LF code unit and to consume an LF that follows a CR.</param>
    /// <param name="crUnit">The encoded CR code unit, or empty when <paramref name="isLine"/> is <see langword="false"/>.</param>
    /// <param name="lfUnit">The encoded LF code unit, or empty when <paramref name="isLine"/> is <see langword="false"/>.</param>
    /// <param name="consumed">When this method returns, the number of bytes consumed, including the terminator when one was found.</param>
    /// <param name="terminatorFound">When this method returns, <see langword="true"/> if a terminator was found.</param>
    /// <returns>The decoded string, without the terminator.</returns>
    private string ReadTerminatedPooled(byte[] chunk, int searched, long maxBytes, bool isLine, byte[] crUnit, byte[] lfUnit, out long consumed, out bool terminatorFound)
    {
        int unitSize = CodeUnitSize;
        bool endReadsAtPage = _readerStream is ProcessMemoryReaderStream;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(chunk.Length + c_FirstStringGrowthSize);
        char[]? chars = null;

        // The number of bytes before buffer[0] that were already decoded into _stringBuilder, once the string is decoded in pieces.
        long decoded = 0;

        try
        {
            Array.Copy(chunk, buffer, chunk.Length);
            int valid = chunk.Length;
            int readSize = c_FirstStringGrowthSize;

            while (true)
            {
                int toRead = (int)Math.Min(readSize, maxBytes - decoded - valid);

                // NOTE(VNC): ReadProcessMemory reads nothing when the range touches an unreadable page, so a read stops at the end
                // of the current page instead of reaching into a page that the first, short read did not touch.
                if (endReadsAtPage)
                    toRead = Math.Min(toRead, c_MemoryPageSize - (int)(_readerStream.ActualPosition & (c_MemoryPageSize - 1)));

                if (buffer.Length - valid < toRead)
                {
                    if (valid + toRead <= c_MaxSinglePassStringSize)
                    {
                        buffer = GrowPooledBuffer(buffer, valid, Math.Min(Math.Max(buffer.Length * 2, valid + toRead), c_MaxSinglePassStringSize));
                    }
                    else
                    {
                        // Too long to decode at once: decode the searched code units and keep only a trailing partial one.
                        if (chars is null)
                        {
                            chars = ArrayPool<char>.Shared.Rent(_encoding.GetMaxCharCount(c_DecodePieceSize));
                            _stringBuilder.Clear();

                            // NOTE(VNC): Reuses the shared decoder instead of allocating a new one per call; safe because a single
                            // reader instance never has two decode operations in flight at the same time.
                            _decoder.Reset();
                        }

                        AppendCharacters(_decoder, buffer, searched, chars, flush: false);
                        decoded += searched;
                        valid -= searched;
                        Array.Copy(buffer, searched, buffer, 0, valid);
                        searched = 0;
                    }
                }

                int read = toRead == 0 ? 0 : ReadStringBytes(buffer, valid, toRead);
                valid += read;

                int aligned = valid - (valid % unitSize);
                int terminatorIndex = AnalyzeAndFindTerminator(buffer, searched, aligned - searched, unitSize, isLine, crUnit, lfUnit, out bool isCr);

                if (terminatorIndex >= 0)
                {
                    terminatorIndex += searched;
                    consumed = decoded + GetTerminatorEnd(buffer.AsSpan(0, valid), terminatorIndex, unitSize, isCr, lfUnit);
                    terminatorFound = true;
                    return Decode(buffer, terminatorIndex, chars);
                }

                searched = aligned;

                if (read == 0)
                {
                    // End of data or maxBytes reached: decode the rest, including a trailing partial code unit.
                    AnalyzeRemainder(buffer.AsSpan(aligned, valid - aligned));
                    consumed = decoded + valid;
                    terminatorFound = false;
                    return Decode(buffer, valid, chars);
                }

                readSize = Math.Min(readSize * 2, c_MaxStringReadSize);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);

            if (chars is not null)
                ArrayPool<char>.Shared.Return(chars);
        }
    }

    /// <summary>
    /// Replaces <paramref name="buffer"/> with a larger pooled buffer that starts with the same <paramref name="count"/> bytes.
    /// </summary>
    /// <param name="buffer">The pooled buffer, which is returned to the pool.</param>
    /// <param name="count">The number of bytes at the start of <paramref name="buffer"/> to keep.</param>
    /// <param name="minimumLength">The minimum length of the new buffer.</param>
    /// <returns>The new pooled buffer.</returns>
    private static byte[] GrowPooledBuffer(byte[] buffer, int count, int minimumLength)
    {
        byte[] larger = ArrayPool<byte>.Shared.Rent(minimumLength);
        Array.Copy(buffer, larger, count);
        ArrayPool<byte>.Shared.Return(buffer);
        return larger;
    }

    /// <summary>
    /// Returns the number of bytes up to and including the terminator at <paramref name="terminatorIndex"/>, and for a CR also an LF
    /// that immediately follows it.
    /// </summary>
    /// <param name="data">The bytes read so far.</param>
    /// <param name="terminatorIndex">The byte offset of the terminator in <paramref name="data"/>.</param>
    /// <param name="unitSize">The size, in bytes, of a single code unit (1, 2 or 4).</param>
    /// <param name="isCr"><see langword="true"/> if the terminator is a CR.</param>
    /// <param name="lfUnit">The encoded LF code unit.</param>
    /// <returns>The number of bytes consumed by the string and its terminator.</returns>
    private int GetTerminatorEnd(ReadOnlySpan<byte> data, int terminatorIndex, int unitSize, bool isCr, ReadOnlySpan<byte> lfUnit)
    {
        int end = terminatorIndex + unitSize;

        if (!isCr)
            return end;

        Span<byte> nextUnit = stackalloc byte[4];

        // Only swallow an immediately following LF after a CR; the next line starts at any other unit.
        if (TryPeekUnit(data[end..], nextUnit[..unitSize]) && nextUnit[..unitSize].SequenceEqual(lfUnit))
            end += unitSize;

        return end;
    }

    /// <summary>
    /// Finds the first aligned terminator in a range of <paramref name="buffer"/>: a null code unit, or a CR or LF code unit when
    /// <paramref name="isLine"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="buffer">The buffer that holds the data to search.</param>
    /// <param name="offset">The offset of the data in <paramref name="buffer"/>.</param>
    /// <param name="count">The number of bytes to search; a multiple of <paramref name="unitSize"/>.</param>
    /// <param name="unitSize">The size, in bytes, of a single code unit (1, 2 or 4).</param>
    /// <param name="isLine"><see langword="true"/> to find a CR or LF code unit instead of a null code unit.</param>
    /// <param name="crUnit">The encoded CR code unit, or empty when <paramref name="isLine"/> is <see langword="false"/>.</param>
    /// <param name="lfUnit">The encoded LF code unit, or empty when <paramref name="isLine"/> is <see langword="false"/>.</param>
    /// <param name="isCr">When this method returns, <see langword="true"/> if the found terminator is a CR.</param>
    /// <returns>The byte offset of the terminator relative to <paramref name="offset"/>, or -1 if none was found.</returns>
    private int AnalyzeAndFindTerminator(byte[] buffer, int offset, int count, int unitSize, bool isLine, byte[] crUnit, byte[] lfUnit, out bool isCr)
    {
        if (isLine)
            return AnalyzeAndFindLineTerminator(buffer, offset, count, unitSize, crUnit, lfUnit, out isCr);

        isCr = false;
        return AnalyzeAndFindTerminator(buffer, offset, count, unitSize);
    }

    /// <summary>
    /// Finds the first aligned null code unit in a range of <paramref name="buffer"/>, reporting every code unit up to and including it
    /// to the <see cref="Analyzer"/> before it is examined.
    /// </summary>
    /// <remarks>
    /// The <see cref="Analyzer"/> can modify the bytes it is given, so each code unit is reported before it is compared or
    /// decoded, and the code units after the terminator, which the caller rewinds, are not reported. An analyzer that does not
    /// modify bytes (see <see cref="Analyzer.ModifiesBytes"/>) gets the same bytes in one call after the search.
    /// </remarks>
    /// <param name="buffer">The buffer that holds the data to search.</param>
    /// <param name="offset">The offset of the data in <paramref name="buffer"/>.</param>
    /// <param name="count">The number of bytes to search; a multiple of <paramref name="unitSize"/>.</param>
    /// <param name="unitSize">The size, in bytes, of a single code unit (1, 2 or 4).</param>
    /// <returns>The byte offset of the terminator relative to <paramref name="offset"/>, or -1 if none was found.</returns>
    private int AnalyzeAndFindTerminator(byte[] buffer, int offset, int count, int unitSize)
    {
        var analyzer = Analyzer;

        if (analyzer is null)
            return FindTerminator(new ReadOnlySpan<byte>(buffer, offset, count), unitSize);

        if (!analyzer.ModifiesBytes)
        {
            int index = FindTerminator(new ReadOnlySpan<byte>(buffer, offset, count), unitSize);
            ReportSearchedUnits(analyzer, buffer, offset, count, index, unitSize);
            return index;
        }

        int end = offset + count;

        for (int i = offset; i < end; i += unitSize)
        {
            analyzer.AnalyzeReadBytes(this, new Span<byte>(buffer, i, unitSize), unitSize);

            if (ReadCodeUnit(buffer, i, unitSize) == 0)
                return i - offset;
        }

        return -1;
    }

    /// <summary>
    /// Finds the first aligned CR or LF code unit in a range of <paramref name="buffer"/>, reporting every code unit up to and including it
    /// to the <see cref="Analyzer"/> before it is examined.
    /// </summary>
    /// <remarks>
    /// An analyzer that does not modify bytes (see <see cref="Analyzer.ModifiesBytes"/>) gets the same bytes in one call after the search.
    /// </remarks>
    /// <param name="buffer">The buffer that holds the data to search.</param>
    /// <param name="offset">The offset of the data in <paramref name="buffer"/>.</param>
    /// <param name="count">The number of bytes to search; a multiple of <paramref name="unitSize"/>.</param>
    /// <param name="unitSize">The size, in bytes, of a single code unit (1, 2 or 4).</param>
    /// <param name="crUnit">The encoded CR code unit.</param>
    /// <param name="lfUnit">The encoded LF code unit.</param>
    /// <param name="isCr">When this method returns, <see langword="true"/> if the found terminator is a CR.</param>
    /// <returns>The byte offset of the terminator relative to <paramref name="offset"/>, or -1 if none was found.</returns>
    private int AnalyzeAndFindLineTerminator(byte[] buffer, int offset, int count, int unitSize, byte[] crUnit, byte[] lfUnit, out bool isCr)
    {
        var analyzer = Analyzer;

        if (analyzer is null)
            return FindLineTerminator(new ReadOnlySpan<byte>(buffer, offset, count), unitSize, crUnit, lfUnit, out isCr);

        if (!analyzer.ModifiesBytes)
        {
            int index = FindLineTerminator(new ReadOnlySpan<byte>(buffer, offset, count), unitSize, crUnit, lfUnit, out isCr);
            ReportSearchedUnits(analyzer, buffer, offset, count, index, unitSize);
            return index;
        }

        uint cr = ReadCodeUnit(crUnit, 0, unitSize);
        uint lf = ReadCodeUnit(lfUnit, 0, unitSize);
        int end = offset + count;

        for (int i = offset; i < end; i += unitSize)
        {
            analyzer.AnalyzeReadBytes(this, new Span<byte>(buffer, i, unitSize), unitSize);

            uint value = ReadCodeUnit(buffer, i, unitSize);

            if (value == cr || value == lf)
            {
                isCr = value == cr;
                return i - offset;
            }
        }

        isCr = false;
        return -1;
    }

    /// <summary>
    /// Reports the code units of a searched range of <paramref name="buffer"/> up to and including the terminator, or all of them when
    /// there is none, to <paramref name="analyzer"/> in one call.
    /// </summary>
    /// <param name="analyzer">The analyzer, which does not modify the bytes it is given.</param>
    /// <param name="buffer">The buffer that holds the searched data.</param>
    /// <param name="offset">The offset of the searched data in <paramref name="buffer"/>.</param>
    /// <param name="count">The number of searched bytes.</param>
    /// <param name="terminatorIndex">The byte offset of the terminator relative to <paramref name="offset"/>, or -1 if none was found.</param>
    /// <param name="unitSize">The size, in bytes, of a single code unit (1, 2 or 4).</param>
    private void ReportSearchedUnits(Analyzer analyzer, byte[] buffer, int offset, int count, int terminatorIndex, int unitSize)
    {
        int reported = terminatorIndex >= 0 ? terminatorIndex + unitSize : count;

        if (reported != 0)
            analyzer.AnalyzeReadBytes(this, new Span<byte>(buffer, offset, reported), reported);
    }

    /// <summary>
    /// Reads the code unit at <paramref name="offset"/> of <paramref name="buffer"/> as a number that is equal for two code units exactly
    /// when their bytes are equal.
    /// </summary>
    /// <param name="buffer">The buffer that holds the code unit.</param>
    /// <param name="offset">The offset of the code unit in <paramref name="buffer"/>.</param>
    /// <param name="unitSize">The size, in bytes, of a single code unit (1, 2 or 4).</param>
    /// <returns>The code unit with its first byte as the lowest byte.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadCodeUnit(byte[] buffer, int offset, int unitSize)
    {
        // NOTE(VNC): Array indexing, because the indexer of the portable spans of .NET Framework and MemoryMarshal.Read are slower.
        return unitSize switch
        {
            1 => buffer[offset],
            2 => (uint)(buffer[offset] | (buffer[offset + 1] << 8)),
            _ => (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24))
        };
    }

    /// <summary>
    /// Reports the trailing bytes that end the data without forming a whole code unit to the <see cref="Analyzer"/>.
    /// </summary>
    /// <param name="remainder">The trailing bytes; empty when the data ended on a code unit boundary.</param>
    private void AnalyzeRemainder(Span<byte> remainder)
    {
        if (!remainder.IsEmpty)
            Analyzer?.AnalyzeReadBytes(this, remainder, remainder.Length);
    }

    /// <summary>
    /// Finds the byte offset of the first aligned zero code unit (null terminator) within <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The data to search; a trailing partial code unit is ignored.</param>
    /// <param name="unitSize">The size, in bytes, of a single code unit (1, 2 or 4).</param>
    /// <returns>The byte offset of the terminator, or -1 if none was found.</returns>
    private static int FindTerminator(ReadOnlySpan<byte> data, int unitSize)
    {
        if (unitSize == 1)
            return data.IndexOf((byte)0);

        int index = unitSize == 2
            ? MemoryMarshal.Cast<byte, char>(data).IndexOf('\0')
            : MemoryMarshal.Cast<byte, uint>(data).IndexOf(0u);

        return index < 0 ? -1 : index * unitSize;
    }

    /// <summary>
    /// Decodes the first <paramref name="count"/> bytes of <paramref name="buffer"/>, either with <see cref="Encoding"/> as a whole or,
    /// once the string is decoded in pieces, with the shared decoder into <see cref="_stringBuilder"/>.
    /// </summary>
    /// <param name="buffer">The bytes to decode.</param>
    /// <param name="count">The number of bytes to decode.</param>
    /// <param name="chars">The character buffer of the shared decoder, or <see langword="null"/> when the string is decoded as a whole.</param>
    /// <returns>The decoded string.</returns>
    private string Decode(byte[] buffer, int count, char[]? chars)
    {
        if (chars is null)
            return DecodeWhole(buffer, count);

        AppendCharacters(_decoder, buffer, count, chars, flush: true);
        return _stringBuilder.ToString();
    }

    /// <summary>
    /// Decodes the first <paramref name="count"/> bytes of <paramref name="bytes"/> with <see cref="Encoding"/> as a whole, like
    /// <see cref="Encoding.GetString(byte[], int, int)"/>.
    /// </summary>
    /// <param name="bytes">The bytes to decode.</param>
    /// <param name="count">The number of bytes to decode.</param>
    /// <returns>The decoded string.</returns>
    private string DecodeWhole(byte[] bytes, int count)
    {
#if NETSTANDARD2_0
        // NOTE(VNC): Encoding.GetString of .NET Framework counts the characters in a pass of its own before it decodes them, and is
        // many times slower for UTF-16; decoding into a character buffer and copying that into the string is faster.
        if (count <= c_MaxCharBytesSize)
        {
            char[] chars = _shortChars ??= new char[_maxCharsSize];
            return new string(chars, 0, _encoding.GetChars(bytes, 0, count, chars, 0));
        }

        char[] rented = ArrayPool<char>.Shared.Rent(_encoding.GetMaxCharCount(count));

        try
        {
            return new string(rented, 0, _encoding.GetChars(bytes, 0, count, rented, 0));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
#else
        return _encoding.GetString(bytes, 0, count);
#endif
    }

    /// <summary>
    /// Decodes the first <paramref name="count"/> bytes of <paramref name="bytes"/> with <paramref name="decoder"/> in pieces of
    /// <see cref="c_DecodePieceSize"/> bytes and appends the characters to <see cref="_stringBuilder"/>.
    /// </summary>
    /// <param name="decoder">The decoder, which keeps an incomplete sequence at the end for the next call unless <paramref name="flush"/> is set.</param>
    /// <param name="bytes">The bytes to decode.</param>
    /// <param name="count">The number of bytes to decode.</param>
    /// <param name="chars">A buffer for the characters of <see cref="c_DecodePieceSize"/> bytes.</param>
    /// <param name="flush"><see langword="true"/> to finish the string, so a trailing incomplete sequence is decoded as well.</param>
    private void AppendCharacters(Decoder decoder, byte[] bytes, int count, char[] chars, bool flush)
    {
        int offset = 0;

        do
        {
            int pieceSize = Math.Min(count - offset, c_DecodePieceSize);
            int charCount = decoder.GetChars(bytes, offset, pieceSize, chars, 0, flush && offset + pieceSize == count);

            _stringBuilder.Append(chars, 0, charCount);
            offset += pieceSize;
        }
        while (offset < count);
    }

    /// <summary>
    /// The decoder of <see cref="ReadString"/>, created on first use; like the decoder of <see cref="BinaryReader.ReadString"/>, it keeps
    /// an incomplete sequence at the end of a string for the next string.
    /// </summary>
    private Decoder? _stringDecoder;

    /// <inheritdoc/>
    /// <remarks>
    /// The bytes of the string are read with as few reads as possible and decoded at once, with the same characters as
    /// <see cref="BinaryReader.ReadString"/>; strings longer than <see cref="c_MaxSinglePassStringSize"/> bytes are decoded in pieces.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    /// <exception cref="EndOfStreamException">The data ends before the string; the available bytes are consumed.</exception>
    /// <exception cref="IOException">The length prefix is negative.</exception>
    /// <exception cref="FormatException">The length prefix is not a valid 7-bit encoded integer.</exception>
    public override string ReadString()
    {
        ThrowIfDisposed();

        // NOTE(VNC): The length prefix is read by the BinaryReader of the current runtime, as its ReadString does.
        int length = Read7BitEncodedInt();

        if (length < 0)
            ThrowInvalidStringLength(length);

        if (length == 0)
            return string.Empty;

        if (length <= c_MaxCharBytesSize)
            return ReadShortString(length);

        return length <= c_MaxSinglePassStringSize ? ReadLongString(length) : ReadStringInPieces(length);
    }

    /// <summary>
    /// Reads and decodes the bytes of a length-prefixed string of at most <see cref="c_MaxCharBytesSize"/> bytes.
    /// </summary>
    /// <param name="length">The number of bytes of the string.</param>
    /// <returns>The decoded string.</returns>
    /// <exception cref="EndOfStreamException">The data ends before the string.</exception>
    private string ReadShortString(int length)
    {
        byte[] bytes = _shortStringBytes ??= new byte[c_MaxCharBytesSize];
        var direct = _direct;
        int read = direct is null ? _readerStream.Read(bytes, 0, length) : direct.Read(bytes, 0, length);

        if (read == 0)
            ThrowEndOfStreamException();

#if !NETSTANDARD2_0
        // NOTE(VNC): Like BinaryReader.ReadString of .NET, a short string that arrives with the first read is decoded on its own,
        // without the decoder that carries an incomplete sequence from string to string.
        if (read == length)
            return _encoding.GetString(bytes, 0, length);
#endif

        if (read != length)
            _readerStream.FillReported(bytes, read, length);

        // NOTE(VNC): Decodes like BinaryReader.ReadString of .NET Framework decodes every string and .NET a string that needed
        // several reads: with the decoder that keeps an incomplete sequence for the next string, into a reused character buffer.
        var decoder = _stringDecoder ??= _encoding.GetDecoder();
        char[] chars = _shortChars ??= new char[_maxCharsSize];
        int charCount = decoder.GetChars(bytes, 0, length, chars, 0);

        return new string(chars, 0, charCount);
    }

    /// <summary>
    /// The bytes of a string of at most <see cref="c_MaxCharBytesSize"/> bytes that <see cref="ReadString"/> reads, created on first use.
    /// </summary>
    private byte[]? _shortStringBytes;

    /// <summary>
    /// A buffer for the characters of up to <see cref="c_MaxCharBytesSize"/> decoded bytes, created on first use.
    /// </summary>
    private char[]? _shortChars;

    /// <summary>
    /// Reads the bytes of a length-prefixed string of at most <see cref="c_MaxSinglePassStringSize"/> bytes into a pooled buffer and
    /// decodes them at once.
    /// </summary>
    /// <param name="length">The number of bytes of the string.</param>
    /// <returns>The decoded string.</returns>
    /// <exception cref="EndOfStreamException">The data ends before the string.</exception>
    private string ReadLongString(int length)
    {
        bool endReadsAtPage = _readerStream is ProcessMemoryReaderStream;

        // NOTE(VNC): The buffer grows with the bytes that arrive, so a corrupt length prefix cannot rent much more than the data holds;
        // one byte more than the known remaining bytes lets the read that finds the end of the data happen without growing the buffer.
        long available = CanSeek && !endReadsAtPage ? Math.Max(Length - Position, 0) + 1 : c_MaxStringReadSize;
        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(length, available));

        try
        {
            int filled = 0;

            while (filled < length)
            {
                if (filled == buffer.Length)
                    buffer = GrowPooledBuffer(buffer, filled, (int)Math.Min(2L * buffer.Length, length));

                int toRead = Math.Min(buffer.Length, length) - filled;

                // NOTE(VNC): ReadProcessMemory reads nothing when the range touches an unreadable page, so a read stops at the end
                // of the current page.
                if (endReadsAtPage)
                    toRead = Math.Min(toRead, c_MemoryPageSize - (int)(_readerStream.ActualPosition & (c_MemoryPageSize - 1)));

                var direct = _direct;
                int read = direct is null ? _readerStream.Read(buffer, filled, toRead) : direct.Read(buffer, filled, toRead);

                if (read == 0)
                    ThrowEndOfStreamException();

                filled += read;
            }

            return DecodeWithStringDecoder(buffer, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads the bytes of a length-prefixed string of more than <see cref="c_MaxSinglePassStringSize"/> bytes in pieces of
    /// <see cref="c_MaxStringReadSize"/> bytes and decodes them into <see cref="_stringBuilder"/>.
    /// </summary>
    /// <param name="length">The number of bytes of the string.</param>
    /// <returns>The decoded string.</returns>
    /// <exception cref="EndOfStreamException">The data ends before the string.</exception>
    private string ReadStringInPieces(int length)
    {
        bool endReadsAtPage = _readerStream is ProcessMemoryReaderStream;
        var decoder = _stringDecoder ??= _encoding.GetDecoder();

        byte[] buffer = ArrayPool<byte>.Shared.Rent(c_MaxStringReadSize);
        char[] chars = ArrayPool<char>.Shared.Rent(_encoding.GetMaxCharCount(c_DecodePieceSize));
        _stringBuilder.Clear();

        try
        {
            int remaining = length;

            while (remaining > 0)
            {
                int toRead = Math.Min(remaining, c_MaxStringReadSize);

                if (endReadsAtPage)
                    toRead = Math.Min(toRead, c_MemoryPageSize - (int)(_readerStream.ActualPosition & (c_MemoryPageSize - 1)));

                int read = _readerStream.Read(buffer, 0, toRead);

                if (read == 0)
                    ThrowEndOfStreamException();

                AppendCharacters(decoder, buffer, read, chars, flush: false);
                remaining -= read;
            }

            return _stringBuilder.ToString();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<char>.Shared.Return(chars);
        }
    }

    /// <summary>
    /// Decodes the first <paramref name="count"/> bytes of <paramref name="bytes"/> with <see cref="_stringDecoder"/>, which keeps an
    /// incomplete sequence at the end for the next string.
    /// </summary>
    /// <param name="bytes">The bytes to decode.</param>
    /// <param name="count">The number of bytes to decode.</param>
    /// <returns>The decoded string.</returns>
    private string DecodeWithStringDecoder(byte[] bytes, int count)
    {
        var decoder = _stringDecoder ??= _encoding.GetDecoder();

#if NETSTANDARD2_0
        // NOTE(VNC): Counting the characters first would decode the bytes twice, which is slow on .NET Framework.
        char[] chars = ArrayPool<char>.Shared.Rent(_encoding.GetMaxCharCount(count));

        try
        {
            int charCount = decoder.GetChars(bytes, 0, count, chars, 0);
            return new string(chars, 0, charCount);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
        }
#else
        int charCount = decoder.GetCharCount(bytes.AsSpan(0, count), flush: false);

        return string.Create(charCount, (decoder, bytes, count), static (chars, state) => state.decoder.GetChars(state.bytes.AsSpan(0, state.count), chars, flush: false));
#endif
    }

    /// <summary>
    /// Throws the <see cref="IOException"/> of <see cref="BinaryReader.ReadString"/> for a negative length prefix.
    /// </summary>
    /// <param name="length">The length prefix.</param>
    /// <exception cref="IOException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidStringLength(int length)
    {
        throw new IOException(string.Format(CultureInfo.CurrentCulture, "BinaryReader encountered an invalid string length of {0} characters.", length));
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadAlignment(byte alignment)
    {
        if (alignment <= 0)
            ThrowAlignmentNotPositive(alignment);

        long position = Position;
        BinaryUtils.ApplyAlignment(ref position, alignment);
        Position = position;
    }

    /// <summary>
    /// Throws the <see cref="ArgumentOutOfRangeException"/> of <see cref="ReadAlignment"/> for an alignment of zero.
    /// </summary>
    /// <remarks>
    /// The message lives here because .NET Framework never inlines a method that loads a string literal into another assembly.
    /// </remarks>
    /// <param name="alignment">The alignment.</param>
    /// <exception cref="ArgumentOutOfRangeException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowAlignmentNotPositive(byte alignment)
    {
        throw new ArgumentOutOfRangeException(nameof(alignment), "Alignment must be greater than 0.");
    }

    /// <inheritdoc/>
    public byte[] ReadUntilEnd(int maxBufferSize = 16 * 1024)
    {
        if (maxBufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBufferSize), "Max buffer size must be greater than 0.");

        long remaining = Length - Position;

        if (remaining is > 0 and <= int.MaxValue)
        {
            int exactSize = (int)remaining;
            byte[] result = new byte[exactSize];

            ReadExactlyInto(result, 0, exactSize);
            return result;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(maxBufferSize);
        try
        {
            using var ms = new MemoryStream();

            int read;
            while ((read = Read(buffer, 0, maxBufferSize)) > 0)
            {
                ms.Write(buffer, 0, read);
            }

            return ms.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}