using Snowberry.IO.Common;

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <inheritdoc/>
    public Sha1 ReadSha1()
    {
        Span<byte> bytes = stackalloc byte[Sha1.StructSize];
        ReadExactly(bytes);
        return new(bytes);
    }

    /// <inheritdoc/>
    public long ReadInt64(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[8];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToInt64(bytes, endian);
    }

    /// <inheritdoc/>
    public ulong ReadUInt64(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[8];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToUInt64(bytes, endian);
    }

    /// <inheritdoc/>
    public uint ReadUInt32(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[4];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToUInt32(bytes, endian);
    }

    /// <inheritdoc/>
    public ushort ReadUInt16(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[2];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToUInt16(bytes, endian);
    }

    /// <inheritdoc/>
    public int ReadInt32(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[4];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToInt32(bytes, endian);
    }

    /// <inheritdoc/>
    public short ReadInt16(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[2];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToInt16(bytes, endian);
    }

    /// <inheritdoc/>
    public Guid ReadGuid(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[16];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToGuid(bytes, 0, endian);
    }

    /// <inheritdoc/>
    public float ReadFloat(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[4];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToFloat(bytes, endian);
    }

    /// <inheritdoc/>
    public unsafe double ReadDouble(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[8];
        ReadExactly(bytes);
        return BinaryEndianConverter.ToDouble(bytes, endian);
    }
}
