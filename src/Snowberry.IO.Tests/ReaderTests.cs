using Snowberry.IO.Common;

using Snowberry.IO.Extensions;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;

namespace Snowberry.IO.Tests;

public class ReaderTests
{
    public static readonly Random Random = new();

    [Theory]
    [InlineData(0, BaseEndianReader.MinBufferSize)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(1000, 1000)]
    [InlineData(BaseEndianReader.MinBufferSize - 1, BaseEndianReader.MinBufferSize)]
    private void EnsureBufferSize(int setValue, int expectedValue)
    {
        using var reader = new EndianStreamReader(new MemoryStream());
        reader.EnsureBufferSize(setValue);

        Assert.Equal(expectedValue, reader.Buffer.Length);
    }

    [Fact]
    private void EnsureBufferSize_MultipleExpansions()
    {
        using var reader = new EndianStreamReader(new MemoryStream(), null, 16);

        reader.EnsureBufferSize(32);
        Assert.True(reader.Buffer.Length >= 32);

        reader.EnsureBufferSize(64);
        Assert.True(reader.Buffer.Length >= 64);

        reader.EnsureBufferSize(128);
        Assert.True(reader.Buffer.Length >= 128);
    }

    [Fact]
    private void ReadUntilEnd()
    {
        var memory = new MemoryStream(
        [
            33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,
            33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82
        ]);

        using var reader = new EndianStreamReader(memory);
        byte[] data = reader.ReadUntilEnd();

        Assert.Equal(data.Length, memory.Length);
    }

    [Fact]
    private void ReadUntilEnd_EmptyStream()
    {
        var memory = new MemoryStream();
        using var reader = new EndianStreamReader(memory);
        byte[] data = reader.ReadUntilEnd();

        Assert.Empty(data);
    }

    [Fact]
    private void ReadUntilEnd_FromMiddle()
    {
        var memory = new MemoryStream(new byte[100]);
        using var reader = new EndianStreamReader(memory);

        reader.Position = 50;
        byte[] data = reader.ReadUntilEnd();

        Assert.Equal(50, data.Length);
    }

    [Fact]
    private void ReadUntilEnd_WithCustomBufferSize()
    {
        var memory = new MemoryStream(new byte[1000]);
        using var reader = new EndianStreamReader(memory);

        byte[] data = reader.ReadUntilEnd(maxBufferSize: 100);

        Assert.Equal(1000, data.Length);
    }

    [Fact]
    private void RegionViewBasic()
    {
        using var reader = new EndianStreamReader(new MemoryStream(new byte[200]));

        reader.Position = 100;
        reader.EnableRegionView(100..100);
        Assert.Equal(0, reader.Position);
        reader.Position += 50;
        Assert.Equal(50, reader.Position);
        reader.Position += 50;
        Assert.Equal(100, reader.Length);
        reader.DisableRegionView();
        Assert.Equal(200, reader.Position);
    }

    [Fact]
    private void RegionView_FullStream()
    {
        using var reader = new EndianStreamReader(new MemoryStream(new byte[100]));

        reader.EnableRegionView(0..100);

        Assert.Equal(0, reader.Position);
        Assert.Equal(100, reader.Length);
        Assert.Equal(0, reader.ActualPosition);
        Assert.Equal(100, reader.ActualLength);
    }

