using System.Runtime.CompilerServices;
using Snowberry.IO.Common;

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadByte(out byte value)
    {
        if (ReadInInternalBuffer(1, 0) == 0)
        {
            value = 0;
            return false;
        }

        value = _buffer[0];
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
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
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadInt16(out short value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[2];
        int bytesRead = Read(bytes);

        if (bytesRead != 2)
        {
            Position = startPosition;
            value = 0;
            return false;
        }

        value = BinaryEndianConverter.ToInt16(bytes, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadUInt16(out ushort value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[2];
        int bytesRead = Read(bytes);

        if (bytesRead != 2)
        {
            Position = startPosition;
            value = 0;
            return false;
        }

        value = BinaryEndianConverter.ToUInt16(bytes, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadInt32(out int value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[4];
        int bytesRead = Read(bytes);

        if (bytesRead != 4)
        {
            Position = startPosition;
            value = 0;
            return false;
        }

        value = BinaryEndianConverter.ToInt32(bytes, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadUInt32(out uint value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[4];
        int bytesRead = Read(bytes);

        if (bytesRead != 4)
        {
            Position = startPosition;
            value = 0;
            return false;
        }

        value = BinaryEndianConverter.ToUInt32(bytes, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadInt64(out long value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[8];
        int bytesRead = Read(bytes);

        if (bytesRead != 8)
        {
            Position = startPosition;
            value = 0;
            return false;
        }

        value = BinaryEndianConverter.ToInt64(bytes, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadUInt64(out ulong value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[8];
        int bytesRead = Read(bytes);

        if (bytesRead != 8)
        {
            Position = startPosition;
            value = 0;
            return false;
        }

        value = BinaryEndianConverter.ToUInt64(bytes, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadFloat(out float value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[4];
        int bytesRead = Read(bytes);

        if (bytesRead != 4)
        {
            Position = startPosition;
            value = 0;
            return false;
        }

        value = BinaryEndianConverter.ToFloat(bytes, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadDouble(out double value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[8];
        int bytesRead = Read(bytes);

        if (bytesRead != 8)
        {
            Position = startPosition;
            value = 0;
            return false;
        }

        value = BinaryEndianConverter.ToDouble(bytes, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadGuid(out Guid value, EndianType endian = EndianType.LITTLE)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[16];
        int bytesRead = Read(bytes);

        if (bytesRead != 16)
        {
            Position = startPosition;
            value = Guid.Empty;
            return false;
        }

        value = BinaryEndianConverter.ToGuid(bytes, 0, endian);
        return true;
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public bool TryReadSha1(out Sha1 value)
    {
        long startPosition = Position;
        Span<byte> bytes = stackalloc byte[Sha1.StructSize];
        int bytesRead = Read(bytes);

        if (bytesRead != Sha1.StructSize)
        {
            Position = startPosition;
            value = Sha1.Zero;
            return false;
        }

        value = new Sha1(bytes);
        return true;
    }
}
