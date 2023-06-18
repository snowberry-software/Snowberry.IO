using Snowberry.IO.Extensions;
using Snowberry.IO.Reader;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Writer;
using Xunit;

namespace Snowberry.IO.Tests;

public class ReaderTests
{
    public static Random Random = new();

    [Theory]
    [InlineData(0, BaseEndianReader.MinBufferSize)]
    [InlineData(50, 50)]
    private void EnsureBufferSize(int setValue, int expectedValue)
    {
        using var reader = new EndianStreamReader(new MemoryStream());
        reader.EnsureBufferSize(setValue);

        Assert.Equal(expectedValue, reader.Buffer.Length);
    }

    [Fact]
    private void ReadUntilEnd()
    {
        var memory = new MemoryStream(new byte[]
        {
            33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,
            33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82,33, 92, 82
        });

        using var reader = new EndianStreamReader(memory);
        byte[] data = reader.ReadUntilEnd();

        Assert.Equal(data.Length, memory.Length);
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
        Assert.Equal(30L, reader.ReadLongAt(EndianType.BIG, offset));
        offset += 8;
        Assert.Equal(30F, reader.ReadFloatAt(EndianType.BIG, offset));
        offset += 4;
        Assert.Equal(30u, reader.ReadUInt32At(EndianType.BIG, offset));
        offset += 4;
        Assert.Equal(30d, reader.ReadDoubleAt(EndianType.BIG, offset));
        offset += 8;
        Assert.Equal(hash, reader.ReadSha1At(offset));
    }
}
