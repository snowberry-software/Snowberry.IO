using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Snowberry.IO.Common;

/// <summary>
/// Provides methods to convert binary data between different endian formats.
/// </summary>
public static partial class BinaryEndianConverter
{
    /// <summary>
    /// Converts the first 8 bytes of <paramref name="data"/> to a double-precision floating-point number using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data. Must be at least 8 bytes long.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted double-precision floating-point number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="data"/> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToDouble(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NET5_0_OR_GREATER
        return endian == EndianType.LITTLE
            ? BinaryPrimitives.ReadDoubleLittleEndian(data)
            : BinaryPrimitives.ReadDoubleBigEndian(data);
#else
        long temp = ToInt64(data, endian);
        return BitConverter.Int64BitsToDouble(temp);
#endif
    }

    /// <summary>
    /// Converts the first 4 bytes of <paramref name="data"/> to a single-precision floating-point number using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data. Must be at least 4 bytes long.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted single-precision floating-point number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="data"/> is shorter than 4 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToFloat(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NET5_0_OR_GREATER
        return endian == EndianType.LITTLE
            ? BinaryPrimitives.ReadSingleLittleEndian(data)
            : BinaryPrimitives.ReadSingleBigEndian(data);
#else
        return Int32BitsToSingle(ToInt32(data, endian));
#endif
    }

    /// <summary>
    /// Converts the first 2 bytes of <paramref name="data"/> to a 16-bit signed integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data. Must be at least 2 bytes long.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 16-bit signed integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="data"/> is shorter than 2 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ToInt16(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(data.Length, sizeof(short));
        short value = ReadAt<short>(data, 0);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
#else
        return endian == EndianType.LITTLE
            ? BinaryPrimitives.ReadInt16LittleEndian(data)
            : BinaryPrimitives.ReadInt16BigEndian(data);
#endif
    }

    /// <summary>
    /// Converts the first 4 bytes of <paramref name="data"/> to a 32-bit signed integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data. Must be at least 4 bytes long.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 32-bit signed integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="data"/> is shorter than 4 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(data.Length, sizeof(int));
        int value = ReadAt<int>(data, 0);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
#else
        return endian == EndianType.LITTLE
            ? BinaryPrimitives.ReadInt32LittleEndian(data)
            : BinaryPrimitives.ReadInt32BigEndian(data);
#endif
    }

    /// <summary>
    /// Converts the first 8 bytes of <paramref name="data"/> to a 64-bit signed integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data. Must be at least 8 bytes long.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 64-bit signed integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="data"/> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToInt64(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(data.Length, sizeof(long));
        long value = ReadAt<long>(data, 0);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
#else
        return endian == EndianType.LITTLE
            ? BinaryPrimitives.ReadInt64LittleEndian(data)
            : BinaryPrimitives.ReadInt64BigEndian(data);
#endif
    }

    /// <summary>
    /// Converts the first 2 bytes of <paramref name="data"/> to a 16-bit unsigned integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data. Must be at least 2 bytes long.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 16-bit unsigned integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="data"/> is shorter than 2 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ToUInt16(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(data.Length, sizeof(ushort));
        ushort value = ReadAt<ushort>(data, 0);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
#else
        return endian == EndianType.LITTLE
            ? BinaryPrimitives.ReadUInt16LittleEndian(data)
            : BinaryPrimitives.ReadUInt16BigEndian(data);
#endif
    }

    /// <summary>
    /// Converts the first 4 bytes of <paramref name="data"/> to a 32-bit unsigned integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data. Must be at least 4 bytes long.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 32-bit unsigned integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="data"/> is shorter than 4 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUInt32(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(data.Length, sizeof(uint));
        uint value = ReadAt<uint>(data, 0);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
#else
        return endian == EndianType.LITTLE
            ? BinaryPrimitives.ReadUInt32LittleEndian(data)
            : BinaryPrimitives.ReadUInt32BigEndian(data);
#endif
    }

    /// <summary>
    /// Converts the first 8 bytes of <paramref name="data"/> to a 64-bit unsigned integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data. Must be at least 8 bytes long.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 64-bit unsigned integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="data"/> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToUInt64(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(data.Length, sizeof(ulong));
        ulong value = ReadAt<ulong>(data, 0);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
#else
        return endian == EndianType.LITTLE
            ? BinaryPrimitives.ReadUInt64LittleEndian(data)
            : BinaryPrimitives.ReadUInt64BigEndian(data);
#endif
    }

#if NETSTANDARD2_0
    /// <summary>
    /// Ensures that a source of <paramref name="length"/> bytes holds at least <paramref name="requiredByteCount"/> bytes.
    /// </summary>
    /// <remarks>
    /// On .NET Framework the portable <see cref="BinaryPrimitives"/> read through <c>MemoryMarshal.GetReference</c>, which is not inlined
    /// and copies the span, so the netstandard2.0 build reads like the offset overloads and only keeps the exception of <see cref="BinaryPrimitives"/>.
    /// </remarks>
    /// <param name="length">The length, in bytes, of the source buffer.</param>
    /// <param name="requiredByteCount">The number of bytes required.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is less than <paramref name="requiredByteCount"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureLength(int length, int requiredByteCount)
    {
        if (length < requiredByteCount)
            ThrowLengthTooShort(length);
    }

    /// <summary>
    /// Throws the exception that <see cref="BinaryPrimitives"/> throws for a source shorter than the value being read.
    /// </summary>
    /// <param name="length">The length, in bytes, of the source buffer.</param>
    /// <exception cref="ArgumentOutOfRangeException">Always thrown.</exception>
    private static void ThrowLengthTooShort(int length)
    {
        throw new ArgumentOutOfRangeException(nameof(length));
    }
#endif
}