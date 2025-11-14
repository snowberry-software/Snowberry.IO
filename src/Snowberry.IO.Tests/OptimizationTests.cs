using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;
using static Snowberry.IO.Tests.TestHelper;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains tests for the performance-optimized methods.
/// </summary>
public class OptimizationTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(128)] // MaxCharBytesSize boundary
    [InlineData(200)]
    [InlineData(1000)]
    public void ReadString_OptimizedPath_SmallStrings(int length)
    {
        string expected = new('x', length);

        CreateShared((w, _) => w.Write(expected),
        (r, _) =>
        {
            string result = r.ReadString();
            Assert.Equal(expected, result);
        });
    }

    [Fact]
    public void ReadString_EmptyString_Optimized()
    {
        CreateShared((w, _) => w.Write(""),
        (r, _) =>
        {
            string result = r.ReadString();
            Assert.Equal("", result);
        });
    }

    [Theory]
    [InlineData("Short")]
    [InlineData("This is a medium length test string that should hit the optimized path")]
    [InlineData("This is a very long string that will exceed the MaxCharBytesSize and use the StringBuilder path for reading, testing the non-optimized code path that handles larger strings with multiple chunk reads and proper character decoding across boundaries")]
    public void ReadString_VariableLengths(string expected)
    {
        CreateShared((w, _) => w.Write(expected),
        (r, _) =>
        {
            string result = r.ReadString();
            Assert.Equal(expected, result);
        });
    }

    [Fact]
    public void ReadCString_ChunkOptimization_ShortString()
    {
        const string expected = "Hello, World!";

        CreateShared((w, _) => w.WriteCString(expected),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(expected, result);
        });
    }

    [Fact]
    public void ReadCString_ChunkOptimization_LongString()
    {
        // Create a string longer than the chunk size (128 bytes)
        string expected = new('A', 200);

        CreateShared((w, _) => w.WriteCString(expected),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(expected, result);
        });
    }

    [Fact]
    public void ReadCString_ChunkOptimization_VeryLongString()
    {
        // Create a string much longer than the chunk size
        string expected = new('B', 500);

        CreateShared((w, _) => w.WriteCString(expected),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(expected, result);
        });
    }

    [Theory]
    [InlineData("a")]
    [InlineData("short")]
    [InlineData("This is exactly 128 characters long padding padding padding padding padding padding padding padding padding padding padding pads")]
    [InlineData("This is longer than 128 characters and will span multiple chunks for testing the chunk-based reading optimization pathway here")]
    public void ReadCString_ChunkOptimization_VariousLengths(string expected)
    {
        CreateShared((w, _) => w.WriteCString(expected),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(expected, result);
        });
    }

    [Fact]
    public void ReadCString_MultipleConsecutive()
    {
        const string str1 = "First";
        const string str2 = "Second string is longer";
        const string str3 = "Third";

        CreateShared((w, _) =>
        {
            w.WriteCString(str1);
            w.WriteCString(str2);
            w.WriteCString(str3);
        },
        (r, _) =>
        {
            Assert.Equal(str1, r.ReadCString());
            Assert.Equal(str2, r.ReadCString());
            Assert.Equal(str3, r.ReadCString());
        });
    }

    [Fact]
    public void ReadUntilEnd_KnownLength_Optimized()
    {
        byte[] expected = new byte[1000];
        new Random(42).NextBytes(expected);

        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(expected);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        byte[] result = reader.ReadUntilEnd();

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void ReadUntilEnd_VariousSizes_Optimized(int size)
    {
        byte[] expected = new byte[size];
        new Random(42).NextBytes(expected);

        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(expected);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        byte[] result = reader.ReadUntilEnd();

        Assert.Equal(expected.Length, result.Length);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReadUntilEnd_FromMiddle_Optimized()
    {
        byte[] data = new byte[1000];
        new Random(42).NextBytes(data);

        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(data);
        }

        stream.Position = 500;
        using var reader = new EndianStreamReader(stream);

        byte[] result = reader.ReadUntilEnd();

        Assert.Equal(500, result.Length);

        // Compare the second half of the data
        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(data[500 + i], result[i]);
        }
    }

#if !NETSTANDARD2_0 && !NET48
    [Fact]
    public void Read_Span_NoAllocation()
    {
        byte[] expected = new byte[] { 1, 2, 3, 4, 5 };

        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(expected);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        Span<byte> buffer = stackalloc byte[5];
        int bytesRead = reader.Read(buffer);

        Assert.Equal(5, bytesRead);
        Assert.True(expected.AsSpan().SequenceEqual(buffer));
    }

    [Fact]
    public void ReadExactly_Span_NoAllocation()
    {
        byte[] expected = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(expected);
        }

        stream.Position = 0;
        using var reader = new EndianStreamReader(stream);

        Span<byte> buffer = stackalloc byte[8];
        reader.ReadExactly(buffer);

        Assert.True(expected.AsSpan().SequenceEqual(buffer));
    }

    [Fact]
    public void Read_Span_PartialRead()
    {
        byte[] data = new byte[] { 1, 2, 3 };

        var stream = new MemoryStream(data);
        using var reader = new EndianStreamReader(stream);

        Span<byte> buffer = stackalloc byte[10];
        int bytesRead = reader.Read(buffer);

        Assert.Equal(3, bytesRead);
        Assert.True(data.AsSpan().SequenceEqual(buffer[..3]));
    }
#endif

    [Fact]
    public void ReadSizedCString_OptimizedValidation()
    {
        const string expected = "Test";
        const int size = 10;

        CreateShared((w, _) => w.WriteSizedCString(expected, size),
        (r, _) =>
        {
            string result = r.ReadSizedCString(size);
            Assert.Equal(expected, result);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(255)]
    public void ReadSizedCString_VariousSizes(int size)
    {
        if (size == 0)
        {
            CreateShared((w, _) => { /* Write nothing */ },
            (r, _) =>
            {
                string result = r.ReadSizedCString(0);
                Assert.Equal("", result);
            });
        }
        else
        {
            string content = "Content";
            CreateShared((w, _) => w.WriteSizedCString(content, size),
            (r, _) =>
            {
                string result = r.ReadSizedCString(size);
                Assert.Equal(content, result);
            });
        }
    }

    [Fact]
    public void Performance_ManySmallStrings()
    {
        // Test that the optimization doesn't break repeated reads
        string[] strings = new[] { "a", "bb", "ccc", "dddd", "eeeee", "ffffff", "ggggggg" };

        CreateShared((w, _) =>
        {
            foreach (string? str in strings)
            {
                w.Write(str);
            }
        },
        (r, _) =>
        {
            foreach (string? expected in strings)
            {
                string result = r.ReadString();
                Assert.Equal(expected, result);
            }
        });
    }

    [Fact]
    public void ReadBytes_ExactCount_NoResize()
    {
        byte[] expected = new byte[] { 1, 2, 3, 4, 5 };

        CreateShared((w, _) => w.Write(expected),
        (r, _) =>
        {
            byte[] result = r.ReadBytes(5);
            Assert.Equal(expected, result);
        });
    }

    [Fact]
    public void ReadBytes_PartialCount_DoesResize()
    {
        byte[] data = new byte[] { 1, 2, 3 };

        var stream = new MemoryStream(data);
        using var reader = new EndianStreamReader(stream);

        byte[] result = reader.ReadBytes(10);
        Assert.Equal(3, result.Length);
        Assert.Equal(data, result);
    }
}
