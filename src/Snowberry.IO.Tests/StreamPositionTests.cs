using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains tests for stream position and seeking behavior.
/// </summary>
public class StreamPositionTests
{
    [Fact]
    public void Position_AfterWriting_MatchesDataSize()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write(100);
        Assert.Equal(4, writer.Position);

        writer.Write(200L);
        Assert.Equal(12, writer.Position);

        writer.Write((short)300);
        Assert.Equal(14, writer.Position);
    }

    [Fact]
    public void Position_AfterReading_MatchesDataSize()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write(100);
        writer.Write(200L);
        writer.Write((short)300);
        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        Assert.Equal(0, reader.Position);

        reader.ReadInt32();
        Assert.Equal(4, reader.Position);

        reader.ReadInt64();
        Assert.Equal(12, reader.Position);

        reader.ReadInt16();
        Assert.Equal(14, reader.Position);
    }

    [Fact]
    public void Position_SetAndGet_WorksCorrectly()
    {
        var stream = new MemoryStream(new byte[100]);
        using var reader = new EndianStreamReader(stream);

        reader.Position = 50;
        Assert.Equal(50, reader.Position);

        reader.Position = 0;
        Assert.Equal(0, reader.Position);

        reader.Position = 99;
        Assert.Equal(99, reader.Position);
    }

    [Fact]
    public void Length_MatchesStreamLength()
    {
        byte[] data = new byte[100];
        var stream = new MemoryStream(data);
        using var reader = new EndianStreamReader(stream);

        Assert.Equal(100, reader.Length);
    }

    [Fact]
    public void Length_AfterWriting_UpdatesCorrectly()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Equal(0, writer.Length);

        writer.Write(100);
        Assert.Equal(4, writer.Length);

        writer.Write(new byte[10]);
        Assert.Equal(14, writer.Length);
    }

    [Fact]
    public void Seek_FromBeginning()
    {
        var stream = new MemoryStream(new byte[100]);
        using var reader = new EndianStreamReader(stream);

        reader.Position = 0;
        reader.ReadInt32();

        reader.Position = 0; // Seek back to beginning
        Assert.Equal(0, reader.Position);
    }

    [Fact]
    public void Seek_ToMiddle()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        for (int i = 0; i < 10; i++)
            writer.Write(i);

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        reader.Position = 20; // Skip to 5th integer
        int value = reader.ReadInt32();
        Assert.Equal(5, value);
    }

    [Fact]
    public void CanReadData_TrueWhenDataAvailable()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(stream);

        Assert.True(reader.CanReadData);
        reader.ReadByte();
        Assert.True(reader.CanReadData);
        reader.ReadByte();
        Assert.True(reader.CanReadData);
        reader.ReadByte();
        Assert.False(reader.CanReadData);
    }

    [Fact]
    public void CanReadData_FalseWhenAtEnd()
    {
        var stream = new MemoryStream(new byte[] { 1 });
        using var reader = new EndianStreamReader(stream);

        reader.ReadByte();
        Assert.False(reader.CanReadData);
    }

    [Fact]
    public void CanReadData_FalseWhenEmptyStream()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.CanReadData);
    }

    [Fact]
    public void Position_WriterAndReader_SameStream()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write(100);
        writer.Write(200);

        long writerPosition = writer.Position;
        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        reader.ReadInt32();
        reader.ReadInt32();

        Assert.Equal(writerPosition, reader.Position);
    }

    [Fact]
    public void ReadUntilEnd_AdjustsPosition()
    {
        byte[] data = new byte[100];
        new Random(42).NextBytes(data);
        var stream = new MemoryStream(data);

        using var reader = new EndianStreamReader(stream);
        reader.Position = 50;

        byte[] remaining = reader.ReadUntilEnd();
        Assert.Equal(50, remaining.Length);
        Assert.Equal(100, reader.Position);
    }

    [Fact]
    public void CopyTo_AdjustsPosition()
    {
        var source = new MemoryStream(new byte[100]);
        var destination = new MemoryStream();

        using var reader = new EndianStreamReader(source);
        reader.CopyTo(destination, 50);

        Assert.Equal(50, reader.Position);
        Assert.Equal(50, destination.Length);
    }

    [Fact]
    public void WriteAndSeekBack()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write(100);
        long position1 = writer.Position;

        writer.Write(200);
        long position2 = writer.Position;

        // Seek back and overwrite
        writer.Position = position1;
        writer.Write(300);

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        Assert.Equal(100, reader.ReadInt32());
        Assert.Equal(300, reader.ReadInt32());
    }

    [Fact]
    public void MultipleReads_PositionTracking()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write((byte)1);
        writer.Write((short)2);
        writer.Write(3);
        writer.Write(4L);
        writer.Write(5.0f);
        writer.Write(6.0);

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        Assert.Equal(0, reader.Position);
        reader.ReadByte();
        Assert.Equal(1, reader.Position);
        reader.ReadInt16();
        Assert.Equal(3, reader.Position);
        reader.ReadInt32();
        Assert.Equal(7, reader.Position);
        reader.ReadInt64();
        Assert.Equal(15, reader.Position);
        reader.ReadFloat();
        Assert.Equal(19, reader.Position);
        reader.ReadDouble();
        Assert.Equal(27, reader.Position);
    }

    [Fact]
    public void ActualPosition_MatchesPosition_NoRegionView()
    {
        var stream = new MemoryStream(new byte[100]);
        using var reader = new EndianStreamReader(stream);

        reader.Position = 50;
        Assert.Equal(50, reader.Position);
        Assert.Equal(50, reader.ActualPosition);
    }

    [Fact]
    public void ActualLength_MatchesLength_NoRegionView()
    {
        var stream = new MemoryStream(new byte[100]);
        using var reader = new EndianStreamReader(stream);

        Assert.Equal(100, reader.Length);
        Assert.Equal(100, reader.ActualLength);
    }

    [Fact]
    public void WritePadding_AdjustsPosition()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write((byte)0xFF);
        Assert.Equal(1, writer.Position);

        writer.WritePadding(4);
        Assert.Equal(4, writer.Position);
    }

    [Fact]
    public void ReadAlignment_AdjustsPosition()
    {
        var stream = new MemoryStream(new byte[100]);
        using var reader = new EndianStreamReader(stream);

        reader.ReadByte();
        Assert.Equal(1, reader.Position);

        reader.ReadAlignment(4);
        Assert.Equal(4, reader.Position);
    }
}
