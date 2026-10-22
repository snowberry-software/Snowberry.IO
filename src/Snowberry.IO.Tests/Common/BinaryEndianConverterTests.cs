using System.Buffers.Binary;
using AwesomeAssertions;
using Snowberry.IO.Common;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Common;

/// <summary>
/// Contains tests for <see cref="BinaryEndianConverter"/>.
/// </summary>
public class BinaryEndianConverterTests
{
    private static readonly Dictionary<string, Action<byte[], int, EndianType>> s_OffsetConversions = new()
    {
        ["Int16"] = (data, offset, endian) => BinaryEndianConverter.ToInt16(data, offset, endian),
        ["UInt16"] = (data, offset, endian) => BinaryEndianConverter.ToUInt16(data, offset, endian),
        ["Int32"] = (data, offset, endian) => BinaryEndianConverter.ToInt32(data, offset, endian),
        ["UInt32"] = (data, offset, endian) => BinaryEndianConverter.ToUInt32(data, offset, endian),
        ["UInt64"] = (data, offset, endian) => BinaryEndianConverter.ToUInt64(data, offset, endian),
        ["Int64"] = (data, offset, endian) => BinaryEndianConverter.ToInt64(data, offset, endian),
        ["Guid"] = (data, offset, endian) => BinaryEndianConverter.ToGuid(data, offset, endian),
        ["Float"] = (data, offset, endian) => BinaryEndianConverter.ToFloat(data, offset, endian),
        ["Double"] = (data, offset, endian) => BinaryEndianConverter.ToDouble(data, offset, endian)
    };

    private static readonly Dictionary<string, Action<byte[], EndianType>> s_SpanConversions = new()
    {
        ["Int16"] = (data, endian) => BinaryEndianConverter.ToInt16(data, endian),
        ["UInt16"] = (data, endian) => BinaryEndianConverter.ToUInt16(data, endian),
        ["Int32"] = (data, endian) => BinaryEndianConverter.ToInt32(data, endian),
        ["UInt32"] = (data, endian) => BinaryEndianConverter.ToUInt32(data, endian),
        ["Int64"] = (data, endian) => BinaryEndianConverter.ToInt64(data, endian),
        ["UInt64"] = (data, endian) => BinaryEndianConverter.ToUInt64(data, endian),
        ["Float"] = (data, endian) => BinaryEndianConverter.ToFloat(data, endian),
        ["Double"] = (data, endian) => BinaryEndianConverter.ToDouble(data, endian)
    };

    private static readonly Dictionary<string, int> s_TypeSizes = new()
    {
        ["Int16"] = 2,
        ["UInt16"] = 2,
        ["Int32"] = 4,
        ["UInt32"] = 4,
        ["Int64"] = 8,
        ["UInt64"] = 8,
        ["Float"] = 4,
        ["Double"] = 8,
        ["Guid"] = 16
    };

    /// <summary>
    /// Creates pseudo-random bytes that never form a NaN or infinity, whichever 4 or 8 of them are read in either byte order.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <returns>Bytes below <c>0x40</c>, so no exponent has all bits set.</returns>
    private static byte[] FiniteFloatingPointBytes(int length)
    {
        byte[] data = RandomBytes(length, seed: 5);

        for (int i = 0; i < data.Length; i++)
            data[i] &= 0x3F;

        return data;
    }

    /// <summary>
    /// Assembles <paramref name="size"/> bytes of <paramref name="data"/>, starting at <paramref name="offset"/>, into an integer byte by byte.
    /// </summary>
    /// <param name="data">The source data.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <param name="size">The number of bytes, at most 8.</param>
    /// <param name="endian">The byte order of the bytes.</param>
    /// <returns>The assembled integer, zero-extended to 64 bits.</returns>
    private static ulong ReadReference(byte[] data, int offset, int size, EndianType endian)
    {
        ulong value = 0;

        for (int i = 0; i < size; i++)
        {
            int shift = endian == EndianType.LITTLE ? i * 8 : (size - 1 - i) * 8;
            value |= (ulong)data[offset + i] << shift;
        }

        return value;
    }

