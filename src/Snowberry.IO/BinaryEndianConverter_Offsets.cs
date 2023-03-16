using System.Runtime.CompilerServices;

namespace Snowberry.IO;

public static partial class BinaryEndianConverter
{
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe long ToLong(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<long>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return (uint)((bigSource[4] << 24) | (bigSource[5] << 16) | (bigSource[6] << 8) | bigSource[7])
                    | ((long)((bigSource[0] << 24) | (bigSource[1] << 16) | (bigSource[2] << 8) | bigSource[3]) << 32);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe ulong ToULong(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<ulong>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return unchecked((ulong)((uint)((bigSource[4] << 24) | (bigSource[5] << 16) | (bigSource[6] << 8) | bigSource[7])
                    | ((long)((bigSource[0] << 24) | (bigSource[1] << 16) | (bigSource[2] << 8) | bigSource[3]) << 32)));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe ushort ToUInt16(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<ushort>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return unchecked((ushort)(bigSource[1] | (bigSource[0] << 8)));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe short ToInt16(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<short>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return (short)(bigSource[1] | (bigSource[0] << 8));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe uint ToUInt32(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<uint>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return unchecked((uint)(bigSource[3] | (bigSource[2] << 8) | (bigSource[1] << 16) | (bigSource[0] << 24)));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int ToInt32(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<int>(ref data[offset]);

        byte* bigSource = (byte*)Unsafe.AsPointer(ref data[offset]);
        return bigSource[3] | (bigSource[2] << 8) | (bigSource[1] << 16) | (bigSource[0] << 24);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static Guid ToGuid(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.BIG)
        {
            return new(new[]
            {
                data[offset + 3], data[offset + 2], data[offset + 1], data[offset],
                data[offset + 5], data[offset + 4], data[offset + 7], data[offset + 6],
                data[offset + 8], data[offset + 9], data[offset + 10], data[offset + 11],
                data[offset + 12], data[offset + 13], data[offset + 14], data[offset + 15]
            });
        }

        return new(data.Slice(offset, 16));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe float ToFloat(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<float>(ref data[offset]);

        int temp = ToInt32(data, offset, EndianType.BIG);
        return BitConverter.Int32BitsToSingle(temp);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe double ToDouble(Span<byte> data, int offset, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<double>(ref data[offset]);

        long temp = ToLong(data, offset, EndianType.BIG);
        return BitConverter.Int64BitsToDouble(temp);
    }
}
