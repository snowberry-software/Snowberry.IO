using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;
using Snowberry.IO.Extensions;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Writer;

/// <summary>
/// Contains tests that write values with <c>EndianStreamWriter</c> and read them back with <c>EndianStreamReader</c>.
/// </summary>
public class WriteReadRoundTripTests
{
    private const int c_Gap = 20;

    /// <summary>
    /// Writes <paramref name="value"/> in little-endian and big-endian order, skips a gap, writes both again, and asserts that
    /// the reads and the <c>*At</c> reads from the internal buffer return the value.
    /// </summary>
    private static void AssertReadsBackInBothByteOrders<T>(
        T value,
        int size,
        Action<IEndianWriter, T, EndianType> write,
        Func<IEndianReader, T> readDefault,
        Func<IEndianReader, EndianType, T> read,
        Func<IEndianReader, EndianType, int, T> readAt)
    {
        WriteThenRead(
            w =>
            {
                write(w, value, EndianType.LITTLE);
                write(w, value, EndianType.BIG);
                w.Position += c_Gap;
                write(w, value, EndianType.LITTLE);
                write(w, value, EndianType.BIG);
            },
            r =>
            {
                readDefault(r).Should().Be(value);
                read(r, EndianType.BIG).Should().Be(value);

                int bufferSize = c_Gap + (size * 2);
                r.EnsureBufferSize(bufferSize);
                r.ReadInInternalBuffer(bufferSize, 0);

                readAt(r, EndianType.LITTLE, c_Gap).Should().Be(value);
                readAt(r, EndianType.BIG, c_Gap + size).Should().Be(value);
            });
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(200L)]
    [InlineData(16383L)]
    [InlineData(16384L)]
    [InlineData(2097151L)]
    [InlineData(2097152L)]
    [InlineData(268435455L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Write7BitEncodedInt64_Value_ReadsBackValue(long value)
    {
        WriteThenRead(w => w.Write7BitEncodedInt64(value), r => r.Read7BitEncodedInt64().Should().Be(value));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [Trait("Category", "Stress")]
    public void Write7BitEncodedInt_ManyRandomValues_ReadsBackEachValue(int count)
    {
        var random = new Random(42);
        var values = Enumerable.Range(0, count).Select(_ => random.Next(int.MaxValue)).ToList();

        WriteThenRead(
            w =>
            {
                foreach (int value in values)
                    w.Write7BitEncodedInt(value);
            },
            r =>
            {
                foreach (int value in values)
                    r.Read7BitEncodedInt().Should().Be(value);
            });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(2097151)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Write7BitEncodedInt_Value_ReadsBackValue(int value)
    {
        WriteThenRead(w => w.Write7BitEncodedInt(value), r => r.Read7BitEncodedInt().Should().Be(value));
    }

    [Fact]
    public void WriteBoolean_TrueAndFalse_ReadsBackValues()
    {
        WriteThenRead(
            w => w.Write(true).Write(false).Write(true),
            r =>
            {
                r.ReadBoolean().Should().BeTrue();
                r.ReadBoolean().Should().BeFalse();
                r.ReadBoolean().Should().BeTrue();
            });
    }

    [Theory]
    [MemberData(nameof(ByteArrays))]
    public void WriteByteArray_Data_ReadsBackSameBytes(byte[] data)
    {
        WriteThenRead(w => w.Write(data), r => r.ReadBytes(data.Length).Should().Equal(data));
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void WriteByteArray_TenMegabytes_ReadsBackSameBytes()
    {
        byte[] data = RandomBytes(10 * 1024 * 1024);

        WriteThenRead(
            w => w.Write(data),
            r =>
            {
                byte[] result = r.ReadBytes(data.Length);

                result.Length.Should().Be(data.Length);
                result.Should().Equal(data);
            });
    }

    [Theory]
    [InlineData(byte.MaxValue)]
    [InlineData(byte.MinValue)]
    public void WriteByte_Value_WritesOneByteAndReadsBack(byte value)
    {
        WriteThenRead(
            w =>
            {
                w.Write(value);
                w.Length.Should().Be(sizeof(byte));
            },
            r => r.ReadByte().Should().Be(value));
    }

    [Theory]
    [InlineData(200D)]
    [InlineData(200.88881111111111111111111111111111D)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void WriteDouble_BothByteOrders_ReadsBackValue(double value)
    {
        AssertReadsBackInBothByteOrders(value, sizeof(double), (w, v, e) => w.Write(v, e), r => r.ReadDouble(), (r, e) => r.ReadDouble(e), (r, e, o) => r.ReadDoubleAt(e, o));
    }

    [Fact]
    public void WriteDouble_NegativeZero_ReadsBackNegativeZero()
    {
        // -0.0 equals 0.0, so only the raw bits show whether the sign survives.
        WriteThenRead(w => w.Write(-0.0), r => BitConverter.GetBytes(r.ReadDouble()).Should().Equal(BitConverter.GetBytes(-0.0)));
    }

    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(-double.Epsilon)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    public void WriteDouble_SpecialValue_ReadsBackSameBits(double value)
    {
        WriteThenRead(
            w => w.Write(value).Write(value, EndianType.BIG),
            r =>
            {
                BitConverter.GetBytes(r.ReadDouble()).Should().Equal(BitConverter.GetBytes(value));
                BitConverter.GetBytes(r.ReadDouble(EndianType.BIG)).Should().Equal(BitConverter.GetBytes(value));
            });
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    public void WriteGuid_BothByteOrders_ReadsBackValue(string guid)
    {
        AssertReadsBackInBothByteOrders(new Guid(guid), 16, (w, v, e) => w.Write(v, e), r => r.ReadGuid(), (r, e) => r.ReadGuid(e), (r, e, o) => r.ReadGuidAt(e, o));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [Trait("Category", "Stress")]
    public void WriteGuid_ManyValues_ReadsBackEachValue(int count)
    {
        var guids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

        WriteThenRead(
            w =>
            {
                foreach (var guid in guids)
                    w.Write(guid);
            },
            r =>
            {
                foreach (var guid in guids)
                    r.ReadGuid().Should().Be(guid);
            });
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void WriteGuid_OneByteOrder_ReadsBackValue(EndianType endian)
    {
        var guid = Guid.NewGuid();

        WriteThenRead(w => w.Write(guid, endian), r => r.ReadGuid(endian).Should().Be(guid));
    }

#if NETCOREAPP
    [Theory]
    [InlineData(0x0000)]
    [InlineData(0x8000)]
    [InlineData(0x0001)]
    [InlineData(0x3C00)]
    [InlineData(0xBC00)]
    [InlineData(0x7BFF)]
    [InlineData(0x7C00)]
    [InlineData(0xFC00)]
    [InlineData(0x7E01)]
    public void WriteHalf_BothByteOrders_ReadsBackSameBits(int bits)
    {
        var value = BitConverter.Int16BitsToHalf((short)bits);

        WriteThenRead(
            w => w.Write(value, EndianType.LITTLE).Write(value, EndianType.BIG).Write(value),
            r =>
            {
                BitConverter.HalfToUInt16Bits(r.ReadHalf(EndianType.LITTLE)).Should().Be((ushort)bits);
                BitConverter.HalfToUInt16Bits(r.ReadHalf(EndianType.BIG)).Should().Be((ushort)bits);
                BitConverter.HalfToUInt16Bits(r.ReadHalf()).Should().Be((ushort)bits);
            });
    }
#endif

    [Theory]
    [InlineData(200)]
    [InlineData(short.MaxValue)]
    [InlineData(short.MinValue)]
    public void WriteInt16_BothByteOrders_ReadsBackValue(short value)
    {
        AssertReadsBackInBothByteOrders(value, sizeof(short), (w, v, e) => w.Write(v, e), r => r.ReadInt16(), (r, e) => r.ReadInt16(e), (r, e, o) => r.ReadInt16At(e, o));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [Trait("Category", "Stress")]
    public void WriteInt32_AlternatingByteOrders_ReadsBackEachValue(int count)
    {
        WriteThenRead(
            w =>
            {
                for (int i = 0; i < count; i++)
                    w.Write(i, i % 2 == 0 ? EndianType.LITTLE : EndianType.BIG);
            },
            r =>
            {
                for (int i = 0; i < count; i++)
                    r.ReadInt32(i % 2 == 0 ? EndianType.LITTLE : EndianType.BIG).Should().Be(i);
            });
    }

    [Theory]
    [InlineData(200)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void WriteInt32_BothByteOrders_ReadsBackValue(int value)
    {
        AssertReadsBackInBothByteOrders(value, sizeof(int), (w, v, e) => w.Write(v, e), r => r.ReadInt32(), (r, e) => r.ReadInt32(e), (r, e, o) => r.ReadInt32At(e, o));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(100000)]
    [Trait("Category", "Stress")]
    public void WriteInt32_ManyValues_ReadsBackEachValue(int count)
    {
        WriteThenRead(
            w =>
            {
                for (int i = 0; i < count; i++)
                    w.Write(i);
            },
            r =>
            {
                for (int i = 0; i < count; i++)
                    r.ReadInt32().Should().Be(i);
            });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [Trait("Category", "Stress")]
    public void WriteInt32_NewStreamPerValue_ReadsBackValue(int count)
    {
        for (int i = 0; i < count; i++)
        {
            int value = i;

            WriteThenRead(w => w.Write(value), r => r.ReadInt32().Should().Be(value));
        }
    }

    [Theory]
    [InlineData(200L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void WriteInt64_BothByteOrders_ReadsBackValue(long value)
    {
        AssertReadsBackInBothByteOrders(value, sizeof(long), (w, v, e) => w.Write(v, e), r => r.ReadInt64(), (r, e) => r.ReadInt64(e), (r, e, o) => r.ReadInt64At(e, o));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(5000)]
    [Trait("Category", "Stress")]
    public void WriteMixedPrimitives_ManyIterations_ReadsBackEachValue(int count)
    {
        WriteThenRead(
            w =>
            {
                for (int i = 0; i < count; i++)
                    w.Write((byte)i).Write((short)i).Write(i).Write((long)i).Write((float)i).Write((double)i);
            },
            r =>
            {
                for (int i = 0; i < count; i++)
                {
                    r.ReadByte().Should().Be((byte)i);
                    r.ReadInt16().Should().Be((short)i);
                    r.ReadInt32().Should().Be(i);
                    r.ReadInt64().Should().Be(i);
                    r.ReadSingle().Should().Be(i);
                    r.ReadDouble().Should().Be(i);
                }
            });
    }

    [Theory]
    [InlineData(sbyte.MaxValue)]
    [InlineData(sbyte.MinValue)]
    public void WriteSByte_Value_WritesOneByteAndReadsBack(sbyte value)
    {
        WriteThenRead(
            w =>
            {
                w.Write(value);
                w.Length.Should().Be(sizeof(sbyte));
            },
            r => r.ReadSByte().Should().Be(value));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void WriteSha1_AllComponentsEqual_ReadsBackValue(uint component)
    {
        var sha1 = new Sha1(component, component, component, component, component);

        WriteThenRead(w => w.Write(sha1), r => r.ReadSha1().Should().Be(sha1));
    }

    [Theory]
    [InlineData(200F)]
    [InlineData(200.8888111F)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void WriteSingle_BothByteOrders_ReadsBackValue(float value)
    {
        AssertReadsBackInBothByteOrders(value, sizeof(float), (w, v, e) => w.Write(v, e), r => r.ReadSingle(), (r, e) => r.ReadSingle(e), (r, e, o) => r.ReadSingleAt(e, o));
    }

    [Fact]
    public void WriteSingle_NegativeZero_ReadsBackNegativeZero()
    {
        // -0.0f equals 0.0f, so only the raw bits show whether the sign survives.
        WriteThenRead(w => w.Write(-0.0f), r => BitConverter.GetBytes(r.ReadSingle()).Should().Equal(BitConverter.GetBytes(-0.0f)));
    }

    [Theory]
    [InlineData(float.Epsilon)]
    [InlineData(-float.Epsilon)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.NaN)]
    [InlineData(0.0f)]
    [InlineData(1.0f)]
    [InlineData(-1.0f)]
    public void WriteSingle_SpecialValue_ReadsBackSameBits(float value)
    {
        WriteThenRead(
            w => w.Write(value).Write(value, EndianType.BIG),
            r =>
            {
                BitConverter.GetBytes(r.ReadSingle()).Should().Equal(BitConverter.GetBytes(value));
                BitConverter.GetBytes(r.ReadSingle(EndianType.BIG)).Should().Equal(BitConverter.GetBytes(value));
            });
    }

    [Theory]
    [InlineData(200)]
    [InlineData(ushort.MaxValue)]
    [InlineData(ushort.MinValue)]
    public void WriteUInt16_BothByteOrders_ReadsBackValue(ushort value)
    {
        AssertReadsBackInBothByteOrders(value, sizeof(ushort), (w, v, e) => w.Write(v, e), r => r.ReadUInt16(), (r, e) => r.ReadUInt16(e), (r, e, o) => r.ReadUInt16At(e, o));
    }

    [Theory]
    [InlineData(200u)]
    [InlineData(uint.MaxValue)]
    [InlineData(uint.MinValue)]
    public void WriteUInt32_BothByteOrders_ReadsBackValue(uint value)
    {
        AssertReadsBackInBothByteOrders(value, sizeof(uint), (w, v, e) => w.Write(v, e), r => r.ReadUInt32(), (r, e) => r.ReadUInt32(e), (r, e, o) => r.ReadUInt32At(e, o));
    }

    [Theory]
    [InlineData(200ul)]
    [InlineData(ulong.MaxValue)]
    [InlineData(ulong.MinValue)]
    public void WriteUInt64_BothByteOrders_ReadsBackValue(ulong value)
    {
        AssertReadsBackInBothByteOrders(value, sizeof(ulong), (w, v, e) => w.Write(v, e), r => r.ReadUInt64(), (r, e) => r.ReadUInt64(e), (r, e, o) => r.ReadUInt64At(e, o));
    }

    [Fact]
    public void Write_MixedByteOrdersInOneStream_ReadsBackEachValue()
    {
        WriteThenRead(
            w => w
                .Write((short)100, EndianType.LITTLE)
                .Write((short)200, EndianType.BIG)
                .Write(300, EndianType.LITTLE)
                .Write(400, EndianType.BIG)
                .Write(500L, EndianType.LITTLE)
                .Write(600L, EndianType.BIG),
            r =>
            {
                r.ReadInt16(EndianType.LITTLE).Should().Be(100);
                r.ReadInt16(EndianType.BIG).Should().Be(200);
                r.ReadInt32(EndianType.LITTLE).Should().Be(300);
                r.ReadInt32(EndianType.BIG).Should().Be(400);
                r.ReadInt64(EndianType.LITTLE).Should().Be(500L);
                r.ReadInt64(EndianType.BIG).Should().Be(600L);
            });
    }

    public static TheoryData<byte[]> ByteArrays =>
    [
        new byte[] { 0x12, 0x13, 0x14 },
        new byte[] { 1, 2, 3, 4, 5 },
        new byte[] { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 },
        RandomBytes(1),
        RandomBytes(100),
        RandomBytes(1000),
        RandomBytes(10000)
    ];
}