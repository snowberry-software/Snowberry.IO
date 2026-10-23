using System.Buffers.Binary;
using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Writer.Interfaces;
using Snowberry.IO.Tests.Infrastructure;
using Snowberry.IO.Writer;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Writer;

/// <summary>
/// Contains tests for the bytes, positions and stream handling of <see cref="EndianStreamWriter"/>.
/// </summary>
public class EndianStreamWriterTests
{
    private static readonly byte[] s_Bytes = [1, 2, 3, 4, 5];
    private static readonly char[] s_Chars = ['A', 'é', '世'];
    private static readonly char[] s_MoreChars = ['A', 'é', '世', 'B'];

    private static readonly Dictionary<string, (Action<EndianStreamWriter> Endian, Action<BinaryWriter> Binary)> s_BinaryWriterOverloads = new()
    {
        ["String"] = (w => w.Write("Aé世"), w => w.Write("Aé世")),
        ["Decimal"] = (w => w.Write(123.456m), w => w.Write(123.456m)),
        ["Single"] = (w => w.Write(1.5f), w => w.Write(1.5f)),
        ["Double"] = (w => w.Write(1.5d), w => w.Write(1.5d)),
        ["Byte"] = (w => w.Write((byte)0xAB), w => w.Write((byte)0xAB)),
        ["SByte"] = (w => w.Write((sbyte)-5), w => w.Write((sbyte)-5)),
        ["Int16"] = (w => w.Write((short)-12345), w => w.Write((short)-12345)),
        ["UInt16"] = (w => w.Write((ushort)0xABCD), w => w.Write((ushort)0xABCD)),
        ["Int32"] = (w => w.Write(-123456789), w => w.Write(-123456789)),
        ["UInt32"] = (w => w.Write(0xDEADBEEFu), w => w.Write(0xDEADBEEFu)),
        ["Int64"] = (w => w.Write(-1234567890123L), w => w.Write(-1234567890123L)),
        ["UInt64"] = (w => w.Write(0xDEADBEEFCAFEBABEul), w => w.Write(0xDEADBEEFCAFEBABEul)),
        ["ByteArray"] = (w => w.Write(s_Bytes), w => w.Write(s_Bytes)),
        ["ByteArrayWithIndexAndCount"] = (w => w.Write(s_Bytes, 1, 3), w => w.Write(s_Bytes, 1, 3)),
        ["CharArray"] = (w => w.Write(s_Chars), w => w.Write(s_Chars)),
        ["CharArrayWithIndexAndCount"] = (w => w.Write(s_MoreChars, 1, 2), w => w.Write(s_MoreChars, 1, 2)),
#if NETCOREAPP
        ["ReadOnlySpanOfChar"] = (w => w.Write("Aé世".AsSpan()), w => w.Write("Aé世".AsSpan())),
        ["ReadOnlySpanOfByte"] = (w => w.Write(s_Bytes.AsSpan()), w => w.Write(s_Bytes.AsSpan())),
        ["Half"] = (w => w.Write((Half)1.5f), w => w.Write((Half)1.5f)),
#endif
    };

    private static readonly Dictionary<string, (Action<IEndianWriter> Write, byte[] Expected)> s_LittleEndianValues = new()
    {
        ["Int32"] = (w => w.Write(0x12345678, EndianType.LITTLE), ToLittleEndian(BitConverter.GetBytes(0x12345678))),
        ["Single"] = (w => w.Write(123.456f, EndianType.LITTLE), ToLittleEndian(BitConverter.GetBytes(123.456f))),
        ["Double"] = (w => w.Write(123.456789, EndianType.LITTLE), ToLittleEndian(BitConverter.GetBytes(123.456789)))
    };

    [Theory]
    [InlineData("String")]
    [InlineData("Decimal")]
    [InlineData("Single")]
    [InlineData("Double")]
    [InlineData("Byte")]
    [InlineData("SByte")]
    [InlineData("Int16")]
    [InlineData("UInt16")]
    [InlineData("Int32")]
    [InlineData("UInt32")]
    [InlineData("Int64")]
    [InlineData("UInt64")]
    [InlineData("ByteArray")]
    [InlineData("ByteArrayWithIndexAndCount")]
    [InlineData("CharArray")]
    [InlineData("CharArrayWithIndexAndCount")]
#if NETCOREAPP
    [InlineData("ReadOnlySpanOfChar")]
    [InlineData("ReadOnlySpanOfByte")]
    [InlineData("Half")]
#endif
    public void Write_InheritedOverload_MatchesBinaryWriter(string overload)
    {
        var (endian, binary) = s_BinaryWriterOverloads[overload];

        WriteEndianStreamWriterBytes(endian).Should().Equal(WriteBinaryWriterBytes(binary));
    }

