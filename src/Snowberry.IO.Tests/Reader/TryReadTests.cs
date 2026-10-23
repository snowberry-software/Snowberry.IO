using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;
using Snowberry.IO.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for the <c>TryRead*</c> methods of the readers.
/// </summary>
public class TryReadTests
{
    private static readonly Dictionary<string, Func<IEndianReader, (bool Success, object Value)>> s_TryReads = new()
    {
        ["Byte"] = r => (r.TryReadByte(out byte value), value),
        ["Int16"] = r => (r.TryReadInt16(out short value), value),
        ["UInt16"] = r => (r.TryReadUInt16(out ushort value), value),
        ["Int32"] = r => (r.TryReadInt32(out int value), value),
        ["UInt32"] = r => (r.TryReadUInt32(out uint value), value),
        ["Int64"] = r => (r.TryReadInt64(out long value), value),
        ["UInt64"] = r => (r.TryReadUInt64(out ulong value), value),
        ["Single"] = r => (r.TryReadSingle(out float value), value),
        ["Double"] = r => (r.TryReadDouble(out double value), value),
        ["Guid"] = r => (r.TryReadGuid(out var value), value),
        ["Sha1"] = r => (r.TryReadSha1(out var value), value)
    };

    private static void AssertTryReadReturnsWrittenValue<T>(T expected, Action<IEndianWriter, T> write, Func<IEndianReader, (bool Success, T Value)> tryRead)
    {
        using var reader = CreateReader(WriteBytes(w => write(w, expected)));

        var result = tryRead(reader);

        result.Success.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public void TryReadByte_EnoughData_ReturnsTrueAndValue()
    {
        using var reader = CreateReader([42]);

        bool success = reader.TryReadByte(out byte value);

        success.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void TryReadByte_NonSeekableEmptyStream_ReturnsFalseAndZero()
    {
        using var reader = CreateReader(new ChunkedReadStream([], maxBytesPerRead: 1, canSeek: false));

        bool success = reader.TryReadByte(out byte value);

        success.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public void TryReadDouble_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue(3.141592653589793, (w, v) => w.Write(v), r => (r.TryReadDouble(out double v), v));
    }

    [Fact]
    public void TryReadGuid_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue(Guid.NewGuid(), (w, v) => w.Write(v), r => (r.TryReadGuid(out var v), v));
    }

    [Fact]
    public void TryReadInt16_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue((short)12345, (w, v) => w.Write(v), r => (r.TryReadInt16(out short v), v));
    }

    [Fact]
    public void TryReadInt32_AnalyzerSetBetweenReads_NotifiesOnlyReadsWhileSet()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(SequentialBytes(12));

        reader.TryReadInt32(out _).Should().BeTrue();
        reader.Analyzer = analyzer;
        reader.TryReadInt32(out int value, EndianType.BIG).Should().BeTrue();
        reader.Analyzer = null;
        reader.TryReadInt32(out _).Should().BeTrue();

