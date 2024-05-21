using System;
using System.Runtime.CompilerServices;

namespace Snowberry.IO.Common;

public static partial class BinaryEndianConverter
{
#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static unsafe long ToLong(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<long>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return (uint)((bigSource[4] << 24) | (bigSource[5] << 16) | (bigSource[6] << 8) | bigSource[7])
                    | ((long)((bigSource[0] << 24) | (bigSource[1] << 16) | (bigSource[2] << 8) | bigSource[3]) << 32);
    }

#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static unsafe ulong ToULong(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<ulong>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return unchecked((ulong)((uint)((bigSource[4] << 24) | (bigSource[5] << 16) | (bigSource[6] << 8) | bigSource[7])
                    | ((long)((bigSource[0] << 24) | (bigSource[1] << 16) | (bigSource[2] << 8) | bigSource[3]) << 32)));
    }

#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static unsafe ushort ToUInt16(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<ushort>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return unchecked((ushort)(bigSource[1] | (bigSource[0] << 8)));
    }

#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static unsafe short ToInt16(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<short>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return (short)(bigSource[1] | (bigSource[0] << 8));
    }

#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static unsafe uint ToUInt32(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<uint>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return unchecked((uint)(bigSource[3] | (bigSource[2] << 8) | (bigSource[1] << 16) | (bigSource[0] << 24)));
    }

#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static unsafe int ToInt32(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<int>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return bigSource[3] | (bigSource[2] << 8) | (bigSource[1] << 16) | (bigSource[0] << 24);
    }

#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static Guid ToGuid(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.BIG)
        {
            return new(
            [
                data[offset + 3], data[offset + 2], data[offset + 1], data[offset],
                data[offset + 5], data[offset + 4], data[offset + 7], data[offset + 6],
                data[offset + 8], data[offset + 9], data[offset + 10], data[offset + 11],
                data[offset + 12], data[offset + 13], data[offset + 14], data[offset + 15]
            ]);
        }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_1_OR_GREATER
        return new(data.Slice(offset, 16));
#else
        return new Guid(data.Slice(offset, 16).ToArray());
#endif
    }

#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static unsafe float ToFloat(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<float>(ref data[offset]);

        int temp = ToInt32(data, offset, EndianType.BIG);

#if NETSTANDARD2_0
        return Int32BitsToSingle(temp);
#else
        return BitConverter.Int32BitsToSingle(temp);
#endif

    }

#if NETCOREAPP3_0_OR_GREATER
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
#endif
    public static unsafe double ToDouble(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<double>(ref data[offset]);

        long temp = ToLong(data, offset, EndianType.BIG);
        return BitConverter.Int64BitsToDouble(temp);
    }

#if NETSTANDARD2_0
    /// <summary>
    /// Converts the specified 32-bit signed integer to a single-precision floating-point number
    /// by reinterpreting its bit pattern.
    /// </summary>
    /// <param name="value">The 32-bit signed integer to convert.</param>
    /// <returns>A single-precision floating-point number with the same bit representation as the input integer.</returns>
    public static float Int32BitsToSingle(int value)
    {
        unsafe
        {
            // Create a float pointer and assign the address of the integer
            float result = *(float*)&value;
            return result;
        }
    }
#endif
}
