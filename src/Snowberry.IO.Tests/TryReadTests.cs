using Snowberry.IO.Common;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;

namespace Snowberry.IO.Tests;

public class TryReadTests
{
    [Fact]
    public void TryReadByte_Success()
    {
        var stream = new MemoryStream(new byte[] { 42 });
        using var reader = new EndianStreamReader(stream);

        bool success = reader.TryReadByte(out byte value);

        Assert.True(success);
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryReadByte_EndOfStream_ReturnsFalse()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        bool success = reader.TryReadByte(out byte value);

        Assert.False(success);
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryReadSByte_Success()
    {
        var stream = new MemoryStream(new byte[] { 250 });
        using var reader = new EndianStreamReader(stream);

        bool success = reader.TryReadSByte(out sbyte value);

        Assert.True(success);
        Assert.Equal(-6, value);
    }

    [Fact]
    public void TryReadInt16_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write((short)12345);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadInt16(out short value);

        Assert.True(success);
        Assert.Equal(12345, value);
    }

    [Fact]
    public void TryReadInt16_InsufficientData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1 });
        using var reader = new EndianStreamReader(stream);

        bool success = reader.TryReadInt16(out short value);

        Assert.False(success);
        Assert.Equal(0, value);
    }

    [Fact]
    public void TryReadInt32_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(123456789);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadInt32(out int value);

        Assert.True(success);
        Assert.Equal(123456789, value);
    }

    [Fact]
    public void TryReadInt32_BigEndian_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(123456789, EndianType.BIG);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadInt32(out int value, EndianType.BIG);

        Assert.True(success);
        Assert.Equal(123456789, value);
    }

    [Fact]
    public void TryReadInt64_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(123456789123456789L);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadInt64(out long value);

        Assert.True(success);
        Assert.Equal(123456789123456789L, value);
    }

    [Fact]
    public void TryReadFloat_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(3.14159f);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadFloat(out float value);

        Assert.True(success);
        Assert.Equal(3.14159f, value, 5);
    }

    [Fact]
    public void TryReadDouble_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(3.141592653589793);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadDouble(out double value);

        Assert.True(success);
        Assert.Equal(3.141592653589793, value, 10);
    }

    [Fact]
    public void TryReadGuid_Success()
    {
        var guid = Guid.NewGuid();
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(guid);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadGuid(out var value);

        Assert.True(success);
        Assert.Equal(guid, value);
    }

    [Fact]
    public void TryReadGuid_InsufficientData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[10]);
        using var reader = new EndianStreamReader(stream);

        bool success = reader.TryReadGuid(out var value);

        Assert.False(success);
        Assert.Equal(Guid.Empty, value);
    }

    [Fact]
    public void TryRead_MultipleValues_SuccessAndFailure()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(100);
            writer.Write(200);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);

        Assert.True(reader.TryReadInt32(out int value1));
        Assert.Equal(100, value1);

        Assert.True(reader.TryReadInt32(out int value2));
        Assert.Equal(200, value2);

        Assert.False(reader.TryReadInt32(out int value3));
        Assert.Equal(0, value3);
    }

    [Fact]
    public void TryReadUInt16_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write((ushort)65000);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadUInt16(out ushort value);

        Assert.True(success);
        Assert.Equal((ushort)65000, value);
    }

    [Fact]
    public void TryReadUInt32_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(4000000000u);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadUInt32(out uint value);

        Assert.True(success);
        Assert.Equal(4000000000u, value);
    }

    [Fact]
    public void TryReadUInt64_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(10000000000000000000ul);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadUInt64(out ulong value);

        Assert.True(success);
        Assert.Equal(10000000000000000000ul, value);
    }

    [Fact]
    public void TryRead_DoesNotThrowOnEndOfStream()
    {
        var stream = new MemoryStream(new byte[1]);
        using var reader = new EndianStreamReader(stream);

        reader.ReadByte();

        Assert.False(reader.TryReadByte(out _));
        Assert.False(reader.TryReadInt16(out _));
        Assert.False(reader.TryReadInt32(out _));
        Assert.False(reader.TryReadInt64(out _));
        Assert.False(reader.TryReadFloat(out _));
        Assert.False(reader.TryReadDouble(out _));
        Assert.False(reader.TryReadGuid(out _));
    }

    [Fact]
    public void TryReadInt16_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadInt16(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadUInt16_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadUInt16(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadInt32_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadInt32(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadUInt32_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadUInt32(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadInt64_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadInt64(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadUInt64_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadUInt64(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadFloat_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadFloat(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadDouble_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadDouble(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadGuid_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadGuid(out _));
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void TryReadSha1_Success()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            byte[] hashBytes = new byte[Sha1.StructSize];
            new Random(42).NextBytes(hashBytes);
            var sha1 = new Sha1(hashBytes);
            writer.Write(sha1);
        }

        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        bool success = reader.TryReadSha1(out var value);

        Assert.True(success);
        Assert.NotEqual(Sha1.Zero, value);
    }

    [Fact]
    public void TryReadSha1_InsufficientData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[15]);
        using var reader = new EndianStreamReader(stream);

        bool success = reader.TryReadSha1(out var value);

        Assert.False(success);
        Assert.Equal(Sha1.Zero, value);
    }

    [Fact]
    public void TryReadSha1_PartialData_ReturnsFalse()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.TryReadSha1(out _));
        Assert.Equal(0, reader.Position);
    }
}