        value.Should().Be(0x04050607);
        analyzer.Bytes.Should().Equal(4, 5, 6, 7);
    }

    [Fact]
    public void TryReadInt32_AtEndOfStream_ReturnsFalseWithoutMovingStreamPosition()
    {
        using var stream = new SeekCountingStream(SequentialBytes(4));
        using var reader = CreateReader(stream);
        reader.ReadInt32(EndianType.LITTLE);

        reader.TryReadInt32(out int value).Should().BeFalse();

        value.Should().Be(0);
        stream.PositionChanges.Should().Be(0);
        reader.Position.Should().Be(4);
    }

    [Fact]
    public void TryReadInt32_BigEndian_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue(123456789, (w, v) => w.Write(v, EndianType.BIG), r => (r.TryReadInt32(out int v, EndianType.BIG), v));
    }

    [Fact]
    public void TryReadInt32_EnoughData_DoesNotMoveStreamPosition()
    {
        using var stream = new SeekCountingStream(SequentialBytes(8));
        using var reader = CreateReader(stream);

        reader.TryReadInt32(out _).Should().BeTrue();
        reader.TryReadInt32(out _, EndianType.BIG).Should().BeTrue();

        stream.PositionChanges.Should().Be(0);
        reader.Position.Should().Be(8);
    }

    [Fact]
    public void TryReadInt32_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue(123456789, (w, v) => w.Write(v), r => (r.TryReadInt32(out int v), v));
    }

    [Fact]
    public void TryReadInt32_InsideRegionViewWithPartialData_ReturnsFalseAndRestoresPosition()
    {
        byte[] data = SequentialBytes(16);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data);
        reader.EnableRegionView(new RegionRange { StartPosition = 4, Size = 7 });

        reader.TryReadInt32(out int first).Should().BeTrue();
        reader.Analyzer = analyzer;
        reader.TryReadInt32(out _).Should().BeFalse();

        first.Should().Be(0x07060504);
        reader.Position.Should().Be(4);
        reader.ActualPosition.Should().Be(8);
        analyzer.TotalBytes.Should().Be(0);

        reader.DisableRegionView();
        reader.TryReadInt32(out int third).Should().BeTrue();
        third.Should().Be(0x0B0A0908);
        analyzer.Bytes.Should().Equal(8, 9, 10, 11);
    }

    [Fact]
    public void TryReadInt32_NonSeekableStreamWithPartialData_ReturnsFalseAndConsumesBytes()
    {
        using var reader = CreateReader(new ChunkedReadStream([0x01, 0x02], maxBytesPerRead: 1, canSeek: false));

        bool success = reader.TryReadInt32(out int value);

        success.Should().BeFalse();
        value.Should().Be(0);
        reader.TryReadByte(out _).Should().BeFalse();
    }

    [Fact]
    public void TryReadInt32_NonSeekableStream_ReturnsTrueAndValue()
    {
        using var reader = CreateReader(new ChunkedReadStream([0x01, 0x02, 0x03, 0x04], maxBytesPerRead: 1, canSeek: false));

        bool success = reader.TryReadInt32(out int value);

        success.Should().BeTrue();
        value.Should().Be(0x04030201);
    }

    [Fact]
    public void TryReadInt32_OneByteReads_ReturnsTrueAndValue()
    {
        using var reader = CreateReader(new ChunkedReadStream([0x01, 0x02, 0x03, 0x04], maxBytesPerRead: 1));

        bool success = reader.TryReadInt32(out int value);

        success.Should().BeTrue();
        value.Should().Be(0x04030201);
        reader.Position.Should().Be(4);
    }

    [Fact]
    public void TryReadInt32_PartialDataInChunksWithoutAnalyzer_ReturnsFalseAndRestoresPosition()
    {
        using var stream = new ChunkedReadStream([9, 1, 2, 3], maxBytesPerRead: 1);
        using var reader = CreateReader(stream);
        reader.ReadByteSafe().Should().Be(9);

        reader.TryReadInt32(out _, EndianType.BIG).Should().BeFalse();

        reader.Position.Should().Be(1);
        stream.Position.Should().Be(1);
        reader.TryReadInt16(out short value, EndianType.BIG).Should().BeTrue();
        value.Should().Be(0x0102);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TryReadInt32_PartialDataInChunks_ReturnsFalseAndRestoresPosition(int maxBytesPerRead)
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new ChunkedReadStream([1, 2, 3], maxBytesPerRead), analyzer: analyzer);

        reader.TryReadInt32(out int value).Should().BeFalse();

        value.Should().Be(0);
        reader.Position.Should().Be(0);
        analyzer.TotalBytes.Should().Be(0);

        reader.ReadBytes(3).Should().Equal(1, 2, 3);
        analyzer.Bytes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void TryReadInt32_PastLastValue_ReturnsFalseAndZero()
    {
        using var reader = CreateReader(WriteBytes(w => w.Write(100).Write(200)));

        reader.TryReadInt32(out int first).Should().BeTrue();
        reader.TryReadInt32(out int second).Should().BeTrue();
        reader.TryReadInt32(out int third).Should().BeFalse();

        first.Should().Be(100);
        second.Should().Be(200);
        third.Should().Be(0);
    }

    [Fact]
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows6.0.6000")]
#endif
    public void TryReadInt32_ProcessMemoryInsideRegionView_ReturnsFalseAtRegionEndAndRestoresPosition()
    {
        Assert.SkipUnless(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows only.");

        using var buffer = new PinnedBuffer([1, 2, 3, 4, 5, 6, 7, 8]);
        using var process = Process.GetCurrentProcess();
        using var reader = new Win32ProcessReader(process.Id, buffer.Address);

        reader.TryReadInt32(out int first, EndianType.BIG).Should().BeTrue();
        reader.EnableRegionView(new RegionRange { StartPosition = buffer.Address + 4, Size = 3 });
        reader.TryReadInt32(out _).Should().BeFalse();

        first.Should().Be(0x01020304);
        reader.Position.Should().Be(0);
        reader.TryReadInt16(out short second).Should().BeTrue();
        second.Should().Be(0x0605);
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void TryReadInt64_ChunkedStream_ReturnsTrueAndValue(EndianType endian)
    {
        byte[] data = WriteBytes(w => w.Write(0x0102030405060708L, endian).Write(0x1112131415161718L, endian));
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead: 3));

        reader.TryReadInt64(out long first, endian).Should().BeTrue();
        reader.TryReadInt64(out long second, endian).Should().BeTrue();
        reader.TryReadInt64(out _, endian).Should().BeFalse();

        first.Should().Be(0x0102030405060708L);
        second.Should().Be(0x1112131415161718L);
        reader.Position.Should().Be(16);
    }

    [Fact]
    public void TryReadInt64_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue(123456789123456789L, (w, v) => w.Write(v), r => (r.TryReadInt64(out long v), v));
    }

    [Fact]
    public void TryReadInt64_OneByteReads_ReturnsTrue()
    {
        using var reader = CreateReader(new ChunkedReadStream([1, 2, 3, 4, 5, 6, 7, 8], maxBytesPerRead: 1));

        bool success = reader.TryReadInt64(out _);

        success.Should().BeTrue();
        reader.Position.Should().Be(8);
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void TryReadPrimitives_Endianness_MatchBinaryPrimitives(EndianType endian)
    {
        byte[] data = [0x81, 0x02, 0xF3, 0x04, 0x45, 0x96, 0x07, 0xA8];
        bool big = endian == EndianType.BIG;
        using var reader = CreateReader(data);

        reader.TryReadInt16(out short int16, endian).Should().BeTrue();
        int16.Should().Be(big ? BinaryPrimitives.ReadInt16BigEndian(data) : BinaryPrimitives.ReadInt16LittleEndian(data));
        reader.Position = 0;
        reader.TryReadUInt16(out ushort uint16, endian).Should().BeTrue();
        uint16.Should().Be(big ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data));
        reader.Position = 0;
        reader.TryReadInt32(out int int32, endian).Should().BeTrue();
        int32.Should().Be(big ? BinaryPrimitives.ReadInt32BigEndian(data) : BinaryPrimitives.ReadInt32LittleEndian(data));
        reader.Position = 0;
        reader.TryReadUInt32(out uint uint32, endian).Should().BeTrue();
        uint32.Should().Be(big ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data));
        reader.Position = 0;
        reader.TryReadInt64(out long int64, endian).Should().BeTrue();
        int64.Should().Be(big ? BinaryPrimitives.ReadInt64BigEndian(data) : BinaryPrimitives.ReadInt64LittleEndian(data));
        reader.Position = 0;
        reader.TryReadUInt64(out ulong uint64, endian).Should().BeTrue();
        uint64.Should().Be(big ? BinaryPrimitives.ReadUInt64BigEndian(data) : BinaryPrimitives.ReadUInt64LittleEndian(data));
        reader.Position = 0;
        reader.TryReadDouble(out double float64, endian).Should().BeTrue();
        float64.Should().Be(BitConverter.Int64BitsToDouble(big ? BinaryPrimitives.ReadInt64BigEndian(data) : BinaryPrimitives.ReadInt64LittleEndian(data)));
    }

    [Fact]
    public void TryReadSByte_EnoughData_ReturnsTrueAndValue()
    {
        using var reader = CreateReader([250]);

        bool success = reader.TryReadSByte(out sbyte value);

        success.Should().BeTrue();
        value.Should().Be(-6);
    }

    [Fact]
    public void TryReadSha1_EnoughData_ReturnsTrueAndValue()
    {
        var sha1 = new Sha1(RandomBytes(Sha1.c_StructSize));

        AssertTryReadReturnsWrittenValue(sha1, (w, v) => w.Write(v), r => (r.TryReadSha1(out var v), v));
    }

    [Fact]
    public void TryReadSingle_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue(3.14159f, (w, v) => w.Write(v), r => (r.TryReadSingle(out float v), v));
    }

    [Fact]
    public void TryReadUInt16_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue((ushort)65000, (w, v) => w.Write(v), r => (r.TryReadUInt16(out ushort v), v));
    }

    [Fact]
    public void TryReadUInt32_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue(4000000000u, (w, v) => w.Write(v), r => (r.TryReadUInt32(out uint v), v));
    }

    [Fact]
    public void TryReadUInt64_EnoughData_ReturnsTrueAndValue()
    {
        AssertTryReadReturnsWrittenValue(10000000000000000000ul, (w, v) => w.Write(v), r => (r.TryReadUInt64(out ulong v), v));
    }

    [Fact]
    public void TryRead_AtEndOfStream_ReturnsFalseForEveryType()
    {
        using var reader = CreateReader(new byte[1]);
        reader.ReadByte();

        reader.TryReadByte(out _).Should().BeFalse();
        reader.TryReadInt16(out _).Should().BeFalse();
        reader.TryReadInt32(out _).Should().BeFalse();
        reader.TryReadInt64(out _).Should().BeFalse();
        reader.TryReadSingle(out _).Should().BeFalse();
        reader.TryReadDouble(out _).Should().BeFalse();
        reader.TryReadGuid(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Byte", 0)]
    [InlineData("Int16", 1)]
    [InlineData("UInt16", 1)]
    [InlineData("Int32", 3)]
    [InlineData("UInt32", 3)]
    [InlineData("Int64", 7)]
    [InlineData("UInt64", 7)]
    [InlineData("Single", 3)]
    [InlineData("Double", 7)]
    [InlineData("Guid", 10)]
    [InlineData("Sha1", 15)]
    public void TryRead_NotEnoughData_ReturnsFalseAndDefaultWithoutMoving(string type, int length)
    {
        using var reader = CreateReader(SequentialBytes(length));

        var result = s_TryReads[type](reader);

        result.Success.Should().BeFalse();
        result.Value.Should().Be(Activator.CreateInstance(result.Value.GetType()));
        reader.Position.Should().Be(0);
    }
}