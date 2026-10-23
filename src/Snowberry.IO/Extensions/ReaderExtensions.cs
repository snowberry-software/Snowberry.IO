using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Extensions;

/// <summary>
/// Provides extension methods for <see cref="IEndianReader"/>, such as reading values directly from its
/// internal <see cref="IEndianReader.Buffer"/> at a given offset, and scanning for byte signatures.
/// </summary>
public static class ReaderExtensions
{
#if NETSTANDARD2_0
    /// <summary>
    /// The size of the buffer of <see cref="ScanSignatures"/>: 1 MiB, the largest array that <see cref="ArrayPool{T}.Shared"/> of
    /// .NET Framework pools; refills cost more there, so fewer and larger ones are faster. Each refill reads up to this size minus
    /// the bytes carried over from the previous refill.
    /// </summary>
    private const int c_ScanBufferSize = 1024 * 1024;
#else
    /// <summary>
    /// The size of the buffer of <see cref="ScanSignatures"/>: 256 KiB, so that a refill is still in the CPU's L2 cache when it is
    /// searched; a 64 MiB scan with 1 MiB refills took up to twice as long on .NET 10. Each refill reads up to this size minus the
    /// bytes carried over from the previous refill.
    /// </summary>
    private const int c_ScanBufferSize = 256 * 1024;
#endif

    /// <summary>
    /// The size of the buffer of <see cref="ScanSignatures"/> over the memory of a process (<see cref="Win32ProcessReader"/>): 1 MiB,
    /// because every refill there is a <c>ReadProcessMemory</c> call, which costs more than searching a larger refill.
    /// </summary>
    private const int c_ProcessScanBufferSize = 1024 * 1024;

    /// <summary>
    /// The minimum number of new bytes that a refill of <see cref="ScanSignatures"/> reads, for patterns that leave less of
    /// <see cref="c_ScanBufferSize"/>.
    /// </summary>
    private const int c_MinScanChunkSize = 64 * 1024;

#if NETSTANDARD2_0
    /// <summary>
    /// The minimum number of bytes that a refill of <see cref="ScanSignatures"/> can hold before the scan samples the data to choose the
    /// byte it searches for.
    /// </summary>
    private const int c_MinSampledScanLength = 64 * 1024;

    /// <summary>
    /// The number of bytes at the start of the scanned data whose byte frequencies choose the byte that <see cref="ScanSignatures"/>
    /// searches for.
    /// </summary>
    private const int c_ScanSampleLength = 4096;
#endif

