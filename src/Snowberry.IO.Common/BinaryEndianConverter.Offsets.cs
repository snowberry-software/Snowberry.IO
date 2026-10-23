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
    /// Converts 8 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a double-precision floating-point number using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted double-precision floating-point number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 8 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToDouble(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, sizeof(double), ValueKind.Double);
        long bits = ReadAt<long>(data, offset);
        return BitConverter.Int64BitsToDouble(endian == HostEndian ? bits : BinaryPrimitives.ReverseEndianness(bits));
    }

    /// <summary>
    /// Converts 4 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a single-precision floating-point number using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted single-precision floating-point number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 4 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToFloat(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, sizeof(float), ValueKind.Single);
        int bits = ReadAt<int>(data, offset);
        return Int32BitsToSingle(endian == HostEndian ? bits : BinaryPrimitives.ReverseEndianness(bits));
    }

    /// <summary>
    /// Converts 16 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a <see cref="Guid"/> using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <remarks>When <paramref name="endian"/> is <see cref="EndianType.BIG"/>, the first three components (<c>Data1</c>, <c>Data2</c> and <c>Data3</c>) are read in big-endian order while the remaining 8 bytes (<c>Data4</c>) are read as-is, matching RFC 4122 / network byte order.</remarks>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted <see cref="Guid"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 16 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ToGuid(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, 16, ValueKind.Guid);

#if NET8_0_OR_GREATER
        // NOTE(VNC): The single-argument constructor is a plain copy; the bigEndian overload costs several times more, so it is only
        // used when the byte order requires it. Reading the Guid into a local and reversing it in place is several times slower here,
        // because passing the local by reference keeps it in memory.
        return endian == EndianType.LITTLE
            ? new Guid(data.Slice(offset, 16))
            : new Guid(data.Slice(offset, 16), bigEndian: true);
#elif NETSTANDARD2_1
        // NOTE(VNC): A Guid stores Data1 (int), Data2 (short), Data3 (short) and Data4 (8 bytes) in that order, so on a little-endian
        // host its memory matches the little-endian byte layout and can be read in one go.
        if (endian == EndianType.LITTLE && BitConverter.IsLittleEndian)
            return ReadAt<Guid>(data, offset);

        int a = ReadAt<int>(data, offset);
        short b = ReadAt<short>(data, offset + 4);
        short c = ReadAt<short>(data, offset + 6);

        if (endian != HostEndian)
        {
            a = BinaryPrimitives.ReverseEndianness(a);
            b = BinaryPrimitives.ReverseEndianness(b);
            c = BinaryPrimitives.ReverseEndianness(c);
        }

        return new Guid(a, b, c, data[offset + 8], data[offset + 9], data[offset + 10], data[offset + 11], data[offset + 12], data[offset + 13], data[offset + 14], data[offset + 15]);
#else
        // NOTE(VNC): A Guid stores Data1 (int), Data2 (short), Data3 (short) and Data4 (8 bytes) in that order, so its memory matches
        // the byte layout in host order and is read in one go. For the other byte order only the first three components are reversed,
        // as Guid(ReadOnlySpan<byte>, bool) does.
        var value = ReadAt<Guid>(data, offset);
        bool littleEndian = endian == EndianType.LITTLE;

        if (littleEndian != HostByteOrder.IsLittleEndian)
            ReverseFirstThreeComponents(ref value);

        return value;
