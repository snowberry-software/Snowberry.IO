using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snowberry.IO.Reader.Interfaces;

namespace Snowberry.IO.Extensions;

public static class ReaderExtensions
{
    /// <summary>
    /// Returns a 64-bit signed integer converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 64-bit signed integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadLongAt(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToLong(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a 64-bit unsigned integer converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 64-bit unsigned integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadULongAt(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToULong(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a 32-bit unsigned integer converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 32-bit unsigned integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToUInt32(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a 16-bit unsigned integer converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 16-bit unsigned integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToUInt16(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a 32-bit signed integer converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 32-bit signed integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToInt32(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a 16-bit signed integer converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The 16-bit signed integer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ReadInt16At(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToInt16(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a <see cref="Guid"/> converted from 16 bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The <see cref="Guid"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ReadGuidAt(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToGuid(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a single-precision floating point number converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The single-precision floating point number.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ReadFloatAt(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToFloat(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a double-precision floating point number converted from four bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="endian">The byte order to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The double-precision floating point number.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ReadDoubleAt(this IEndianReader reader, EndianType endian = EndianType.LITTLE, int offset = 0)
    {
        return BinaryEndianConverter.ToDouble(reader.Buffer, offset, endian);
    }

    /// <summary>
    /// Returns a <see cref="Guid"/> converted from 20 bytes at a specified <paramref name="offset"/> from the reader's buffer.
    /// </summary>
    /// <param name="reader">The reader to use.</param>
    /// <param name="offset">The starting position within the reader's buffer.</param>
    /// <returns>The <see cref="Sha1"/>.</returns>
    public static Sha1 ReadSha1At(this IEndianReader reader, int offset = 0)
    {
        return new(reader.Buffer.AsSpan().Slice(offset, Sha1.StructSize));
    }

    /// <summary>
    /// Scans for signatures and returns the position for each one.
    /// </summary>
    /// <remarks>The signature pattern has to follow this pattern and start with a known byte value: 44 ?? 44 ?? 44 44 ??</remarks>
    /// <param name="reader">The reader to use.</param>
    /// <param name="pattern">The signature pattern to scan.</param>
    /// <param name="maxCount">The maximum amount of offsets.</param>
    /// <param name="maxAddress">The maximum position that won't be exceeded.</param>
    /// <returns>The offsets/addresses for the found signatures.</returns>
    public static unsafe IList<long> ScanSignatures(this IEndianReader reader, string pattern, int maxCount, long maxAddress)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(pattern);

        var offsets = new List<long>();

        pattern = pattern.Replace(" ", "").Trim();

        if (pattern.StartsWith("?"))
            throw new FormatException("Pattern must start with a known byte!");

        // Parse current pattern.
        var patternData = new PatternMeta[pattern.Length / 2];
        for (int i = 0; i < patternData.Length; i++)
        {
            string hex = pattern.Substring(i * 2, 2);

            patternData[i] = new()
            {
                IsWildcard = hex == "??",
                Value = hex != "??" ? byte.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : (byte)0
            };
        }

        // 1MB
        const int bufferSize = 1_048_576;

        long memoryStartPos;
        byte[]? bufferData;

        byte* bufferManagedAddress = (byte*)IntPtr.Zero;
        byte* bufferManagedOffset = (byte*)IntPtr.Zero;
        byte* maxManagedBufferOffset = (byte*)IntPtr.Zero;

        // Initialize buffer
        if (!RefillBuffer())
            return offsets;

        while (bufferData != null)
        {
            // Check whether the current offset starts with the first pattern data value.
            if (*bufferManagedOffset != patternData[0].Value)
            {
                bufferManagedOffset++;
            }
            else
            {
                byte* tempArrayOffset = bufferManagedOffset;
                bool isValid = true;

                // Compare the rest of the pattern with the current buffer data.
                for (int i = 0; i < patternData.Length; i++)
                {
                    if (!patternData[i].IsWildcard && *tempArrayOffset != patternData[i].Value)
                    {
                        isValid = false;
                        break;
                    }

                    tempArrayOffset++;
                }

                if (isValid)
                {
                    // NOTE(VNC):
                    //
                    // Because we are comparing addresses and also increase them,
                    // we will take the managed buffer address and subtract it from the current offset.
                    // Through this we will get the offset in the buffer where the pattern data ends.
                    // To get the beginning we just subtract the pattern length from the offset.
                    //
                    // Because we assume that we start reading at the module base address,
                    // we just have to add the final start position of the pattern data to the beginning of the current memory position.
                    //
                    long patternDataEnd = tempArrayOffset - bufferManagedAddress;
                    long bufferStartPosition = patternDataEnd - patternData.Length;
                    long offset = memoryStartPos + bufferStartPosition;

                    offsets.Add(offset);
                    bufferManagedOffset += patternData.Length;

                    if (offsets.Count == maxCount)
                        return offsets;
                }
                else
                {
                    bufferManagedOffset++;
                }
            }

            // Check if we are at the end of the array buffer and if so, refill the buffer with new data.
            if (bufferManagedOffset == maxManagedBufferOffset)
                RefillBuffer();
        }

        // Returns whether the buffer data is valid and correctly initialized.
        bool RefillBuffer()
        {
            memoryStartPos = reader.Position;

            if (memoryStartPos > maxAddress)
            {
                bufferData = null;
                return false;
            }

            bufferData = reader.ReadBytes(bufferSize);

            if (bufferData == null)
                return false;

            bufferManagedAddress = (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(bufferData, 0);
            bufferManagedOffset = bufferManagedAddress;
            maxManagedBufferOffset = bufferManagedOffset + bufferSize;
            return true;
        }

        return offsets;
    }

    private struct PatternMeta
    {
        /// <summary>
        /// Is unknown byte.
        /// </summary>
        public bool IsWildcard;

        /// <summary>
        /// Known byte value.
        /// </summary>
        public byte Value;
    }

}