    /// <summary>
    /// Returns a 64-bit signed integer converted from eight bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 64-bit signed integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadInt64At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToInt64(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a 64-bit unsigned integer converted from eight bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 64-bit unsigned integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadUInt64At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToUInt64(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a 32-bit unsigned integer converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 32-bit unsigned integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToUInt32(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a 16-bit unsigned integer converted from two bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 16-bit unsigned integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToUInt16(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a 32-bit signed integer converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 32-bit signed integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToInt32(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a 16-bit signed integer converted from two bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 16-bit signed integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ReadInt16At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToInt16(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a <see cref="Guid"/> converted from 16 bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The <see cref="Guid"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ReadGuidAt(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToGuid(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a single-precision floating point number converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The single-precision floating point number.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ReadSingleAt(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToFloat(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a double-precision floating point number converted from eight bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The double-precision floating point number.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ReadDoubleAt(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToDouble(reader.Buffer.Span, offset, endian);
    }

    /// <summary>
    /// Returns a <see cref="Sha1"/> hash converted from 20 bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <remarks>Reads from <see cref="IEndianReader.Buffer"/>; the buffer must already contain the relevant data, for example via <see cref="IEndianReader.ReadInInternalBuffer"/>.</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The <see cref="Sha1"/>.</returns>
    public static Sha1 ReadSha1At(this IEndianReader reader, int offset = 0)
    {
        return new(reader.Buffer.Span.Slice(offset, Sha1.c_StructSize));
    }

    /// <summary>
    /// Scans for signatures and returns the position for each one.
    /// </summary>
    /// <remarks>
    /// The signature pattern consists of hex bytes and <c>??</c> wildcards, for example <c>44 ?? 44 ?? 44 44 ??</c>, and must
    /// contain at least one known byte. The scan searches for the longest run of known bytes and compares the other known
    /// bytes of each candidate; the .NET Standard 2.0 build searches a scan of at least 64 KiB for the known byte that is the
    /// rarest in the first 4 KiB of the data instead, when it is clearly rarer there than the first byte of that run.
    /// <para/>
    /// Matching bytes may span across internal buffer refills, and matches do not overlap; the search stops once
    /// <paramref name="maxCount"/> offsets have been found, once <paramref name="maxAddress"/> has been reached, or once the
    /// reader has no more data. The search starts at the current <see cref="IEndianReader.Position"/>, advances it, and reads
    /// no byte past the last pattern that could start at <paramref name="maxAddress"/>. Over the memory of a process
    /// (<see cref="Win32ProcessReader"/>), the pages of allocated memory that cannot be read are skipped, and no match spans them;
    /// address space that is not allocated ends the data.
    /// </remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="pattern">The signature pattern to scan.</param>
    /// <param name="maxCount">The maximum amount of offsets.</param>
    /// <param name="maxAddress">The maximum position that won't be exceeded.</param>
    /// <returns>The offsets/addresses for the found signatures.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reader"/> or <paramref name="pattern"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="pattern"/> is empty, has an odd number of hex characters, consists of wildcards (<c>??</c>) only,
    /// or contains an invalid hex byte.
    /// </exception>
    public static IList<long> ScanSignatures(this IEndianReader reader, string pattern, int maxCount, long maxAddress)
    {
        _ = reader ?? throw new ArgumentNullException(nameof(reader));
        _ = pattern ?? throw new ArgumentNullException(nameof(pattern));

        var offsets = new List<long>();

        pattern = pattern.Replace(" ", "").Trim();

        if (pattern.Length == 0)
            throw new FormatException("Pattern must not be empty.");

        if (pattern.Length % 2 != 0)
            throw new FormatException("Pattern must consist of an even number of hex characters.");

        if (!SignaturePattern.HasKnownByte(pattern))
            throw new FormatException("Pattern must contain at least one known byte!");

        if (maxCount <= 0)
            return offsets;

        var signature = SignaturePattern.Parse(pattern);
        int patternLength = signature.Length;

        // Each refill holds new data plus the last (patternLength - 1) bytes of the previous chunk, so that matches spanning a chunk
        // boundary are not missed. The buffer is no larger than the bytes the scan can reach, and the pool may return a larger
        // array; only bufferSize bytes of it are used.
        long reachable = maxAddress - reader.Position;

        if (reachable < 0)
            return offsets;

        int scanBufferSize = reader is Win32ProcessReader ? c_ProcessScanBufferSize : c_ScanBufferSize;
        long scanLength = Math.Min(Math.Max(scanBufferSize - patternLength, c_MinScanChunkSize), reachable);

        if (TryGetRemainingLength(reader, out long remaining))
            scanLength = Math.Min(scanLength, Math.Max(remaining, 0));

        int bufferSize = (int)scanLength + patternLength;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            // Process memory goes on after pages that cannot be read; no match spans them, so the scan starts over after them.
            do
            {
                ScanSignaturesCore(reader, signature, buffer, bufferSize, maxCount, maxAddress, offsets);
            }
            while (offsets.Count < maxCount && reader is BaseEndianReader baseReader && baseReader.SkipUnreadableData(maxAddress));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return offsets;
    }

    private static void ScanSignaturesCore(IEndianReader reader, SignaturePattern signature, byte[] buffer, int bufferSize, int maxCount, long maxAddress, List<long> offsets)
    {
        int patternLength = signature.Length;
        byte[] anchor = signature.Anchor;
        int anchorOffset = signature.AnchorOffset;
        int carry = 0;

        // The first index in the buffer at which a match may start: the bytes carried over from the previous refill can still
        // belong to its last match, and matches do not overlap.
        int firstStart = 0;

#if NETSTANDARD2_0
        bool sampleData = bufferSize - patternLength >= c_MinSampledScanLength;
#endif

        while (true)
        {
            long chunkStartPosition = reader.Position - carry;

            if (chunkStartPosition > maxAddress)
                return;

            // The last match can start at maxAddress, so no byte after maxAddress + patternLength - 1 is needed.
            long bytesUpToLastMatch = maxAddress - chunkStartPosition;
            int fillEnd = bytesUpToLastMatch < bufferSize - patternLength
                ? (int)bytesUpToLastMatch + patternLength
                : bufferSize;

            int newBytes = FillRemaining(reader, buffer, carry, fillEnd);
            int validLength = carry + newBytes;

            if (validLength < patternLength)
                return;

#if NETSTANDARD2_0
            // NOTE(VNC): IndexOf of a byte sequence in System.Memory searches for the first byte of the sequence and compares the rest
            // at every hit, so a frequent first byte, such as 0x48 in x64 code, makes it slow. A long scan therefore searches for the
            // known byte that is the rarest in the first bytes of the data and compares the other known bytes of each hit.
            if (sampleData)
            {
                sampleData = false;
                signature = signature.WithRarestKnownByteAsAnchor(buffer, Math.Min(validLength, c_ScanSampleLength));
                anchor = signature.Anchor;
                anchorOffset = signature.AnchorOffset;
            }
#endif

            int searchEnd = validLength - patternLength;
            int i = firstStart;
            int lastMatchEnd = firstStart;

            while (i <= searchEnd)
            {
                // The anchor of a match that starts at i lies at i + anchorOffset; the window ends with the anchor of a match
                // that starts at searchEnd.
                int windowLength = searchEnd - i + anchor.Length;
                var window = buffer.AsSpan(i + anchorOffset, windowLength);
                int matchIndex = anchor.Length == 1 ? window.IndexOf(anchor[0]) : window.IndexOf(anchor);

                if (matchIndex < 0)
                    break;

                i += matchIndex;

                if (signature.MatchesAt(buffer, i))
                {
                    long matchAddress = chunkStartPosition + i;

                    if (matchAddress <= maxAddress)
                    {
                        offsets.Add(matchAddress);

                        if (offsets.Count == maxCount)
                            return;
                    }

                    // Matches do not overlap, so the search continues after this one.
                    i += patternLength;
                    lastMatchEnd = i;
                }
                else
                {
                    i++;
                }
            }

            // A short refill means the reader ran out of data or the next chunk would start after maxAddress.
            if (validLength < bufferSize)
                return;

            int overlap = patternLength - 1;

            if (overlap > 0)
                Buffer.BlockCopy(buffer, validLength - overlap, buffer, 0, overlap);

            carry = overlap;
            firstStart = Math.Max(0, lastMatchEnd - (validLength - overlap));
        }
    }

    /// <summary>
    /// Gets the number of bytes after the current position when <paramref name="reader"/> knows the length of its data.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="remaining">When this method returns <see langword="true"/>, the number of bytes after the current position.</param>
    /// <returns><see langword="true"/> if the length of the data is known; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetRemainingLength(IEndianReader reader, out long remaining)
    {
        // NOTE(VNC): Without a region view, the length of process memory is unknown (Length is 0), and a non-seekable stream has none.
        if (reader.IsRegionViewEnabled || reader is EndianStreamReader { BaseStream.CanSeek: true })
        {
            remaining = reader.Length - reader.Position;
            return true;
        }

        remaining = 0;
        return false;
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> from index <paramref name="start"/> up to (but excluding) <paramref name="end"/>
    /// by issuing repeated <see cref="IEndianReader.Read(byte[], int, int)"/> calls, stopping once the buffer is full
    /// or once a read returns zero bytes (end of stream).
    /// </summary>
    /// <param name="reader">The reader to read from.</param>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="start">The zero-based index to start filling at.</param>
    /// <param name="end">The exclusive end index to fill up to.</param>
    /// <returns>The number of bytes actually written to <paramref name="buffer"/>, starting at <paramref name="start"/>.</returns>
    private static int FillRemaining(IEndianReader reader, byte[] buffer, int start, int end)
    {
        int total = 0;

        while (start + total < end)
        {
            int read = reader.Read(buffer, start + total, end - start - total);

            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    /// <summary>
    /// A parsed signature pattern of <see cref="ScanSignatures"/>.
    /// </summary>
    /// <param name="length">The number of bytes of the pattern, including the wildcards.</param>
    /// <param name="anchor">The longest run of known bytes of the pattern.</param>
    /// <param name="anchorOffset">The offset of <paramref name="anchor"/> in the pattern.</param>
    /// <param name="checkOffsets">The offsets of the known bytes outside <paramref name="anchor"/>.</param>
    /// <param name="checkValues">The values of the known bytes at <paramref name="checkOffsets"/>.</param>
    private sealed class SignaturePattern(int length, byte[] anchor, int anchorOffset, int[] checkOffsets, byte[] checkValues)
    {
        /// <summary>
        /// Returns whether <paramref name="pattern"/> contains a byte that is not a wildcard.
        /// </summary>
        /// <param name="pattern">The pattern without spaces, with an even number of characters.</param>
        /// <returns><see langword="true"/> if the pattern contains a known byte; otherwise, <see langword="false"/>.</returns>
        public static bool HasKnownByte(string pattern)
        {
            for (int i = 0; i < pattern.Length / 2; i++)
            {
                if (!IsWildcard(pattern, i))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Parses <paramref name="pattern"/>.
        /// </summary>
        /// <param name="pattern">The pattern without spaces, with an even number of characters and at least one known byte.</param>
        /// <returns>The parsed pattern.</returns>
        /// <exception cref="FormatException">The pattern contains an invalid hex byte.</exception>
        public static SignaturePattern Parse(string pattern)
        {
            int length = pattern.Length / 2;

            // NOTE(VNC): A longer run of known bytes has fewer hits, so the longest one is searched with a single vectorized
            // IndexOf, and the other known bytes are only compared for its hits.
            int anchorOffset = 0;
            int anchorLength = 0;
            int knownCount = 0;

            for (int start = 0; start < length;)
            {
                if (IsWildcard(pattern, start))
                {
                    start++;
                    continue;
                }

                int end = start + 1;

                while (end < length && !IsWildcard(pattern, end))
                    end++;

                if (end - start > anchorLength)
                {
                    anchorOffset = start;
                    anchorLength = end - start;
                }

                knownCount += end - start;
                start = end;
            }

            byte[] anchor = new byte[anchorLength];
            int[] checkOffsets = new int[knownCount - anchorLength];
            byte[] checkValues = new byte[checkOffsets.Length];
            int checkCount = 0;

            // The bytes are parsed in order, so the first invalid hex byte is the one that throws.
            for (int i = 0; i < length; i++)
            {
                if (IsWildcard(pattern, i))
                    continue;

                byte value = ParseHexByte(pattern, i);

                if (i >= anchorOffset && i < anchorOffset + anchorLength)
                {
                    anchor[i - anchorOffset] = value;
                }
                else
                {
                    checkOffsets[checkCount] = i;
                    checkValues[checkCount] = value;
                    checkCount++;
                }
            }

            return new SignaturePattern(length, anchor, anchorOffset, checkOffsets, checkValues);
        }

        /// <summary>
        /// Returns whether the byte at <paramref name="index"/> of <paramref name="pattern"/> is a wildcard (<c>??</c>).
        /// </summary>
        /// <param name="pattern">The pattern without spaces.</param>
        /// <param name="index">The index of the byte, which starts at character <c>2 * index</c>.</param>
        /// <returns><see langword="true"/> if the byte is a wildcard; otherwise, <see langword="false"/>.</returns>
        private static bool IsWildcard(string pattern, int index)
        {
            return pattern[index * 2] == '?' && pattern[(index * 2) + 1] == '?';
        }

        /// <summary>
        /// Parses the hex byte at <paramref name="index"/> of <paramref name="pattern"/>.
        /// </summary>
        /// <param name="pattern">The pattern without spaces.</param>
        /// <param name="index">The index of the byte, which starts at character <c>2 * index</c>.</param>
        /// <returns>The value of the byte.</returns>
        /// <exception cref="FormatException">The two characters are not a hex byte.</exception>
        private static byte ParseHexByte(string pattern, int index)
        {
#if NETSTANDARD2_0
            return byte.Parse(pattern.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#else
            return byte.Parse(pattern.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#endif
        }

        /// <summary>
        /// Returns whether the known bytes outside <see cref="Anchor"/> match the bytes of a candidate in <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">The scanned bytes.</param>
        /// <param name="start">The index in <paramref name="buffer"/> at which the candidate starts.</param>
        /// <returns><see langword="true"/> if the candidate matches the pattern; otherwise, <see langword="false"/>.</returns>
        public bool MatchesAt(byte[] buffer, int start)
        {
            for (int i = 0; i < checkOffsets.Length; i++)
            {
                if (buffer[start + checkOffsets[i]] != checkValues[i])
                    return false;
            }

            return true;
        }

#if NETSTANDARD2_0
        /// <summary>
        /// Returns a pattern that searches for the known byte that occurs the fewest times in <paramref name="sample"/>, the first one of
        /// equally rare bytes, and compares all other known bytes of each hit, when that byte occurs less than half as often as the first
        /// byte of <see cref="Anchor"/>, whose every occurrence stops the search for the anchor.
        /// </summary>
        /// <param name="sample">The bytes whose frequencies decide.</param>
        /// <param name="sampleLength">The number of bytes at the start of <paramref name="sample"/> to count.</param>
        /// <returns>The pattern with a single-byte <see cref="Anchor"/>, or this pattern.</returns>
        public unsafe SignaturePattern WithRarestKnownByteAsAnchor(byte[] sample, int sampleLength)
        {
            int knownCount = Anchor.Length + checkOffsets.Length;

            if (knownCount == 1)
                return this;

            int* counts = stackalloc int[256];

            for (int i = 0; i < 256; i++)
                counts[i] = 0;

            for (int i = 0; i < sampleLength; i++)
                counts[sample[i]]++;

            int[] offsets = new int[knownCount];
            byte[] values = new byte[knownCount];

            for (int i = 0; i < Anchor.Length; i++)
            {
                offsets[i] = AnchorOffset + i;
                values[i] = Anchor[i];
            }

            checkOffsets.CopyTo(offsets, Anchor.Length);
            checkValues.CopyTo(values, Anchor.Length);

            int rarest = 0;

            for (int i = 1; i < knownCount; i++)
            {
                if (counts[values[i]] < counts[values[rarest]])
                    rarest = i;
            }

            // NOTE(VNC): Searching for a single byte costs more per hit than the search for the anchor in System.Memory, so a byte that
            // is not clearly rarer than the first byte of the anchor, as in uniformly distributed data, does not pay off.
            if (counts[values[rarest]] * 2 >= counts[values[0]])
                return this;

            int[] otherOffsets = new int[knownCount - 1];
            byte[] otherValues = new byte[knownCount - 1];
            int other = 0;

            for (int i = 0; i < knownCount; i++)
            {
                if (i == rarest)
                    continue;

                otherOffsets[other] = offsets[i];
                otherValues[other] = values[i];
                other++;
            }

            return new SignaturePattern(Length, [values[rarest]], offsets[rarest], otherOffsets, otherValues);
        }
#endif

        /// <summary>
        /// Gets the longest run of known bytes of the pattern, the first one of equally long runs, which the scan searches for.
        /// </summary>
        public byte[] Anchor { get; } = anchor;

        /// <summary>
        /// Gets the offset of <see cref="Anchor"/> in the pattern.
        /// </summary>
        public int AnchorOffset { get; } = anchorOffset;

        /// <summary>
        /// Gets the number of bytes of the pattern, including the wildcards.
        /// </summary>
        public int Length { get; } = length;
    }
}