    [Fact]
    private void RegionViewEnhanced()
    {
        const string testData = "someTestString";

        var memA = new MemoryStream();

        using var writer = new EndianStreamWriter(memA, false);

        writer.Write(new byte[20]);
        writer.WriteCString(testData);
        writer.WriteCString(testData + "28");
        writer.WriteSizedCString(testData, 40);

        writer.BaseStream.Position = 0;

        using var reader = new EndianStreamReader(memA);

        reader.EnableRegionView(20..((int)reader.Length - 20));

        // Test offset adjustment
        Assert.Equal(20, reader.ActualPosition);
        Assert.Equal(0, reader.Position);

        // We skipped the first 20 bytes by using a region view.
        int length = (testData.Length * 3)
                + 2 // CString zero characters
                + 2 // "28"
                + (40 - testData.Length);
        Assert.Equal(length, reader.Length);

        Assert.Equal(testData, reader.ReadCString());
        Assert.Equal(testData + "28", reader.ReadCString());
        Assert.Equal(testData, reader.ReadSizedCString(40));

        Assert.Equal(reader.Length, reader.Position);

        // Just some assignment tests
        long pos = reader.Position;
        reader.Position = 0;

        Assert.Equal(20, reader.ActualPosition);
        Assert.Equal(0, reader.Position);

        reader.Position = pos;
        Assert.Equal(pos, reader.Position);

        var memB = new MemoryStream();
        reader.CopyTo(memB);
    }

    [Fact]
    private void RegionView_NestedNotSupported()
    {
        using var reader = new EndianStreamReader(new MemoryStream(new byte[200]));

        reader.EnableRegionView(50..150);
        Assert.True(reader.IsRegionViewEnabled);

        // Enabling another region view should replace the previous one
        reader.EnableRegionView(60..140);
        Assert.True(reader.IsRegionViewEnabled);
    }

    [Fact]
    private void RegionView_DisableWhenNotEnabled()
    {
        using var reader = new EndianStreamReader(new MemoryStream(new byte[100]));

        Assert.False(reader.IsRegionViewEnabled);
        reader.DisableRegionView(); // Should not throw
        Assert.False(reader.IsRegionViewEnabled);
    }

    [Fact]
    private void ReadWithOffsets()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        byte[] hashBuffer = new byte[Sha1.StructSize];
        Random.NextBytes(hashBuffer);

        var hash = new Sha1(hashBuffer);
        writer.Write(30, EndianType.BIG)
              .Write(30L, EndianType.BIG)
              .Write(30F, EndianType.BIG)
              .Write(30u, EndianType.BIG)
              .Write(30d, EndianType.BIG)
              .Write(hash);

        writer.BaseStream.Position = 0;

        const int size = 48;
        using var reader = new EndianStreamReader(stream, null, size);

        reader.ReadInInternalBuffer(size, 0);