    [Fact]
    public void Write_ByteArray_PassesArrayToStream()
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write(s_Bytes);

        stream.WriteCalls.Should().Be(1);
        stream.LastArray.Should().BeSameAs(s_Bytes);
        stream.ToArray().Should().Equal(s_Bytes);
    }

    [Fact]
    public void Write_ByteArrayWithIndexAndCount_PassesArrayToStream()
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write(s_Bytes, 1, 3);

        stream.WriteCalls.Should().Be(1);
        stream.LastArray.Should().BeSameAs(s_Bytes);
        stream.ToArray().Should().Equal(2, 3, 4);
    }

    [Fact]
    public void Write_NullByteArray_ThrowsArgumentNullException()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        FluentActions.Invoking(() => writer.Write((byte[])null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(200000)]
    public void Write_ReadOnlySpanOfByte_WritesSpanBytes(int length)
    {
        byte[] data = RandomBytes(length);

        byte[] actual = WriteEndianStreamWriterBytes(w =>
        {
            w.Write(new ReadOnlySpan<byte>(data));
            w.Write(new ReadOnlySpan<byte>(data, 0, length / 2));
        });

        actual.Should().Equal([.. data, .. data.Take(length / 2)]);
    }

    [Fact]
    public void Write_SpanOfByte_BindsToReadOnlySpanOverload()
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);
        Span<byte> span = [7, 8, 9];

        writer.Write(span);

        stream.WriteCalls.Should().Be(1);
        stream.ToArray().Should().Equal(7, 8, 9);
    }

    [Theory]
    [InlineData('A')]
    [InlineData('é')]
    [InlineData('世')]
    public void Write_Char_MatchesBinaryWriter(char value)
    {
        WriteEndianStreamWriterBytes(w => w.Write(value)).Should().Equal(WriteBinaryWriterBytes(w => w.Write(value)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Write_Bool_MatchesBinaryWriter(bool value)
    {
        WriteEndianStreamWriterBytes(w => w.Write(value)).Should().Equal(WriteBinaryWriterBytes(w => w.Write(value)));
    }

    [Theory]
    [InlineData("Int32")]
    [InlineData("Single")]
    [InlineData("Double")]
    public void Write_LittleEndianValue_MatchesBitConverter(string type)
    {
        var (write, expected) = s_LittleEndianValues[type];

        WriteBytes(write).Should().Equal(expected);
    }

    [Fact]
    public void Write_BigEndianIntegers_MatchBinaryPrimitives()
    {
        byte[] expected = new byte[2 + 2 + 4 + 4 + 8 + 8];
        BinaryPrimitives.WriteInt16BigEndian(expected, -2);
        BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(2), 0xABCD);
        BinaryPrimitives.WriteInt32BigEndian(expected.AsSpan(4), -123456789);
        BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(8), 0xDEADBEEF);
        BinaryPrimitives.WriteInt64BigEndian(expected.AsSpan(12), -1234567890123L);
        BinaryPrimitives.WriteUInt64BigEndian(expected.AsSpan(20), 0xDEADBEEFCAFEBABE);

        byte[] actual = WriteEndianStreamWriterBytes(w => w
            .Write((short)-2, EndianType.BIG)
            .Write((ushort)0xABCD, EndianType.BIG)
            .Write(-123456789, EndianType.BIG)
            .Write(0xDEADBEEFu, EndianType.BIG)
            .Write(-1234567890123L, EndianType.BIG)
            .Write(0xDEADBEEFCAFEBABEul, EndianType.BIG));

        actual.Should().Equal(expected);
    }

    [Fact]
    public void WriteInt32_BigEndian_ReversesLittleEndianBytes()
    {
        byte[] little = WriteBytes(w => w.Write(0x12345678, EndianType.LITTLE));
        byte[] big = WriteBytes(w => w.Write(0x12345678, EndianType.BIG));

        big.Should().Equal(Enumerable.Reverse(little));
    }

    [Fact]
    public void WriteGuid_LittleEndian_WritesGuidToByteArrayOrder()
    {
        var guid = Guid.NewGuid();

        WriteBytes(w => w.Write(guid, EndianType.LITTLE)).Should().Equal(guid.ToByteArray());
    }

    [Fact]
    public void WriteGuid_BigEndian_WritesRfc4122ByteOrder()
    {
        var guid = Guid.NewGuid();
        byte[] standard = guid.ToByteArray();
        byte[] expected =
        [
            standard[3], standard[2], standard[1], standard[0],
            standard[5], standard[4], standard[7], standard[6],
            standard[8], standard[9], standard[10], standard[11],
            standard[12], standard[13], standard[14], standard[15],
        ];

        WriteBytes(w => w.Write(guid, EndianType.BIG)).Should().Equal(expected);
    }

    [Theory]
    [InlineData(EndianType.LITTLE, new byte[] { 0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF })]
    [InlineData(EndianType.BIG, new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF })]
    public void WriteGuid_KnownGuid_WritesExpectedBytes(EndianType endian, byte[] expected)
    {
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        WriteBytes(w => w.Write(guid, endian)).Should().Equal(expected);
    }

    [Fact]
    public void WriteGuid_SeveralValuesInBothByteOrders_WritesEachLayout()
    {
        var first = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var second = new Guid("fedcba98-7654-3210-0123-456789abcdef");
        byte[] expected =
        [
            .. first.ToByteArray(),
            0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
            .. second.ToByteArray()
        ];

        byte[] actual = WriteBytes(w => w.Write(first, EndianType.LITTLE).Write(second, EndianType.BIG).Write(first, EndianType.BIG).Write(second));

        actual.Should().Equal(expected);
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void WriteGuid_BothByteOrders_WritesWithOneStreamCall(EndianType endian)
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write(Guid.NewGuid(), endian);

        stream.WriteCalls.Should().Be(1);
        stream.Length.Should().Be(16);
    }

    [Fact]
    public void WriteSha1_AnyHash_WritesHashBuffer()
    {
        var sha1 = new Sha1(0x11223344, 0x55667788, 0x99AABBCC, 0xDDEEFF00, 0x01020304);

        WriteEndianStreamWriterBytes(w => w.Write(sha1)).Should().Equal(sha1.GetHashBuffer());
    }

    [Fact]
    public void WriteSha1_SeveralHashes_WritesEachHashBuffer()
    {
        var first = new Sha1(0x11223344, 0x55667788, 0x99AABBCC, 0xDDEEFF00, 0x01020304);
        var second = new Sha1(0xFFFFFFFF, 0, 0x80000000, 1, 0x7FFFFFFF);

        byte[] actual = WriteBytes(w => w.Write(first).Write(second).Write(first));

        actual.Should().Equal([.. first.GetHashBuffer(), .. second.GetHashBuffer(), .. first.GetHashBuffer()]);
    }

    [Fact]
    public void WriteSha1_AnyHash_WritesWithOneStreamCall()
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);
        var sha1 = new Sha1(0x11223344, 0x55667788, 0x99AABBCC, 0xDDEEFF00, 0x01020304);

        writer.Write(sha1);

        stream.WriteCalls.Should().Be(1);
        stream.ToArray().Should().Equal(sha1.GetHashBuffer());
    }

    [Theory]
    [InlineData(0x00000000)]
    [InlineData(unchecked((int)0x80000000))]
    [InlineData(0x00000001)]
    [InlineData(0x3FC00000)]
    [InlineData(0x7F800000)]
    [InlineData(0x7FC00001)]
    [InlineData(unchecked((int)0xFFC00000))]
    public void WriteSingle_BigEndian_WritesBitsInBigEndianOrder(int bits)
    {
        float value = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        byte[] expected = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(expected, bits);

        WriteBytes(w => w.Write(value, EndianType.BIG)).Should().Equal(expected);
    }

