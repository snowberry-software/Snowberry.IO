using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Snowberry.IO;

public static partial class BinaryEndianConverter
{
    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe int ToInt32(Span<byte> data, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(data));

        byte* bigSource = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data));
        return bigSource[3] | (bigSource[2] << 8) | (bigSource[1] << 16) | (bigSource[0] << 24);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe uint ToUInt32(Span<byte> data, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(data));

        byte* bigSource = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data));
        return unchecked((uint)(bigSource[3] | (bigSource[2] << 8) | (bigSource[1] << 16) | (bigSource[0] << 24)));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe ushort ToUInt16(Span<byte> data, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(data));

        byte* bigSource = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data));
        return unchecked((ushort)(bigSource[1] | (bigSource[0] << 8)));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe short ToInt16(Span<byte> data, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<short>(ref MemoryMarshal.GetReference(data));

        byte* bigSource = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data));
        return (short)(bigSource[1] | (bigSource[0] << 8));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe float ToFloat(Span<byte> data, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(data));

        int temp = ToInt32(data, EndianType.BIG);
        return BitConverter.Int32BitsToSingle(temp);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe double ToDouble(Span<byte> data, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<double>(ref MemoryMarshal.GetReference(data));

        long temp = ToLong(data, EndianType.BIG);
        return BitConverter.Int64BitsToDouble(temp);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe long ToLong(Span<byte> data, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(data));

        byte* bigSource = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data));
        return (uint)((bigSource[4] << 24) | (bigSource[5] << 16) | (bigSource[6] << 8) | bigSource[7])
                    | ((long)((bigSource[0] << 24) | (bigSource[1] << 16) | (bigSource[2] << 8) | bigSource[3]) << 32);
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static unsafe ulong ToULong(Span<byte> data, EndianType endian)
    {
        if (endian == EndianType.LITTLE)
            return Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(data));

        byte* bigSource = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data));
        return unchecked((ulong)((uint)((bigSource[4] << 24) | (bigSource[5] << 16) | (bigSource[6] << 8) | bigSource[7])
                    | ((long)((bigSource[0] << 24) | (bigSource[1] << 16) | (bigSource[2] << 8) | bigSource[3]) << 32)));
    }
}
