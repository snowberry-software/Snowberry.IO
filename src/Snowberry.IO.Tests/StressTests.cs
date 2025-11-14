using Snowberry.IO.Common;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains stress tests and performance edge cases.
/// </summary>
public class StressTests
{
    [Theory]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(100000)]
    public void WriteAndRead_ManyIntegers(int count)
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        for (int i = 0; i < count; i++)
        {
            writer.Write(i);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, reader.ReadInt32());
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public void WriteAndRead_ManyStrings(int count)
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        for (int i = 0; i < count; i++)
        {
            writer.WriteCString($"String_{i}");
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal($"String_{i}", reader.ReadCString());
        }
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    public void WriteAndRead_MixedTypes(int iterations)
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        for (int i = 0; i < iterations; i++)
        {
            writer.Write((byte)i);
            writer.Write((short)i);
            writer.Write(i);
            writer.Write((long)i);
            writer.Write((float)i);
            writer.Write((double)i);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        for (int i = 0; i < iterations; i++)
        {
            Assert.Equal((byte)i, reader.ReadByte());
            Assert.Equal((short)i, reader.ReadInt16());
            Assert.Equal(i, reader.ReadInt32());
            Assert.Equal(i, reader.ReadInt64());
            Assert.Equal(i, reader.ReadFloat());
            Assert.Equal(i, reader.ReadDouble());
        }
    }

    [Fact]
    public void VeryLargeByteArray()
    {
        int size = 10 * 1024 * 1024; // 10 MB
        byte[] data = new byte[size];
        new Random(42).NextBytes(data);

        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write(data);

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);
        byte[] result = reader.ReadBytes(size);

        Assert.Equal(data.Length, result.Length);
        Assert.Equal(data, result);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public void AlternatingEndianTypes(int iterations)
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        for (int i = 0; i < iterations; i++)
        {
            if (i % 2 == 0)
            {
                writer.Write(i, EndianType.LITTLE);
            }
            else
            {
                writer.Write(i, EndianType.BIG);
            }
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        for (int i = 0; i < iterations; i++)
        {
            if (i % 2 == 0)
            {
                Assert.Equal(i, reader.ReadInt32(EndianType.LITTLE));
            }
            else
            {
                Assert.Equal(i, reader.ReadInt32(EndianType.BIG));
            }
        }
    }

    [Fact]
    public void ManySmallReads()
    {
        byte[] data = new byte[10000];
        new Random(42).NextBytes(data);
        var stream = new MemoryStream(data);

        using var reader = new EndianStreamReader(stream);

        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(data[i], reader.ReadByte());
        }
    }

    [Fact]
    public void ManySmallWrites()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        var expected = new List<byte>();
        var random = new Random(42);

        for (int i = 0; i < 10000; i++)
        {
            byte value = (byte)random.Next(256);
            expected.Add(value);
            writer.Write(value);
        }

        Assert.Equal(expected.ToArray(), stream.ToArray());
    }

    [Fact]
    public void DeepPositionChanges()
    {
        var stream = new MemoryStream(new byte[10000]);
        using var reader = new EndianStreamReader(stream);

        for (int i = 0; i < 1000; i++)
        {
            reader.Position = i * 10;
            Assert.Equal(i * 10, reader.Position);
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void WriteReadCycle_MultipleIterations(int cycles)
    {
        for (int cycle = 0; cycle < cycles; cycle++)
        {
            var stream = new MemoryStream();
            using var writer = new EndianStreamWriter(stream, true);
            writer.Write(cycle);

            stream.Position = 0;
            using var reader = new EndianStreamReader(stream);
            Assert.Equal(cycle, reader.ReadInt32());
        }
    }

    [Fact]
    public void LargeString_SizePrefixed()
    {
        string largeString = new('x', 1000000); // 1 million characters

        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write(largeString);

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);
        string result = reader.ReadString();

        Assert.Equal(largeString.Length, result.Length);
        Assert.Equal(largeString, result);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    public void ManyGuids(int count)
    {
        var guids = new List<Guid>();
        for (int i = 0; i < count; i++)
        {
            guids.Add(Guid.NewGuid());
        }

        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        foreach (var guid in guids)
        {
            writer.Write(guid);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(guids[i], reader.ReadGuid());
        }
    }

    [Fact]
    public void BufferResize_MultipleExpansions()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream, null, 16);

        Assert.Equal(BaseEndianReader.MinBufferSize, reader.Buffer.Length);

        reader.EnsureBufferSize(32);
        Assert.Equal(32, reader.Buffer.Length);

        reader.EnsureBufferSize(64);
        Assert.Equal(64, reader.Buffer.Length);

        reader.EnsureBufferSize(128);
        Assert.Equal(128, reader.Buffer.Length);

        reader.EnsureBufferSize(256);
        Assert.Equal(256, reader.Buffer.Length);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void Many7BitEncodedIntegers(int count)
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        var values = new List<int>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            int value = random.Next(int.MaxValue);
            values.Add(value);
            writer.Write7BitEncodedInt(value);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(values[i], reader.Read7BitEncodedInt());
        }
    }

    [Fact]
    public void ReadUntilEnd_LargeStream()
    {
        int size = 5 * 1024 * 1024; // 5 MB
        byte[] data = new byte[size];
        new Random(42).NextBytes(data);

        var stream = new MemoryStream(data);
        using var reader = new EndianStreamReader(stream);

        byte[] result = reader.ReadUntilEnd();

        Assert.Equal(data.Length, result.Length);
        Assert.Equal(data, result);
    }

    [Fact]
    public void CopyTo_LargeStream()
    {
        int size = 5 * 1024 * 1024; // 5 MB
        byte[] data = new byte[size];
        new Random(42).NextBytes(data);

        var source = new MemoryStream(data);
        var destination = new MemoryStream();

        using var reader = new EndianStreamReader(source);
        reader.CopyTo(destination);

        Assert.Equal(data.Length, destination.Length);
        Assert.Equal(data, destination.ToArray());
    }
}