#if NETCOREAPP
    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x8000)]
    [InlineData(0x0001)]
    [InlineData(0x3C00)]
    [InlineData(0x7C00)]
    [InlineData(0x7E01)]
    public void WriteHalf_BothByteOrders_WritesBitsInRequestedOrder(int bits)
    {
        var value = BitConverter.Int16BitsToHalf((short)bits);
        byte[] little = new byte[2];
        byte[] big = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(little, (ushort)bits);
        BinaryPrimitives.WriteUInt16BigEndian(big, (ushort)bits);

        WriteBytes(w => w.Write(value, EndianType.LITTLE)).Should().Equal(little);
        WriteBytes(w => w.Write(value, EndianType.BIG)).Should().Equal(big);
        WriteEndianStreamWriterBytes(w => w.Write(value)).Should().Equal(little);
    }
#endif

    [Fact]
    public void Write_ByteAndSByteWithByteOrder_BindToInt16Overload()
    {
        byte unsignedValue = 0xAB;
        sbyte signedValue = -2;

        WriteEndianStreamWriterBytes(w => w.Write(unsignedValue, EndianType.BIG).Write(signedValue, EndianType.BIG)).Should().Equal(0x00, 0xAB, 0xFF, 0xFE);
        WriteBytes(w => w.Write(unsignedValue, EndianType.BIG).Write(signedValue, EndianType.BIG)).Should().Equal(0x00, 0xAB, 0xFF, 0xFE);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    [InlineData(0x3FFF)]
    [InlineData(0x4000)]
    [InlineData(0x1FFFFF)]
    [InlineData(0x200000)]
    [InlineData(0xFFFFFFF)]
    [InlineData(0x10000000)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Write7BitEncodedInt_EdgeValue_WritesLowestSevenBitGroupFirst(int value)
    {
        byte[] expected = Encode7Bit((uint)value);

        WriteBytes(w => w.Write7BitEncodedInt(value)).Should().Equal(expected);
        WriteEndianStreamWriterBytes(w => w.Write7BitEncodedInt(value)).Should().Equal(expected);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(0x7FL)]
    [InlineData(0x80L)]
    [InlineData(0x3FFFL)]
    [InlineData(0x4000L)]
    [InlineData(0xFFFFFFFL)]
    [InlineData(0x10000000L)]
    [InlineData(0xFFFFFFFFL)]
    [InlineData(1L << 35)]
    [InlineData(1L << 60)]
    [InlineData(long.MaxValue)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Write7BitEncodedInt64_EdgeValue_WritesLowestSevenBitGroupFirst(long value)
    {
        WriteBytes(w => w.Write7BitEncodedInt64(value)).Should().Equal(Encode7Bit((ulong)value));
    }

#if !NETCOREAPP
    [Theory]
    [InlineData(0L)]
    [InlineData(0x7FL)]
    [InlineData(0x80L)]
    [InlineData(0xFFFFFFFL)]
    [InlineData(1L << 60)]
    [InlineData(-1L)]
    public void Write7BitEncodedInt64_AnyValue_WritesWithOneStreamCall(long value)
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        ((IEndianWriter)writer).Write7BitEncodedInt64(value);

        stream.WriteCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x7F)]
    [InlineData(0x80)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    public void Write7BitEncodedInt_AnyValue_WritesWithOneStreamCall(int value)
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write7BitEncodedInt(value);
        stream.WriteCalls.Should().Be(1);

        stream.Reset();
        ((IEndianWriter)writer).Write7BitEncodedInt(value);
        stream.WriteCalls.Should().Be(1);
    }
#endif

    [Fact]
    public void Write7BitEncodedInt_SeveralValues_WritesEachEncoding()
    {
        byte[] expected = [.. Encode7Bit(0x80), .. Encode7Bit(5), .. Encode7Bit(ulong.MaxValue), .. Encode7Bit(uint.MaxValue), .. Encode7Bit(0)];

        byte[] actual = WriteBytes(w => w.Write7BitEncodedInt(0x80).Write7BitEncodedInt64(5).Write7BitEncodedInt64(-1).Write7BitEncodedInt(-1).Write7BitEncodedInt64(0));

        actual.Should().Equal(expected);
    }

    [Fact]
    public void Write_SameValuesTwice_WritesSameBytes()
    {
        static void WriteValues(IEndianWriter writer)
        {
            writer.Write(100, EndianType.LITTLE)
                .Write(200L, EndianType.LITTLE)
                .Write(300.5f, EndianType.LITTLE)
                .Write(400.123, EndianType.LITTLE);
        }

        WriteBytes(WriteValues).Should().Equal(WriteBytes(WriteValues));
    }

    [Fact]
    public void Write_PrimitivesAndString_ReadableByBinaryReader()
    {
        byte[] data = WriteBytes(w => w.Write(100).Write(200L).Write("Test"));
        using var reader = new BinaryReader(new MemoryStream(data));

        reader.ReadInt32().Should().Be(100);
        reader.ReadInt64().Should().Be(200L);
        reader.ReadString().Should().Be("Test");
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void WriteByte_TenThousandBytes_WritesEachByte()
    {
        byte[] expected = new byte[10000];
        var random = new Random(42);

        for (int i = 0; i < expected.Length; i++)
            expected[i] = (byte)random.Next(256);

        byte[] actual = WriteBytes(w =>
        {
            foreach (byte value in expected)
                w.Write(value);
        });

        actual.Should().Equal(expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void WritePadding_PowerOfTwoAlignment_MovesToNextBoundary(byte alignment)
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);
        writer.Write((byte)1);
        writer.Position.Should().Be(1);

        writer.WritePadding(alignment);

        writer.Position.Should().Be(alignment);
        (writer.Position % alignment).Should().Be(0);
    }

    [Fact]
    public void WritePadding_Alignment16_WritesZeroBytes()
    {
        byte[] bytes = WriteBytes(w => w.Write((byte)0xFF).WritePadding(16));

        bytes.Length.Should().Be(16);
        bytes.Skip(1).Should().AllBeEquivalentTo((byte)0);
    }

    [Fact]
    public void WritePadding_ZeroAlignment_ThrowsArgumentOutOfRangeException()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        FluentActions.Invoking(() => writer.WritePadding(0)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Position_AfterWrites_EqualsBytesWritten()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        writer.Write(100);
        writer.Position.Should().Be(4);

        writer.Write(200L);
        writer.Position.Should().Be(12);

        writer.Write((short)300);
        writer.Position.Should().Be(14);
    }

    [Fact]
    public void Position_SetBackwards_OverwritesValue()
    {
        byte[] data = WriteBytes(w =>
        {
            w.Write(100);
            long secondValuePosition = w.Position;
            w.Write(200);

            w.Position = secondValuePosition;
            w.Write(300);
        });
        using var reader = CreateReader(data);

        reader.ReadInt32().Should().Be(100);
        reader.ReadInt32().Should().Be(300);
    }

    [Fact]
    public void BaseStreamPosition_SetPastWrittenData_IsKept()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write(100);
        stream.Position.Should().Be(4);

        stream.Position = 100;

        stream.Position.Should().Be(100);
    }

    [Fact]
    public void Length_AfterWrites_EqualsBytesWritten()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        writer.Length.Should().Be(0);

        writer.Write(100);
        writer.Length.Should().Be(4);

        writer.Write(new byte[10]);
        writer.Length.Should().Be(14);
    }

    [Fact]
    public void PositionLengthPaddingAndBaseStream_Used_NeverFlushStream()
    {
        var stream = new FlushCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.Write((byte)1);
        _ = writer.Position;
        _ = writer.Length;
        writer.WritePadding(8);
        writer.Position = 2;
        _ = writer.BaseStream;

        stream.FlushCount.Should().Be(0);
        writer.Length.Should().Be(8);
        writer.Position.Should().Be(2);
        writer.BaseStream.Should().BeSameAs(stream);
    }

    [Fact]
    public void Constructor_NullStream_ThrowsArgumentNullException()
    {
        FluentActions.Invoking(() => new EndianStreamWriter(null!, true)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ReadOnlyStream_ThrowsArgumentException()
    {
        using var readOnly = new MemoryStream([1, 2, 3], writable: false);

        FluentActions.Invoking(() =>
        {
            using var writer = new EndianStreamWriter(readOnly, true);
            writer.Write(0);
        }).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Dispose_KeepStreamOpen_LeavesStreamReadable()
    {
        var stream = new MemoryStream();

        using (var writer = new EndianStreamWriter(stream, keepStreamOpen: true))
            writer.Write(100);

        stream.Position = 0;
        using var reader = CreateReader(stream);
        reader.ReadInt32().Should().Be(100);
    }

    [Fact]
    public void Dispose_KeepStreamOpenFalse_DisposesStream()
    {
        var stream = new MemoryStream();

        using (var writer = new EndianStreamWriter(stream, keepStreamOpen: false))
            writer.Write(100);

        FluentActions.Invoking(() => stream.Position = 0).Should().ThrowExactly<ObjectDisposedException>();
    }

    private static byte[] ToLittleEndian(byte[] bytes)
    {
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return bytes;
    }

    /// <summary>
    /// Encodes <paramref name="value"/> in seven-bit groups, lowest group first, with the high bit set on every byte but the last.
    /// </summary>
    private static byte[] Encode7Bit(ulong value)
    {
        var bytes = new List<byte>();

        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
        return [.. bytes];
    }
}