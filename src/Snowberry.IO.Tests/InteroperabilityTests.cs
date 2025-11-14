using Snowberry.IO.Common;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains tests for binary data interoperability and compatibility scenarios.
/// </summary>
public class InteroperabilityTests
{
    [Fact]
    public void CSharpBinaryWriter_CompatibleWithEndianStreamReader()
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(100);
            writer.Write(200L);
            writer.Write("Test");
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        Assert.Equal(100, reader.ReadInt32());
        Assert.Equal(200L, reader.ReadInt64());
        Assert.Equal("Test", reader.ReadString());
    }

    [Fact]
    public void EndianStreamWriter_CompatibleWithCSharpBinaryReader()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(100);
            writer.Write(200L);
            writer.Write("Test");
        }

        stream.Position = 0;
        using var reader = new BinaryReader(stream);

        Assert.Equal(100, reader.ReadInt32());
        Assert.Equal(200L, reader.ReadInt64());
        Assert.Equal("Test", reader.ReadString());
    }

    [Fact]
    public void LittleEndian_MatchesSystemBitConverter()
    {
        int value = 0x12345678;

        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write(value, EndianType.LITTLE);

        byte[] writerBytes = stream.ToArray();
        byte[] converterBytes = BitConverter.GetBytes(value);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(converterBytes);
        }

        Assert.Equal(converterBytes, writerBytes);
    }

    [Fact]
    public void BigEndian_ReverseOfLittleEndian()
    {
        int value = 0x12345678;

        var streamLE = new MemoryStream();
        using (var writer = new EndianStreamWriter(streamLE, true))
        {
            writer.Write(value, EndianType.LITTLE);
        }

        var streamBE = new MemoryStream();
        using (var writer = new EndianStreamWriter(streamBE, true))
        {
            writer.Write(value, EndianType.BIG);
        }

        byte[] leBytes = streamLE.ToArray();
        byte[] beBytes = streamBE.ToArray();

        Assert.Equal(leBytes[0], beBytes[3]);
        Assert.Equal(leBytes[1], beBytes[2]);
        Assert.Equal(leBytes[2], beBytes[1]);
        Assert.Equal(leBytes[3], beBytes[0]);
    }

    [Fact]
    public void GuidBytes_LittleEndian_MatchesDotNetFormat()
    {
        var guid = Guid.NewGuid();
        byte[] dotNetBytes = guid.ToByteArray();

        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write(guid, EndianType.LITTLE);

        byte[] writerBytes = stream.ToArray();
        Assert.Equal(dotNetBytes, writerBytes);
    }

    [Fact]
    public void FloatBytes_MatchesBitConverter()
    {
        float value = 123.456f;

        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write(value, EndianType.LITTLE);

        byte[] writerBytes = stream.ToArray();
        byte[] converterBytes = BitConverter.GetBytes(value);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(converterBytes);
        }

        Assert.Equal(converterBytes, writerBytes);
    }

    [Fact]
    public void DoubleBytes_MatchesBitConverter()
    {
        double value = 123.456789;

        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write(value, EndianType.LITTLE);

        byte[] writerBytes = stream.ToArray();
        byte[] converterBytes = BitConverter.GetBytes(value);

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(converterBytes);
        }

        Assert.Equal(converterBytes, writerBytes);
    }

    [Fact]
    public void CrossPlatform_SameOutput()
    {
        // Test that the same data produces the same binary output regardless of platform

        var stream1 = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream1, true))
        {
            writer.Write(100, EndianType.LITTLE);
            writer.Write(200L, EndianType.LITTLE);
            writer.Write(300.5f, EndianType.LITTLE);
            writer.Write(400.123, EndianType.LITTLE);
        }

        var stream2 = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream2, true))
        {
            writer.Write(100, EndianType.LITTLE);
            writer.Write(200L, EndianType.LITTLE);
            writer.Write(300.5f, EndianType.LITTLE);
            writer.Write(400.123, EndianType.LITTLE);
        }

        Assert.Equal(stream1.ToArray(), stream2.ToArray());
    }

    [Fact]
    public void ReadWriteCycle_PreservesData()
    {
        byte[] originalData = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };

        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(originalData);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);
        byte[] readData = reader.ReadBytes(originalData.Length);

        Assert.Equal(originalData, readData);
    }

    [Fact]
    public void BinaryEndianConverter_ToInt64()
    {
        long value = 0x123456789ABCDEF0;

        byte[] buffer = new byte[8];
        using (var stream = new MemoryStream(buffer))
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(value, EndianType.LITTLE);
        }

        long result = BinaryEndianConverter.ToInt64(buffer, EndianType.LITTLE);
        Assert.Equal(value, result);
    }

    [Fact]
    public void BinaryEndianConverter_WithOffset()
    {
        int value = 0x12345678;

        byte[] buffer = new byte[20];
        int offset = 10;

        using (var stream = new MemoryStream(buffer))
        using (var writer = new EndianStreamWriter(stream, true))
        {
            stream.Position = offset;
            writer.Write(value, EndianType.BIG);
        }

        int result = BinaryEndianConverter.ToInt32(buffer, offset, EndianType.BIG);
        Assert.Equal(value, result);
    }

    [Fact]
    public void MultipleReadersOnSameStream()
    {
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(100);
            writer.Write(200);
            writer.Write(300);
        }

        stream.Position = 0;
        using var reader1 = new EndianStreamReader(stream, null, 0, System.Text.Encoding.UTF8);
        Assert.Equal(100, reader1.ReadInt32());

        using var reader2 = new EndianStreamReader(stream, null, 0, System.Text.Encoding.UTF8);
        Assert.Equal(200, reader2.ReadInt32());
        Assert.Equal(300, reader2.ReadInt32());
    }

    [Fact]
    public void KeepStreamOpen_True_StreamNotDisposed()
    {
        var stream = new MemoryStream();

        using (var writer = new EndianStreamWriter(stream, keepStreamOpen: true))
        {
            writer.Write(100);
        }

        // Stream should still be open
        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);
        Assert.Equal(100, reader.ReadInt32());
    }

    [Fact]
    public void KeepStreamOpen_False_StreamDisposed()
    {
        var stream = new MemoryStream();

        using (var writer = new EndianStreamWriter(stream, keepStreamOpen: false))
        {
            writer.Write(100);
        }

        // Stream should be disposed
        Assert.Throws<ObjectDisposedException>(() => stream.Position = 0);
    }
}
