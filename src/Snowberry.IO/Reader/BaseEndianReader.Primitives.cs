using Snowberry.IO.Common;

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <inheritdoc/>
    public Sha1 ReadSha1()
    {
        Span<byte> bytes = stackalloc byte[Sha1.StructSize];
        Read(bytes);
        return new(bytes);
    }

    /// <inheritdoc/>
    public long ReadLong(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[8];
        Read(bytes);
        return BinaryEndianConverter.ToLong(bytes, endian);
    }

    /// <inheritdoc/>
    public ulong ReadULong(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[8];
        Read(bytes);
        return BinaryEndianConverter.ToULong(bytes, endian);
    }

    /// <inheritdoc/>
    public uint ReadUInt32(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[4];
        Read(bytes);
        return BinaryEndianConverter.ToUInt32(bytes, endian);
    }

    /// <inheritdoc/>
    public ushort ReadUInt16(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[2];
        Read(bytes);
        return BinaryEndianConverter.ToUInt16(bytes, endian);
    }

    /// <inheritdoc/>
    public int ReadInt32(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[4];
        Read(bytes);
        return BinaryEndianConverter.ToInt32(bytes, endian);
    }

    /// <inheritdoc/>
    public short ReadInt16(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[2];
        Read(bytes);
        return BinaryEndianConverter.ToInt16(bytes, endian);
    }

    /// <inheritdoc/>
    public Guid ReadGuid(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[16];
        Read(bytes);
        return BinaryEndianConverter.ToGuid(bytes, 0, endian);
    }

    /// <inheritdoc/>
    public float ReadFloat(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[4];
        Read(bytes);
        return BinaryEndianConverter.ToFloat(bytes, endian);
    }

    /// <inheritdoc/>
    public unsafe double ReadDouble(EndianType endian = EndianType.LITTLE)
    {
        Span<byte> bytes = stackalloc byte[8];
        Read(bytes);
        return BinaryEndianConverter.ToDouble(bytes, endian);
    }
}