    [Fact]
    public void ToDouble_KnownBytes_ReturnsLittleAndBigEndianValues()
    {
        byte[] little = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F];
        byte[] big = [0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        BinaryEndianConverter.ToDouble(little, EndianType.LITTLE).Should().Be(1.0d);
        BinaryEndianConverter.ToDouble(big, EndianType.BIG).Should().Be(1.0d);
    }

    [Fact]
    public void ToFloat_KnownBytes_ReturnsLittleAndBigEndianValues()
    {
        byte[] little = [0x00, 0x00, 0x80, 0x3F];
        byte[] big = [0x3F, 0x80, 0x00, 0x00];

        BinaryEndianConverter.ToFloat(little, EndianType.LITTLE).Should().Be(1.0f);
        BinaryEndianConverter.ToFloat(big, EndianType.BIG).Should().Be(1.0f);
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void ToFloatingPoint_EveryOffset_ReinterpretsTheIntegerBits(EndianType endian)
    {
        byte[] data = FiniteFloatingPointBytes(20);

        for (int offset = 0; offset <= data.Length - 4; offset++)
        {
            BitConverter.ToInt32(BitConverter.GetBytes(BinaryEndianConverter.ToFloat(data, offset, endian)), 0).Should().Be((int)ReadReference(data, offset, 4, endian));

            if (offset <= data.Length - 8)
                BitConverter.DoubleToInt64Bits(BinaryEndianConverter.ToDouble(data, offset, endian)).Should().Be((long)ReadReference(data, offset, 8, endian));
        }
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void ToFloatingPoint_SpanOverloads_ReinterpretTheIntegerBits(EndianType endian)
    {
        byte[] data = FiniteFloatingPointBytes(8);

        BitConverter.ToInt32(BitConverter.GetBytes(BinaryEndianConverter.ToFloat(data, endian)), 0).Should().Be((int)ReadReference(data, 0, 4, endian));
        BitConverter.DoubleToInt64Bits(BinaryEndianConverter.ToDouble(data, endian)).Should().Be((long)ReadReference(data, 0, 8, endian));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ToGuid_BigEndianAtOffset_ReadsTheFirstThreeComponentsBigEndian(int offset)
    {
        byte[] data = [.. new byte[offset], 0x76, 0x54, 0x32, 0x10, 0xBA, 0x98, 0xFE, 0xDC, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

        BinaryEndianConverter.ToGuid(data, offset, EndianType.BIG)
            .Should().Be(new Guid(0x76543210, unchecked((short)0xBA98), unchecked((short)0xFEDC), 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF));
    }

    [Fact]
    public void ToGuid_BigEndianBytesAtOffsetFromWriter_ReturnsWrittenGuid()
    {
        var guid = Guid.NewGuid();

        byte[] data = WriteBytes(w => w.Write((byte)0xFF).Write(guid, EndianType.BIG));

        BinaryEndianConverter.ToGuid(data, 1, EndianType.BIG).Should().Be(guid);
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void ToGuid_EveryOffset_MatchesReference(EndianType endian)
    {
        byte[] data = RandomBytes(40, seed: 11);

        for (int offset = 0; offset <= data.Length - 16; offset++)
        {
            var expected = new Guid(
                (int)ReadReference(data, offset, 4, endian),
                (short)ReadReference(data, offset + 4, 2, endian),
                (short)ReadReference(data, offset + 6, 2, endian),
                data[offset + 8], data[offset + 9], data[offset + 10], data[offset + 11], data[offset + 12], data[offset + 13], data[offset + 14], data[offset + 15]);

            BinaryEndianConverter.ToGuid(data, offset, endian).Should().Be(expected);
        }
    }

    [Fact]
    public void ToGuid_LittleAndBigEndian_DifferOnlyInTheFirstThreeComponents()
    {
        byte[] data = SequentialBytes(16);

        BinaryEndianConverter.ToGuid(data, 0, EndianType.LITTLE).ToString().Should().Be("03020100-0504-0706-0809-0a0b0c0d0e0f");
        BinaryEndianConverter.ToGuid(data, 0, EndianType.BIG).ToString().Should().Be("00010203-0405-0607-0809-0a0b0c0d0e0f");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void ToGuid_LittleEndianAtOffset_MatchesGuidFromBytes(int offset)
    {
        byte[] guidBytes = [0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];
        byte[] data = [.. new byte[offset], .. guidBytes, 0xFF];

        BinaryEndianConverter.ToGuid(data, offset, EndianType.LITTLE).Should().Be(new Guid(guidBytes));
    }

    [Fact]
    public void ToInt32_BigEndianBytesAtOffsetFromWriter_ReturnsWrittenValue()
    {
        const int value = 0x12345678;
        const int offset = 10;

        byte[] data = WriteBytes(w => w.Write(new byte[offset]).Write(value, EndianType.BIG).Write(new byte[6]));

        BinaryEndianConverter.ToInt32(data, offset, EndianType.BIG).Should().Be(value);
    }

    [Fact]
    public void ToInt32_KnownBytes_ReturnsLittleAndBigEndianValues()
    {
        byte[] data = [0x12, 0x34, 0x56, 0x78];

        BinaryEndianConverter.ToInt32(data, EndianType.LITTLE).Should().Be(0x78563412);
        BinaryEndianConverter.ToInt32(data, EndianType.BIG).Should().Be(0x12345678);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ToInt32_NegativeOffset_ThrowsArgumentOutOfRangeExceptionForOffset(int offset)
    {
        byte[] data = new byte[16];

        FluentActions.Invoking(() => BinaryEndianConverter.ToInt32(data, offset, EndianType.LITTLE))
            .Should().ThrowExactly<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("offset");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void ToInt32_OffsetOutsideBuffer_ThrowsArgumentOutOfRangeException(int offset)
    {
        byte[] buffer = new byte[10];

        FluentActions.Invoking(() => BinaryEndianConverter.ToInt32(buffer, offset, EndianType.LITTLE)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToInt32_OffsetOverloadWithShortBuffer_ThrowsArgumentOutOfRangeException()
    {
        byte[] buffer = new byte[2];

        FluentActions.Invoking(() => BinaryEndianConverter.ToInt32(buffer, 0, EndianType.LITTLE)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToInt64_DefaultEmptySpan_ThrowsArgumentOutOfRangeException()
    {
        // A collection expression creates an empty span without an underlying array, so its reference is null.
        FluentActions.Invoking(() => BinaryEndianConverter.ToInt64([], EndianType.BIG)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToInt64_KnownBytes_ReturnsLittleAndBigEndianValues()
    {
        byte[] data = [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

        BinaryEndianConverter.ToInt64(data, EndianType.LITTLE).Should().Be(unchecked((long)0xEFCDAB8967452301));
        BinaryEndianConverter.ToInt64(data, EndianType.BIG).Should().Be(0x0123456789ABCDEF);
    }

    [Fact]
    public void ToInt64_LittleEndianBytesFromWriter_ReturnsWrittenValue()
    {
        const long value = 0x123456789ABCDEF0;

        byte[] data = WriteBytes(w => w.Write(value, EndianType.LITTLE));

        BinaryEndianConverter.ToInt64(data, EndianType.LITTLE).Should().Be(value);
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void ToIntegers_EveryOffset_MatchReference(EndianType endian)
    {
        byte[] data = RandomBytes(24, seed: 3);

        for (int offset = 0; offset <= data.Length; offset++)
        {
            int available = data.Length - offset;

            if (available >= 2)
            {
                BinaryEndianConverter.ToInt16(data, offset, endian).Should().Be((short)ReadReference(data, offset, 2, endian));
                BinaryEndianConverter.ToUInt16(data, offset, endian).Should().Be((ushort)ReadReference(data, offset, 2, endian));
            }

            if (available >= 4)
            {
                BinaryEndianConverter.ToInt32(data, offset, endian).Should().Be((int)ReadReference(data, offset, 4, endian));
                BinaryEndianConverter.ToUInt32(data, offset, endian).Should().Be((uint)ReadReference(data, offset, 4, endian));
            }

            if (available >= 8)
            {
                BinaryEndianConverter.ToInt64(data, offset, endian).Should().Be((long)ReadReference(data, offset, 8, endian));
                BinaryEndianConverter.ToUInt64(data, offset, endian).Should().Be(ReadReference(data, offset, 8, endian));
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ToIntegers_RandomBytes_MatchBinaryPrimitives(int seed)
    {
        byte[] data = new byte[8];
        new Random(seed).NextBytes(data);

        BinaryEndianConverter.ToInt16(data, EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt16BigEndian(data));
        BinaryEndianConverter.ToUInt16(data, EndianType.BIG).Should().Be(BinaryPrimitives.ReadUInt16BigEndian(data));
        BinaryEndianConverter.ToInt32(data, EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt32BigEndian(data));
        BinaryEndianConverter.ToUInt32(data, EndianType.BIG).Should().Be(BinaryPrimitives.ReadUInt32BigEndian(data));
        BinaryEndianConverter.ToInt64(data, EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt64BigEndian(data));
        BinaryEndianConverter.ToUInt64(data, EndianType.BIG).Should().Be(BinaryPrimitives.ReadUInt64BigEndian(data));

        BinaryEndianConverter.ToInt16(data, EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt16LittleEndian(data));
        BinaryEndianConverter.ToUInt16(data, EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadUInt16LittleEndian(data));
        BinaryEndianConverter.ToInt32(data, EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt32LittleEndian(data));
        BinaryEndianConverter.ToUInt32(data, EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadUInt32LittleEndian(data));
        BinaryEndianConverter.ToInt64(data, EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt64LittleEndian(data));
        BinaryEndianConverter.ToUInt64(data, EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadUInt64LittleEndian(data));
    }

    [Theory]
    [InlineData("Int16", "Int16")]
    [InlineData("UInt16", "UInt16")]
    [InlineData("Int32", "Int32")]
    [InlineData("UInt32", "UInt32")]
    [InlineData("Int64", "Int64")]
    [InlineData("UInt64", "UInt64")]
    [InlineData("Float", "Single")]
    [InlineData("Double", "Double")]
    [InlineData("Guid", "Guid")]
    public void ToPrimitive_OffsetLeavingTooFewBytes_ThrowsArgumentOutOfRangeExceptionNamingTheType(string type, string typeName)
    {
        var convert = s_OffsetConversions[type];
        int size = s_TypeSizes[type];
        byte[] data = new byte[size + 4];
        string expectedMessage = $"Not enough data to read a {typeName} value ({size} bytes required) at the given offset.";

        foreach (int offset in new[] { 5, data.Length, data.Length + 1, -1, int.MinValue, int.MaxValue })
        {
            foreach (var endian in new[] { EndianType.LITTLE, EndianType.BIG })
            {
                var exception = FluentActions.Invoking(() => convert(data, offset, endian)).Should().ThrowExactly<ArgumentOutOfRangeException>().Which;

                exception.ParamName.Should().Be("offset");
                exception.ActualValue.Should().Be(offset);
                exception.Message.Should().StartWith(expectedMessage);
            }
        }
    }

    [Theory]
    [InlineData("Int32", EndianType.LITTLE)]
    [InlineData("Int64", EndianType.LITTLE)]
    [InlineData("Guid", EndianType.LITTLE)]
    [InlineData("Float", EndianType.BIG)]
    [InlineData("Double", EndianType.BIG)]
    public void ToPrimitive_OffsetOverflowingLength_ThrowsArgumentOutOfRangeException(string type, EndianType endian)
    {
        var convert = s_OffsetConversions[type];
        byte[] data = new byte[16];

        FluentActions.Invoking(() => convert(data, int.MaxValue, endian)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void ToPrimitive_SpanOfEveryLength_ReadsTheLeadingBytesOrThrows(EndianType endian)
    {
        byte[] source = RandomBytes(9, seed: 7);

        for (int length = 0; length <= source.Length; length++)
        {
            byte[] data = source.AsSpan(0, length).ToArray();

            if (length >= 2)
            {
                BinaryEndianConverter.ToInt16(data, endian).Should().Be((short)ReadReference(data, 0, 2, endian));
                BinaryEndianConverter.ToUInt16(data, endian).Should().Be((ushort)ReadReference(data, 0, 2, endian));
            }

            if (length >= 4)
            {
                BinaryEndianConverter.ToInt32(data, endian).Should().Be((int)ReadReference(data, 0, 4, endian));
                BinaryEndianConverter.ToUInt32(data, endian).Should().Be((uint)ReadReference(data, 0, 4, endian));
            }

            if (length >= 8)
            {
                BinaryEndianConverter.ToInt64(data, endian).Should().Be((long)ReadReference(data, 0, 8, endian));
                BinaryEndianConverter.ToUInt64(data, endian).Should().Be(ReadReference(data, 0, 8, endian));
            }

            foreach (var conversion in s_SpanConversions)
            {
                if (length < s_TypeSizes[conversion.Key])
                    FluentActions.Invoking(() => conversion.Value(data, endian)).Should().ThrowExactly<ArgumentOutOfRangeException>();
            }
        }
    }

    [Theory]
    [InlineData("Int16", 0)]
    [InlineData("Int16", 1)]
    [InlineData("UInt16", 0)]
    [InlineData("UInt16", 1)]
    [InlineData("Int32", 0)]
    [InlineData("Int32", 1)]
    [InlineData("Int32", 2)]
    [InlineData("Int32", 3)]
    [InlineData("UInt32", 0)]
    [InlineData("UInt32", 1)]
    [InlineData("UInt32", 2)]
    [InlineData("UInt32", 3)]
    [InlineData("Int64", 0)]
    [InlineData("Int64", 1)]
    [InlineData("Int64", 4)]
    [InlineData("Int64", 7)]
    [InlineData("UInt64", 0)]
    [InlineData("UInt64", 1)]
    [InlineData("UInt64", 4)]
    [InlineData("UInt64", 7)]
    [InlineData("Float", 0)]
    [InlineData("Float", 1)]
    [InlineData("Float", 3)]
    [InlineData("Double", 0)]
    [InlineData("Double", 1)]
    [InlineData("Double", 7)]
    public void ToPrimitive_SpanShorterThanType_ThrowsArgumentOutOfRangeException(string type, int length)
    {
        var convert = s_SpanConversions[type];
        byte[] data = new byte[length];

        FluentActions.Invoking(() => convert(data, EndianType.LITTLE)).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => convert(data, EndianType.BIG)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("Int16", 0)]
    [InlineData("Int16", 1)]
    [InlineData("UInt16", 1)]
    [InlineData("Int32", 3)]
    [InlineData("UInt32", 3)]
    [InlineData("Int64", 7)]
    [InlineData("UInt64", 0)]
    [InlineData("Float", 3)]
    [InlineData("Double", 7)]
    public void ToPrimitive_SpanShorterThanType_ThrowsArgumentOutOfRangeExceptionForLength(string type, int length)
    {
        var convert = s_SpanConversions[type];
        byte[] data = new byte[length];
        string expectedMessage = new ArgumentOutOfRangeException("length").Message;

        foreach (var endian in new[] { EndianType.LITTLE, EndianType.BIG })
        {
            var exception = FluentActions.Invoking(() => convert(data, endian)).Should().ThrowExactly<ArgumentOutOfRangeException>().Which;

            exception.ParamName.Should().Be("length");
            exception.ActualValue.Should().BeNull();
            exception.Message.Should().Be(expectedMessage);
        }
    }

    [Theory]
    [InlineData("Int16")]
    [InlineData("UInt16")]
    [InlineData("Int32")]
    [InlineData("UInt32")]
    [InlineData("Int64")]
    [InlineData("UInt64")]
    [InlineData("Float")]
    [InlineData("Double")]
    [InlineData("Guid")]
    public void ToPrimitive_ValueEndingAtTheLastByte_DoesNotThrow(string type)
    {
        var convert = s_OffsetConversions[type];
        byte[] data = new byte[s_TypeSizes[type] + 3];

        FluentActions.Invoking(() => convert(data, 3, EndianType.LITTLE)).Should().NotThrow();
        FluentActions.Invoking(() => convert(data, 3, EndianType.BIG)).Should().NotThrow();
        FluentActions.Invoking(() => convert(data.AsSpan(3).ToArray(), 0, EndianType.BIG)).Should().NotThrow();
    }

    [Fact]
    public void ToUInt16_KnownBytes_ReturnsLittleAndBigEndianValues()
    {
        byte[] data = [0xAB, 0xCD];

        BinaryEndianConverter.ToUInt16(data, EndianType.LITTLE).Should().Be(0xCDAB);
        BinaryEndianConverter.ToUInt16(data, EndianType.BIG).Should().Be(0xABCD);
    }
}