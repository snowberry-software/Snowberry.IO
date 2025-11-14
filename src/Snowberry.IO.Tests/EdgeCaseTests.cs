using Snowberry.IO.Common;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;
using static Snowberry.IO.Tests.TestHelper;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains edge case tests for binary reading and writing operations.
/// </summary>
public class EdgeCaseTests
{
    [Fact]
    public void EmptyStream_ReadByte_ThrowsEndOfStreamException()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<EndOfStreamException>(() => reader.ReadByte());
    }

    [Fact]
    public void EmptyStream_ReadSByte_ThrowsEndOfStreamException()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<EndOfStreamException>(() => reader.ReadSByte());
    }

    [Fact]
    public void EmptyStream_ReadByteSafe_ReturnsNegativeOne()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        Assert.Equal(-1, reader.ReadByteSafe());
    }

    [Fact]
    public void EmptyStream_ReadBytes_ReturnsEmptyArray()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        byte[] result = reader.ReadBytes(10);
        Assert.Empty(result);
    }

    [Fact]
    public void ReadBytes_ZeroCount_ReturnsEmptyArray()
    {
        CreateShared((w, _) => w.Write(new byte[] { 1, 2, 3 }),
        (r, _) =>
        {
            byte[] result = r.ReadBytes(0);
            Assert.Empty(result);

            r.Position = r.Length;
        });
    }

    [Fact]
    public void ReadBytes_PartialData_ReturnsAvailableBytes()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(stream);

        byte[] result = reader.ReadBytes(10);
        Assert.Equal(3, result.Length);
    }

    [Fact]
    public void EmptyString_CString_RoundTrip()
    {
        CreateShared((w, _) => w.WriteCString(""),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal("", result);
        });
    }

    [Fact]
    public void EmptyString_SizedCString_RoundTrip()
    {
        CreateShared((w, _) => w.WriteSizedCString("", 10),
        (r, _) =>
        {
            string result = r.ReadSizedCString(10);
            Assert.Equal("", result);
        });
    }

    [Fact]
    public void EmptyString_SizePrefixed_RoundTrip()
    {
        CreateShared((w, _) => w.Write(""),
        (r, _) =>
        {
            string result = r.ReadString();
            Assert.Equal("", result);
        });
    }

    [Fact]
    public void VeryLongString_CString_RoundTrip()
    {
        string longString = new('x', 10000);
        CreateShared((w, _) => w.WriteCString(longString),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(longString, result);
        });
    }

    [Fact]
    public void VeryLongString_SizePrefixed_RoundTrip()
    {
        string longString = new('y', 10000);
        CreateShared((w, _) => w.Write(longString),
        (r, _) =>
        {
            string result = r.ReadString();
            Assert.Equal(longString, result);
        });
    }

    [Fact]
    public void SpecialCharacters_AllInOne_RoundTrip()
    {
        string special = "Hello\r\n\t\0World\u0000Test";
        CreateShared((w, _) =>
        {
            w.WriteSizedCString(special, 50);
        },
        (r, _) =>
        {
            string result = r.ReadSizedCString(50);
            Assert.StartsWith("Hello", result);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(2097151)]
    public void SevenBitEncodedInt_BoundaryValues(int value)
    {
        CreateShared((w, _) => w.Write7BitEncodedInt(value),
        (r, _) =>
        {
            int result = r.Read7BitEncodedInt();
            Assert.Equal(value, result);
        });
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(16383L)]
    [InlineData(16384L)]
    [InlineData(2097151L)]
    [InlineData(2097152L)]
    [InlineData(268435455L)]
    public void SevenBitEncodedLong_BoundaryValues(long value)
    {
        CreateShared((w, _) => w.Write7BitEncodedInt64(value),
        (r, _) =>
        {
            long result = r.Read7BitEncodedLong();
            Assert.Equal(value, result);
        });
    }

    [Fact]
    public void NegativeSevenBitEncodedInt_RoundTrip()
    {
        CreateShared((w, _) => w.Write7BitEncodedInt(-1),
        (r, _) =>
        {
            int result = r.Read7BitEncodedInt();
            Assert.Equal(-1, result);
        });
    }

    [Fact]
    public void NegativeSevenBitEncodedLong_RoundTrip()
    {
        CreateShared((w, _) => w.Write7BitEncodedInt64(-1L),
        (r, _) =>
        {
            long result = r.Read7BitEncodedLong();
            Assert.Equal(-1L, result);
        });
    }

    [Fact]
    public void ZeroGuid_RoundTrip()
    {
        var guid = Guid.Empty;
        CreateShared((w, _) =>
        {
            w.Write(guid);
            w.Write(guid, EndianType.BIG);
        },
        (r, _) =>
        {
            Assert.Equal(guid, r.ReadGuid());
            Assert.Equal(guid, r.ReadGuid(EndianType.BIG));
        });
    }

    [Fact]
    public void MaxGuid_RoundTrip()
    {
        var guid = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff");
        CreateShared((w, _) =>
        {
            w.Write(guid);
            w.Write(guid, EndianType.BIG);
        },
        (r, _) =>
        {
            Assert.Equal(guid, r.ReadGuid());
            Assert.Equal(guid, r.ReadGuid(EndianType.BIG));
        });
    }

    [Theory]
    [InlineData(float.Epsilon)]
    [InlineData(-float.Epsilon)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.NaN)]
    public void Float_SpecialValues(float value)
    {
        CreateShared((w, _) =>
        {
            w.Write(value);
            w.Write(value, EndianType.BIG);
        },
        (r, _) =>
        {
            float littleEndian = r.ReadFloat();
            float bigEndian = r.ReadFloat(EndianType.BIG);

            if (float.IsNaN(value))
            {
                Assert.True(float.IsNaN(littleEndian));
                Assert.True(float.IsNaN(bigEndian));
            }
            else
            {
                Assert.Equal(value, littleEndian);
                Assert.Equal(value, bigEndian);
            }
        });
    }

    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(-double.Epsilon)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void Double_SpecialValues(double value)
    {
        CreateShared((w, _) =>
        {
            w.Write(value);
            w.Write(value, EndianType.BIG);
        },
        (r, _) =>
        {
            double littleEndian = r.ReadDouble();
            double bigEndian = r.ReadDouble(EndianType.BIG);

            if (double.IsNaN(value))
            {
                Assert.True(double.IsNaN(littleEndian));
                Assert.True(double.IsNaN(bigEndian));
            }
            else
            {
                Assert.Equal(value, littleEndian);
                Assert.Equal(value, bigEndian);
            }
        });
    }

    [Fact]
    public void Bool_TrueAndFalse_RoundTrip()
    {
        CreateShared((w, _) =>
        {
            w.Write(true);
            w.Write(false);
            w.Write(true);
        },
        (r, _) =>
        {
            Assert.True(r.ReadBool());
            Assert.False(r.ReadBool());
            Assert.True(r.ReadBool());
        });
    }

    [Fact]
    public void Bool_NonZeroIsTrue()
    {
        var stream = new MemoryStream(new byte[] { 0, 1, 2, 255 });
        using var reader = new EndianStreamReader(stream);

        Assert.False(reader.ReadBool());
        Assert.True(reader.ReadBool());
        Assert.True(reader.ReadBool());
        Assert.True(reader.ReadBool());
    }

    [Fact]
    public void Sha1_ZeroHash_RoundTrip()
    {
        var sha1 = new Sha1(0, 0, 0, 0, 0);
        CreateShared((w, _) => w.Write(sha1),
        (r, _) =>
        {
            var result = r.ReadSha1();
            Assert.Equal(sha1, result);
        });
    }

    [Fact]
    public void Sha1_MaxHash_RoundTrip()
    {
        var sha1 = new Sha1(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);
        CreateShared((w, _) => w.Write(sha1),
        (r, _) =>
        {
            var result = r.ReadSha1();
            Assert.Equal(sha1, result);
        });
    }

    [Fact]
    public void WriteSizedCString_SmallerThanSize_PadsWithZeros()
    {
        CreateShared((w, _) =>
        {
            w.WriteSizedCString("Hello", 10);
        },
        (r, _) =>
        {
            byte[] data = r.ReadBytes(10);
            Assert.Equal((byte)'H', data[0]);
            Assert.Equal((byte)'o', data[4]);
            Assert.Equal(0, data[5]);
            Assert.Equal(0, data[9]);
        });
    }

    [Fact]
    public void WriteSizedCString_SizeEqualToText_NoPadding()
    {
        CreateShared((w, _) =>
        {
            w.WriteSizedCString("Hello", 5);
        },
        (r, _) =>
        {
            string result = r.ReadSizedCString(5);
            Assert.Equal("Hello", result);
        });
    }

    [Fact]
    public void WriteSizedCString_SizeSmallerThanText_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteSizedCString("HelloWorld", 5));
    }

    [Fact]
    public void WriteSizedCString_NullText_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentNullException>(() => writer.WriteSizedCString(null!, 10));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void WritePadding_VariousAlignments(byte alignment)
    {
        CreateShared((w, _) =>
        {
            w.Write((byte)0xFF);
            w.WritePadding(alignment);
            long position = w.Position;
            Assert.Equal(0, position % alignment);
        },
        (r, _) => { r.Position = r.Length; });
    }

    [Fact]
    public void ReadAlignment_VariousAlignments()
    {
        CreateShared((w, _) =>
        {
            w.Write((byte)0xFF);
            w.WritePadding(16);
        },
        (r, _) =>
        {
            r.ReadByte();
            r.ReadAlignment(16);
            Assert.Equal(16, r.Position);
        });
    }

    [Fact]
    public void ReadLine_EmptyLine()
    {
        CreateShared((w, _) =>
        {
            w.WriteLine("");
            w.WriteLine("test");
        },
        (r, _) =>
        {
            Assert.Equal("", r.ReadLine());
            Assert.Equal("test", r.ReadLine());
        });
    }

    [Fact]
    public void ReadLine_OnlyNewLine()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.WriteStringCharacters("\r\n");
        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        string? result = reader.ReadLine();
        Assert.Equal("", result);
    }

    [Fact]
    public void ReadLine_LfOnly()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.WriteStringCharacters("test\nmore");
        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        string? result = reader.ReadLine();
        Assert.Equal("test", result);
    }

    [Fact]
    public void ReadLine_CrLf()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.WriteStringCharacters("test\r\nmore");
        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        string? result = reader.ReadLine();
        Assert.Equal("test", result);
    }

    [Fact]
    public void ReadLine_EndOfStream_ReturnsNull()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        string? result = reader.ReadLine();
        Assert.Null(result);
    }

    [Fact]
    public void Position_SetBeyondEnd_AllowedByStream()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write(100);

        Assert.Equal(4, stream.Position);
        stream.Position = 100;
        Assert.Equal(100, stream.Position);
    }

    [Fact]
    public void Position_Negative_ThrowsException()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Position = -1);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(1000, 1000)]
    [InlineData(10000, 10000)]
    public void LargeByteArray_RoundTrip(int size, int expectedSize)
    {
        byte[] data = new byte[size];
        new Random(42).NextBytes(data);

        CreateShared((w, _) => w.Write(data),
        (r, _) =>
        {
            byte[] result = r.ReadBytes(expectedSize);
            Assert.Equal(data, result);
        });
    }

    [Fact]
    public void MixedEndianTypes_InSingleStream()
    {
        CreateShared((w, _) =>
        {
            w.Write((short)100, EndianType.LITTLE);
            w.Write((short)200, EndianType.BIG);
            w.Write(300, EndianType.LITTLE);
            w.Write(400, EndianType.BIG);
            w.Write(500L, EndianType.LITTLE);
            w.Write(600L, EndianType.BIG);
        },
        (r, _) =>
        {
            Assert.Equal(100, r.ReadInt16(EndianType.LITTLE));
            Assert.Equal(200, r.ReadInt16(EndianType.BIG));
            Assert.Equal(300, r.ReadInt32(EndianType.LITTLE));
            Assert.Equal(400, r.ReadInt32(EndianType.BIG));
            Assert.Equal(500L, r.ReadInt64(EndianType.LITTLE));
            Assert.Equal(600L, r.ReadInt64(EndianType.BIG));
        });
    }

    [Fact]
    public void ReadCString_NoTerminator_ReadsUntilEnd()
    {
        var stream = new MemoryStream(new byte[] { (byte)'H', (byte)'i' });
        using var reader = new EndianStreamReader(stream);

        string result = reader.ReadCString();
        Assert.Equal("Hi", result);
    }

    [Fact]
    public void ReadCString_EmptyStream_ThrowsException()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<EndOfStreamException>(reader.ReadCString);
    }

    [Theory]
    [InlineData(0.0f, 0.0f)]
    [InlineData(-0.0f, -0.0f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(-1.0f, -1.0f)]
    public void Float_PositiveAndNegativeZero(float writeValue, float expectedValue)
    {
        CreateShared((w, _) => w.Write(writeValue),
        (r, _) =>
        {
            float result = r.ReadFloat();
            Assert.Equal(expectedValue, result);
        });
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(-0.0, -0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(-1.0, -1.0)]
    public void Double_PositiveAndNegativeZero(double writeValue, double expectedValue)
    {
        CreateShared((w, _) => w.Write(writeValue),
        (r, _) =>
        {
            double result = r.ReadDouble();
            Assert.Equal(expectedValue, result);
        });
    }
}