        int offset = 0;
        Assert.Equal(30, reader.ReadInt32At(EndianType.BIG, offset));
        offset += 4;
        Assert.Equal(30L, reader.ReadInt64At(EndianType.BIG, offset));
        offset += 8;
        Assert.Equal(30F, reader.ReadFloatAt(EndianType.BIG, offset));
        offset += 4;
        Assert.Equal(30u, reader.ReadUInt32At(EndianType.BIG, offset));
        offset += 4;
        Assert.Equal(30d, reader.ReadDoubleAt(EndianType.BIG, offset));
        offset += 8;
        Assert.Equal(hash, reader.ReadSha1At(offset));
    }

    [Fact]
    private void ReadWithOffsets_VariousTypes()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write((short)100, EndianType.LITTLE)
              .Write((ushort)200, EndianType.LITTLE)
              .Write(Guid.NewGuid(), EndianType.LITTLE);

        writer.BaseStream.Position = 0;

        using var reader = new EndianStreamReader(stream, null, 100);
        reader.ReadInInternalBuffer(20, 0);

        Assert.Equal(100, reader.ReadInt16At(EndianType.LITTLE, 0));
        Assert.Equal((ushort)200, reader.ReadUInt16At(EndianType.LITTLE, 2));
    }

    [Fact]
    private void ReadUntilEndOfStreamException()
    {
        var memory = new MemoryStream(
        [
            33, 92, 82, 33
        ]);

        using var reader = new EndianStreamReader(memory);
        byte[] data = reader.ReadUntilEnd();

        Assert.Throws<EndOfStreamException>(() => reader.ReadInt16());
    }

    [Fact]
    private void ReadByteSafe_MultipleReads()
    {
        var memory = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(memory);

        Assert.Equal(1, reader.ReadByteSafe());
        Assert.Equal(2, reader.ReadByteSafe());
        Assert.Equal(3, reader.ReadByteSafe());
        Assert.Equal(-1, reader.ReadByteSafe());
        Assert.Equal(-1, reader.ReadByteSafe());
    }

    [Fact]
    private void Read_IntoBuffer_PartialRead()
    {
        var memory = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(memory);

        byte[] buffer = new byte[10];
        int bytesRead = reader.Read(buffer, 0, 10);

        Assert.Equal(3, bytesRead);
        Assert.Equal(1, buffer[0]);
        Assert.Equal(2, buffer[1]);
        Assert.Equal(3, buffer[2]);
    }

    [Fact]
    private void ReadExactly_ThrowsIfNotEnoughData()
    {
        var memory = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(memory);

        byte[] buffer = new byte[10];
        Assert.Throws<EndOfStreamException>(() => reader.ReadExactly(buffer, 0, 10));
    }

    [Fact]
    private void ReadExactly_Success()
    {
        var memory = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        using var reader = new EndianStreamReader(memory);

        byte[] buffer = new byte[5];
        reader.ReadExactly(buffer, 0, 5);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, buffer);
    }

    [Fact]
    private void CopyTo_EntireStream()
    {
        var source = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var destination = new MemoryStream();

        using var reader = new EndianStreamReader(source);
        reader.CopyTo(destination);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, destination.ToArray());
    }

    [Fact]
    private void CopyTo_PartialStream()
    {
        var source = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var destination = new MemoryStream();

        using var reader = new EndianStreamReader(source);
        reader.CopyTo(destination, 5);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, destination.ToArray());
    }

    [Fact]
    private void CopyTo_WithCustomBufferSize()
    {
        var source = new MemoryStream(new byte[1000]);
        var destination = new MemoryStream();

        using var reader = new EndianStreamReader(source);
        reader.CopyTo(destination, 1000, bufferSize: 100);

        Assert.Equal(1000, destination.Length);
    }

    [Fact]
    private void Disposed_ThrowsObjectDisposedException()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var reader = new EndianStreamReader(stream);
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => reader.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => _ = reader.Position);
    }

    [Fact]
    private void Disposed_Property_ReturnsTrue()
    {
        var stream = new MemoryStream();
        var reader = new EndianStreamReader(stream);

        Assert.False(reader.Disposed);
        reader.Dispose();
        Assert.True(reader.Disposed);
    }

    [Fact]
    private void Buffer_Property_ReturnsInternalBuffer()
    {
        using var reader = new EndianStreamReader(new MemoryStream(), null, 100);

        Assert.NotNull(reader.Buffer);
        Assert.True(reader.Buffer.Length >= 100);
    }

    [Fact]
    private void Encoding_Property_ReturnsCorrectEncoding()
    {
        using var reader = new EndianStreamReader(new MemoryStream(), null, 0, System.Text.Encoding.UTF8);

        Assert.Equal(System.Text.Encoding.UTF8, reader.Encoding);
    }

    [Fact]
    private void KeepStreamOpen_Reader()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        using (var reader = new EndianStreamReader(stream))
        {
            reader.KeepStreamOpen = true;
            reader.ReadByte();
        }

        // Stream should still be usable
        Assert.Equal(1, stream.Position);
        stream.Position = 0;
    }

    [Fact]
    private void ReadAlignment_AtBoundary()
    {
        var stream = new MemoryStream(new byte[100]);
        using var reader = new EndianStreamReader(stream);

        reader.Position = 16; // Already aligned to 16
        reader.ReadAlignment(16);
        Assert.Equal(16, reader.Position); // Should not move
    }

    [Fact]
    private void ReadAlignment_NotAtBoundary()
    {
        var stream = new MemoryStream(new byte[100]);
        using var reader = new EndianStreamReader(stream);

        reader.Position = 17;
        reader.ReadAlignment(16);
        Assert.Equal(32, reader.Position); // Should jump to next 16-byte boundary
    }
}
