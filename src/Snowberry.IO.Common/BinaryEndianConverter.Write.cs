using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
#if !NETSTANDARD2_0
using System.Runtime.InteropServices;
#endif

namespace Snowberry.IO.Common;

public static partial class BinaryEndianConverter
{
    /// <summary>
    /// Writes <paramref name="value"/> to the first 8 bytes of <paramref name="destination"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination. Must be at least 8 bytes long.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteDouble(Span<byte> destination, double value, EndianType endian)
    {
#if NET5_0_OR_GREATER
        if (endian == EndianType.LITTLE)
            BinaryPrimitives.WriteDoubleLittleEndian(destination, value);
        else
            BinaryPrimitives.WriteDoubleBigEndian(destination, value);
#else
        WriteInt64(destination, BitConverter.DoubleToInt64Bits(value), endian);
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 8 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 8 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteDouble(Span<byte> destination, int offset, double value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, sizeof(double), ValueKind.Double);
        long bits = BitConverter.DoubleToInt64Bits(value);
        WriteAt(destination, offset, endian == HostEndian ? bits : BinaryPrimitives.ReverseEndianness(bits));
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the first 4 bytes of <paramref name="destination"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination. Must be at least 4 bytes long.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter than 4 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteFloat(Span<byte> destination, float value, EndianType endian)
    {
#if NET5_0_OR_GREATER
        if (endian == EndianType.LITTLE)
            BinaryPrimitives.WriteSingleLittleEndian(destination, value);
        else
            BinaryPrimitives.WriteSingleBigEndian(destination, value);
#else
        WriteInt32(destination, SingleToInt32Bits(value), endian);
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 4 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 4 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteFloat(Span<byte> destination, int offset, float value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, sizeof(float), ValueKind.Single);
        int bits = SingleToInt32Bits(value);
        WriteAt(destination, offset, endian == HostEndian ? bits : BinaryPrimitives.ReverseEndianness(bits));
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 16 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <remarks>
    /// When <paramref name="endian"/> is <see cref="EndianType.BIG"/>, the first three components (<c>Data1</c>, <c>Data2</c> and <c>Data3</c>)
    /// are written in big-endian order while the remaining 8 bytes (<c>Data4</c>) are written as-is, matching RFC 4122 / network byte order and
    /// <see cref="ToGuid(ReadOnlySpan{byte}, int, EndianType)"/>.
    /// </remarks>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 16 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteGuid(Span<byte> destination, int offset, Guid value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, 16, ValueKind.Guid);

#if NET8_0_OR_GREATER
        value.TryWriteBytes(destination.Slice(offset, 16), bigEndian: endian == EndianType.BIG, out _);
#else
        // NOTE(VNC): A Guid stores Data1 (int), Data2 (short), Data3 (short) and Data4 (8 bytes) in that order, so its memory is the byte
        // layout in host order. For the other byte order only the first three components are reversed, as ToGuid reads them. Swapping
        // them as integers is several times faster on .NET Framework than reversing the bytes with Span<T>.Reverse.
        WriteAt(destination, offset, value);

        bool littleEndian = endian == EndianType.LITTLE;

        if (littleEndian != HostByteOrder.IsLittleEndian)
        {
            WriteAt(destination, offset, BinaryPrimitives.ReverseEndianness(ReadAt<int>(destination, offset)));
            WriteAt(destination, offset + 4, BinaryPrimitives.ReverseEndianness(ReadAt<short>(destination, offset + 4)));
            WriteAt(destination, offset + 6, BinaryPrimitives.ReverseEndianness(ReadAt<short>(destination, offset + 6)));
        }
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the first 2 bytes of <paramref name="destination"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination. Must be at least 2 bytes long.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter than 2 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt16(Span<byte> destination, short value, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(destination.Length, sizeof(short));
        WriteAt(destination, 0, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
#else
        if (endian == EndianType.LITTLE)
            BinaryPrimitives.WriteInt16LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteInt16BigEndian(destination, value);
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 2 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 2 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt16(Span<byte> destination, int offset, short value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, sizeof(short), ValueKind.Int16);
        WriteAt(destination, offset, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the first 4 bytes of <paramref name="destination"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination. Must be at least 4 bytes long.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter than 4 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32(Span<byte> destination, int value, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(destination.Length, sizeof(int));
        WriteAt(destination, 0, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
#else
        if (endian == EndianType.LITTLE)
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteInt32BigEndian(destination, value);
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 4 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 4 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32(Span<byte> destination, int offset, int value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, sizeof(int), ValueKind.Int32);
        WriteAt(destination, offset, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the first 8 bytes of <paramref name="destination"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination. Must be at least 8 bytes long.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt64(Span<byte> destination, long value, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(destination.Length, sizeof(long));
        WriteAt(destination, 0, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
#else
        if (endian == EndianType.LITTLE)
            BinaryPrimitives.WriteInt64LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteInt64BigEndian(destination, value);
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 8 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 8 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt64(Span<byte> destination, int offset, long value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, sizeof(long), ValueKind.Int64);
        WriteAt(destination, offset, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the first 2 bytes of <paramref name="destination"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination. Must be at least 2 bytes long.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter than 2 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16(Span<byte> destination, ushort value, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(destination.Length, sizeof(ushort));
        WriteAt(destination, 0, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
#else
        if (endian == EndianType.LITTLE)
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 2 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 2 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16(Span<byte> destination, int offset, ushort value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, sizeof(ushort), ValueKind.UInt16);
        WriteAt(destination, offset, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the first 4 bytes of <paramref name="destination"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination. Must be at least 4 bytes long.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter than 4 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32(Span<byte> destination, uint value, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(destination.Length, sizeof(uint));
        WriteAt(destination, 0, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
#else
        if (endian == EndianType.LITTLE)
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 4 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 4 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32(Span<byte> destination, int offset, uint value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, sizeof(uint), ValueKind.UInt32);
        WriteAt(destination, offset, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
    }

    /// <summary>
    /// Writes <paramref name="value"/> to the first 8 bytes of <paramref name="destination"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination. Must be at least 8 bytes long.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="destination"/> is shorter than 8 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt64(Span<byte> destination, ulong value, EndianType endian)
    {
#if NETSTANDARD2_0
        EnsureLength(destination.Length, sizeof(ulong));
        WriteAt(destination, 0, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
#else
        if (endian == EndianType.LITTLE)
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
#endif
    }

    /// <summary>
    /// Writes <paramref name="value"/> to 8 bytes of <paramref name="destination"/>, starting at <paramref name="offset"/>, using the specified
    /// <paramref name="endian"/> order.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based offset into <paramref name="destination"/> to start writing at.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="endian">The <see langword="byte"/> order to write in.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 8 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt64(Span<byte> destination, int offset, ulong value, EndianType endian)
    {
        EnsureSpace(destination.Length, offset, sizeof(ulong), ValueKind.UInt64);
        WriteAt(destination, offset, endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value));
    }

    /// <summary>
    /// Ensures that <paramref name="requiredByteCount"/> bytes can be written starting at <paramref name="offset"/> within a buffer of length
    /// <paramref name="destinationLength"/>.
    /// </summary>
    /// <param name="destinationLength">The length, in bytes, of the destination buffer.</param>
    /// <param name="offset">The zero-based offset to start writing at.</param>
    /// <param name="requiredByteCount">The number of bytes required starting at <paramref name="offset"/>.</param>
    /// <param name="kind">The type being written, named in the exception message.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when fewer than <paramref name="requiredByteCount"/> bytes are available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureSpace(int destinationLength, int offset, int requiredByteCount, ValueKind kind)
    {
        // NOTE(VNC): Casting to `uint` folds the "negative offset" and "offset beyond the buffer" cases
        // into a single, overflow-safe comparison.
        if ((uint)offset > (uint)destinationLength || destinationLength - offset < requiredByteCount)
            ThrowNotEnoughSpace(offset, requiredByteCount, kind);
    }

    /// <summary>
    /// Converts the specified single-precision floating-point number to a 32-bit signed integer by reinterpreting its bit pattern.
    /// </summary>
    /// <param name="value">The single-precision floating-point number to convert.</param>
    /// <returns>A 32-bit signed integer with the same bit representation as <paramref name="value"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SingleToInt32Bits(float value)
    {
#if NETSTANDARD2_0
        return Unsafe.As<float, int>(ref value);
#else
        return BitConverter.SingleToInt32Bits(value);
#endif
    }

    /// <summary>
    /// Throws the exception for an offset that leaves fewer than <paramref name="requiredByteCount"/> bytes to write.
    /// </summary>
    /// <remarks>Kept out of line like <see cref="ThrowNotEnoughData"/>, so the bounds check in <see cref="EnsureSpace"/> stays a compare and a branch.</remarks>
    /// <param name="offset">The zero-based offset to start writing at.</param>
    /// <param name="requiredByteCount">The number of bytes required starting at <paramref name="offset"/>.</param>
    /// <param name="kind">The type being written.</param>
    /// <exception cref="ArgumentOutOfRangeException">Always thrown.</exception>
    private static void ThrowNotEnoughSpace(int offset, int requiredByteCount, ValueKind kind)
    {
        throw new ArgumentOutOfRangeException(nameof(offset), offset, $"Not enough space to write a {kind} value ({requiredByteCount} bytes required) at the given offset.");
    }

    /// <summary>
    /// Writes <paramref name="value"/> in host byte order to <paramref name="destination"/> at <paramref name="offset"/>, without slicing.
    /// </summary>
    /// <remarks>The caller must have validated the range, like for <see cref="ReadAt{T}"/>.</remarks>
    /// <typeparam name="T">The primitive type to write.</typeparam>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The zero-based, already validated offset into <paramref name="destination"/>.</param>
    /// <param name="value">The value in host byte order.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteAt<T>(Span<byte> destination, int offset, T value)
        where T : struct
    {
#if NETSTANDARD2_0
        Unsafe.WriteUnaligned(ref destination[offset], value);
#elif NETSTANDARD2_1
        MemoryMarshal.Write(destination[offset..], ref value);
#else
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(destination), (nint)(uint)offset), value);
#endif
    }
}