#endif
    }

    /// <summary>
    /// Converts 2 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a 16-bit signed integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 16-bit signed integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 2 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ToInt16(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, sizeof(short), ValueKind.Int16);
        short value = ReadAt<short>(data, offset);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    /// <summary>
    /// Converts 4 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a 32-bit signed integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 32-bit signed integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 4 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, sizeof(int), ValueKind.Int32);
        int value = ReadAt<int>(data, offset);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    /// <summary>
    /// Converts 8 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a 64-bit signed integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 64-bit signed integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 8 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToInt64(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, sizeof(long), ValueKind.Int64);
        long value = ReadAt<long>(data, offset);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    /// <summary>
    /// Converts 2 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a 16-bit unsigned integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 16-bit unsigned integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 2 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ToUInt16(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, sizeof(ushort), ValueKind.UInt16);
        ushort value = ReadAt<ushort>(data, offset);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    /// <summary>
    /// Converts 4 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a 32-bit unsigned integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 32-bit unsigned integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 4 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ToUInt32(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, sizeof(uint), ValueKind.UInt32);
        uint value = ReadAt<uint>(data, offset);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    /// <summary>
    /// Converts 8 bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, to a 64-bit unsigned integer using the specified <paramref name="endian"/> order.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based offset into <paramref name="data"/> to start reading from.</param>
    /// <param name="endian">The <see langword="byte"/> order of <paramref name="data"/>.</param>
    /// <returns>The converted 64-bit unsigned integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when there are fewer than 8 bytes available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToUInt64(ReadOnlySpan<byte> data, int offset, EndianType endian)
    {
        EnsureAvailable(data.Length, offset, sizeof(ulong), ValueKind.UInt64);
        ulong value = ReadAt<ulong>(data, offset);
        return endian == HostEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }

    /// <summary>
    /// Ensures that <paramref name="requiredByteCount"/> bytes are available starting at <paramref name="offset"/> within a buffer of length <paramref name="dataLength"/>.
    /// </summary>
    /// <param name="dataLength">The length, in bytes, of the source buffer.</param>
    /// <param name="offset">The zero-based offset to start reading from.</param>
    /// <param name="requiredByteCount">The number of bytes required starting at <paramref name="offset"/>.</param>
    /// <param name="kind">The type being read, named in the exception message.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="offset"/> is negative, or when fewer than <paramref name="requiredByteCount"/> bytes are available starting at <paramref name="offset"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnsureAvailable(int dataLength, int offset, int requiredByteCount, ValueKind kind)
    {
        // NOTE(VNC): Casting to `uint` folds the "negative offset" and "offset beyond the buffer" cases
        // into a single, overflow-safe comparison.
        if ((uint)offset > (uint)dataLength || dataLength - offset < requiredByteCount)
            ThrowNotEnoughData(offset, requiredByteCount, kind);
    }

    /// <summary>
    /// Converts the specified 32-bit signed integer to a single-precision floating-point number by reinterpreting its bit pattern.
    /// </summary>
    /// <param name="value">The 32-bit signed integer to convert.</param>
    /// <returns>A single-precision floating-point number with the same bit representation as <paramref name="value"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Int32BitsToSingle(int value)
    {
#if NETSTANDARD2_0
        return Unsafe.As<int, float>(ref value);
#else
        return BitConverter.Int32BitsToSingle(value);
#endif
    }

    /// <summary>
    /// Reads a <typeparamref name="T"/> in host byte order from <paramref name="data"/> at <paramref name="offset"/>, without slicing.
    /// </summary>
    /// <remarks>
    /// The caller must have validated the range with <see cref="EnsureAvailable"/>, so the .NET build reads without another range check.
    /// On .NET Framework the span indexer passes <paramref name="data"/> by reference, which keeps the read inlinable, where
    /// <c>MemoryMarshal.GetReference</c> would copy the span.
    /// </remarks>
    /// <typeparam name="T">The primitive type to read.</typeparam>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The zero-based, already validated offset into <paramref name="data"/>.</param>
    /// <returns>The value in host byte order.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T ReadAt<T>(ReadOnlySpan<byte> data, int offset)
        where T : struct
    {
#if NETSTANDARD2_0
        return Unsafe.ReadUnaligned<T>(ref Unsafe.AsRef(in data[offset]));
#elif NETSTANDARD2_1
        return MemoryMarshal.Read<T>(data[offset..]);
#else
        return Unsafe.ReadUnaligned<T>(ref Unsafe.Add(ref MemoryMarshal.GetReference(data), (nint)(uint)offset));
#endif
    }

#if NETSTANDARD2_0
    /// <summary>
    /// Reverses the byte order of the <c>Data1</c>, <c>Data2</c> and <c>Data3</c> components of <paramref name="value"/> in place.
    /// </summary>
    /// <param name="value">The <see cref="Guid"/> to change.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReverseFirstThreeComponents(ref Guid value)
    {
        ref byte bytes = ref Unsafe.As<Guid, byte>(ref value);
        Unsafe.WriteUnaligned(ref bytes, BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<int>(ref bytes)));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref bytes, 4), BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<short>(ref Unsafe.Add(ref bytes, 4))));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref bytes, 6), BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<short>(ref Unsafe.Add(ref bytes, 6))));
    }
#endif

    /// <summary>
    /// Throws the exception for an offset that leaves fewer than <paramref name="requiredByteCount"/> bytes to read.
    /// </summary>
    /// <remarks>
    /// The JIT does not inline a method that always throws and treats the call as not returning, so the bounds check in
    /// <see cref="EnsureAvailable"/> costs only a compare and a branch, and the message with its string literals stays out of the converters.
    /// </remarks>
    /// <param name="offset">The zero-based offset to start reading from.</param>
    /// <param name="requiredByteCount">The number of bytes required starting at <paramref name="offset"/>.</param>
    /// <param name="kind">The type being read.</param>
    /// <exception cref="ArgumentOutOfRangeException">Always thrown.</exception>
    private static void ThrowNotEnoughData(int offset, int requiredByteCount, ValueKind kind)
    {
        throw new ArgumentOutOfRangeException(nameof(offset), offset, $"Not enough data to read a {kind} value ({requiredByteCount} bytes required) at the given offset.");
    }

    /// <summary>
    /// Gets the <see cref="EndianType"/> of the host, which <see cref="ReadAt{T}"/> reads in.
    /// </summary>
    private static EndianType HostEndian => HostByteOrder.Endian;

    /// <summary>
    /// The value types that the offset overloads read, named in the exception for an offset that leaves too few bytes.
    /// </summary>
    /// <remarks>
    /// The converters pass this instead of a type name: on .NET Framework a method that loads a string literal never inlines into
    /// another assembly. Each name formats as the name of the matching type.
    /// </remarks>
    private enum ValueKind : byte
    {
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Guid
    }
}