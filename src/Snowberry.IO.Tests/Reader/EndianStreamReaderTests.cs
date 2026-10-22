using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for <see cref="EndianStreamReader"/>: reading bytes and primitives, buffers, copying and disposal.
/// </summary>
public class EndianStreamReaderTests
{
    private static readonly byte[] s_MixedBytes = [0x81, 0x02, 0xF3, 0x04, 0x45, 0x96, 0x07, 0xA8, 0x19, 0x2A, 0xBB, 0x3C, 0xCD, 0x4E, 0xDF, 0x70];

    private static readonly Dictionary<string, Action<IEndianReader>> s_PrimitiveReads = new()
    {
        ["Int32"] = r => r.ReadInt32(),
        ["Int64"] = r => r.ReadInt64(),
        ["Guid"] = r => r.ReadGuid(),
        ["Sha1"] = r => r.ReadSha1()
    };

    [Fact]
    public void BaseStream_SeekInsideRegionView_StaysInsideTheRegion()
    {
        using var reader = CreateReader([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
        reader.EnableRegionView(new RegionRange { StartPosition = 2, Size = 4 });

        reader.BaseStream.Position = 2;

        reader.Position.Should().Be(2);
        reader.BaseStream.Length.Should().Be(4);
        reader.ReadBytes(10).Should().Equal(4, 5);
        reader.ActualPosition.Should().Be(6);
    }

    [Fact]
    public void BaseStream_WithStream_SharesThePositionWithTheReader()
    {
        var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);
        using var reader = CreateReader(stream);

        reader.Stream.Should().BeSameAs(stream);
        reader.BaseStream.Should().NotBeSameAs(stream);
        ((IEndianReader)reader).BaseStream.Should().BeSameAs(reader.BaseStream);
        reader.BaseStream.CanRead.Should().BeTrue();
        reader.BaseStream.CanSeek.Should().BeTrue();
        reader.BaseStream.CanWrite.Should().BeFalse();

        reader.BaseStream.Position = 6;

        reader.Position.Should().Be(6);
        reader.ReadByte().Should().Be(7);
        reader.BaseStream.Position.Should().Be(7);
    }

    [Fact]
    public void BaseStream_WithoutStream_IsNotSeekable()
    {
        using var reader = new EndianStreamReader(null);

        reader.BaseStream.CanSeek.Should().BeFalse();
        reader.Stream.Should().BeNull();
        reader.Length.Should().Be(0);
        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void Buffer_CustomBufferSize_IsAtLeastThatLong()
    {
        using var reader = new EndianStreamReader(new MemoryStream(), null, 100);

        reader.Buffer.IsEmpty.Should().BeFalse();
        reader.Buffer.Length.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void Constructor_SharedStream_StartsAtStreamPosition()
    {
        var stream = new MemoryStream(WriteBytes(w => w.Write(100).Write(200).Write(300)));

        using var first = new EndianStreamReader(stream, null, 0, Encoding.UTF8);
        first.ReadInt32().Should().Be(100);

        using var second = new EndianStreamReader(stream, null, 0, Encoding.UTF8);
        second.ReadInt32().Should().Be(200);
        second.ReadInt32().Should().Be(300);
    }

    [Fact]
    public void CopyTo_AnalyzerSetAndRemovedBetweenCopies_DecryptsOnlyWhileSet()
    {
        byte[] plain = RandomBytes(1000);
        byte[] data = [.. plain.Take(400), .. plain.Skip(400).Select(b => (byte)(b ^ 0x21))];
        using var reader = CreateReader(data);
        var first = new MemoryStream();
        var second = new MemoryStream();
        var third = new MemoryStream();

        reader.CopyTo(first, 400);
        reader.Analyzer = new XorAnalyzer(0x21);
        reader.CopyTo(second);
        reader.Analyzer = null;
        reader.Position = 0;
        reader.CopyTo(third);

        first.ToArray().Should().Equal(plain.Take(400));
        second.ToArray().Should().Equal(plain.Skip(400));
        third.ToArray().Should().Equal(data);
    }

    [Fact]
    public void CopyTo_DisposedDestination_ThrowsObjectDisposedException()
    {
        using var reader = CreateReader(RandomBytes(10));
        var destination = new MemoryStream();
        destination.Dispose();

        FluentActions.Invoking(() => reader.CopyTo(destination)).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void CopyTo_FiveMegabytes_CopiesAllBytes()
    {
        byte[] data = RandomBytes(5 * 1024 * 1024);
        using var reader = CreateReader(data);
        var destination = new MemoryStream();

        reader.CopyTo(destination);

        destination.Length.Should().Be(data.Length);
        destination.ToArray().Should().Equal(data);
    }

    [Fact]
    public void CopyTo_FromMiddle_CopiesRemainingBytesAndMovesToEnd()
    {
        byte[] data = RandomBytes(300_000);
        using var reader = CreateReader(data);
        var destination = new MemoryStream();
        reader.Position = 1000;

        reader.CopyTo(destination);

        destination.ToArray().Should().Equal(data.Skip(1000));
        reader.Position.Should().Be(data.Length);
        reader.ReadByteSafe().Should().Be(-1);
    }

    [Fact]
    public void CopyTo_Length_AdvancesPositionByLength()
    {
        using var reader = CreateReader(new byte[100]);
        var destination = new MemoryStream();

        reader.CopyTo(destination, 50);

        reader.Position.Should().Be(50);
        destination.Length.Should().Be(50);
    }

    [Fact]
    public void CopyTo_Length_CopiesOnlyLength()
    {
        using var reader = CreateReader([1, 2, 3, 4, 5, 6, 7, 8]);
        var destination = new MemoryStream();

        reader.CopyTo(destination, 5);

        destination.ToArray().Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void CopyTo_MemoryStreamSubclass_CopiesRemainingBytes()
    {
        byte[] data = RandomBytes(200_000);
        using var reader = CreateReader(new SeekCountingStream(data));
        var destination = new MemoryStream();
        reader.Position = 10;

        reader.CopyTo(destination);

        destination.ToArray().Should().Equal(data.Skip(10));
        reader.Position.Should().Be(data.Length);
    }

    [Fact]
    public void CopyTo_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        using var reader = CreateReader(new byte[100]);

        FluentActions.Invoking(() => reader.CopyTo(new MemoryStream(), -1)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(100, -1)]
    [InlineData(3, 0)]
    public void CopyTo_NonPositiveBufferSize_ThrowsArgumentOutOfRangeException(int length, int bufferSize)
    {
        using var reader = CreateReader(new byte[length]);

        FluentActions.Invoking(() => reader.CopyTo(new MemoryStream(), length, bufferSize)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CopyTo_NullDestination_ThrowsArgumentNullException()
    {
        using var reader = CreateReader([1, 2, 3]);

        FluentActions.Invoking(() => reader.CopyTo(null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void CopyTo_ReadOnlyDestination_ThrowsNotSupportedExceptionUnlessNothingIsLeft()
    {
        using var reader = CreateReader(RandomBytes(10));
        var destination = new MemoryStream(new byte[16], writable: false);

        FluentActions.Invoking(() => reader.CopyTo(destination)).Should().ThrowExactly<NotSupportedException>();

        reader.Position = 10;
        FluentActions.Invoking(() => reader.CopyTo(destination)).Should().NotThrow();
    }

    [Fact]
    public void CopyTo_SmallBufferSize_CopiesAllBytes()
    {
        using var reader = CreateReader(new byte[1000]);
        var destination = new MemoryStream();

        reader.CopyTo(destination, 1000, bufferSize: 100);

        destination.Length.Should().Be(1000);
    }

    [Fact]
    public void CopyTo_WholeStream_CopiesAllBytes()
    {
        using var reader = CreateReader([1, 2, 3, 4, 5]);
        var destination = new MemoryStream();

        reader.CopyTo(destination);

        destination.ToArray().Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Encoding_PassedToConstructor_IsReturned()
    {
        using var reader = CreateReader([], Encoding.UTF8);

        reader.Encoding.Should().Be(Encoding.UTF8);
    }

    [Fact]
    public void EnsureBufferSize_GrowingRequests_GrowsBufferToEachSize()
    {
        using var reader = new EndianStreamReader(new MemoryStream(), null, 16);

        reader.Buffer.Length.Should().Be(BaseEndianReader.c_MinBufferSize);

        reader.EnsureBufferSize(32);
        reader.Buffer.Length.Should().Be(32);

        reader.EnsureBufferSize(64);
        reader.Buffer.Length.Should().Be(64);

        reader.EnsureBufferSize(128);
        reader.Buffer.Length.Should().Be(128);

        reader.EnsureBufferSize(256);
        reader.Buffer.Length.Should().Be(256);
    }

    [Fact]
    public void EnsureBufferSize_NegativeSize_ThrowsArgumentOutOfRangeException()
    {
        using var reader = CreateReader([]);

        FluentActions.Invoking(() => reader.EnsureBufferSize(-1)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, BaseEndianReader.c_MinBufferSize)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(1000, 1000)]
    [InlineData(BaseEndianReader.c_MinBufferSize - 1, BaseEndianReader.c_MinBufferSize)]
    public void EnsureBufferSize_RequestedSize_SetsBufferLength(int requestedSize, int expectedLength)
    {
        using var reader = CreateReader([]);

        reader.EnsureBufferSize(requestedSize);

        reader.Buffer.Length.Should().Be(expectedLength);
    }

    [Fact]
    public void ParameterlessReads_AnyData_ReadLittleEndian()
    {
        byte[] data = s_MixedBytes;
        using var reader = CreateReader(data);

        reader.ReadInt64().Should().Be(BinaryPrimitives.ReadInt64LittleEndian(data));
        reader.Position = 0;
        reader.ReadGuid().Should().Be(new Guid(data));
        reader.Position = 0;
        reader.TryReadInt32(out int value).Should().BeTrue();
        value.Should().Be(BinaryPrimitives.ReadInt32LittleEndian(data));
    }

    [Fact]
    public void Position_NullStream_ThrowsInvalidOperationException()
    {
        using var reader = new EndianStreamReader(null);

        FluentActions.Invoking(() => _ = reader.Position).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void Read7BitEncodedInt64_MoreThanTenBytes_ThrowsFormatException()
    {
        using var reader = CreateReader([.. Enumerable.Repeat((byte)0xFF, 10)]);

        FluentActions.Invoking(() => reader.Read7BitEncodedInt64()).Should().ThrowExactly<FormatException>();
    }

    [Fact]
    public void Read7BitEncodedInt_MoreThanFiveBytes_ThrowsFormatException()
    {
        using var reader = CreateReader([.. Enumerable.Repeat((byte)0xFF, 5)]);

        FluentActions.Invoking(() => reader.Read7BitEncodedInt()).Should().ThrowExactly<FormatException>();
    }

    [Fact]
    public void ReadBoolean_NonZeroByte_ReturnsTrue()
    {
        using var reader = CreateReader([0, 1, 2, 255]);

        reader.ReadBoolean().Should().BeFalse();
        reader.ReadBoolean().Should().BeTrue();
        reader.ReadBoolean().Should().BeTrue();
        reader.ReadBoolean().Should().BeTrue();
    }

    [Fact]
    public void ReadByteSafe_EmptyStream_ReturnsMinusOne()
    {
        using var reader = CreateReader([]);

        reader.ReadByteSafe().Should().Be(-1);
    }

    [Fact]
    public void ReadByteSafe_PastEnd_ReturnsMinusOne()
    {
        using var reader = CreateReader([1, 2, 3]);

        reader.ReadByteSafe().Should().Be(1);
        reader.ReadByteSafe().Should().Be(2);
        reader.ReadByteSafe().Should().Be(3);
        reader.ReadByteSafe().Should().Be(-1);
        reader.ReadByteSafe().Should().Be(-1);
    }

    [Fact]
    public void ReadByte_EmptyStream_ThrowsEndOfStreamException()
    {
        using var reader = CreateReader([]);

        FluentActions.Invoking(() => reader.ReadByte()).Should().ThrowExactly<EndOfStreamException>();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void ReadByte_TenThousandBytes_ReturnsEachByte()
    {
        byte[] data = RandomBytes(10000);
        using var reader = CreateReader(data);

        for (int i = 0; i < data.Length; i++)
            reader.ReadByte().Should().Be(data[i]);
    }

    [Fact]
    public void ReadBytes_CountBeyondEnd_ReturnsAvailableBytes()
    {
        byte[] data = [1, 2, 3];
        using var reader = CreateReader(data);

        byte[] result = reader.ReadBytes(10);

        result.Length.Should().Be(3);
        result.Should().Equal(data);
    }

    [Fact]
    public void ReadBytes_EmptyStream_ReturnsEmptyArray()
    {
        using var reader = CreateReader([]);

        reader.ReadBytes(10).Should().BeEmpty();
    }

    [Fact]
    public void ReadBytes_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        using var reader = CreateReader([]);

        FluentActions.Invoking(() => reader.ReadBytes(-1)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadBytes_OneByteReads_ReturnsAllBytes()
    {
        byte[] data = SequentialBytes(50);
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead: 1));

        reader.ReadBytes(50).Should().Equal(data);
    }

    [Fact]
    public void ReadBytes_ZeroCount_ReturnsEmptyArray()
    {
        using var reader = CreateReader([1, 2, 3]);

        reader.ReadBytes(0).Should().BeEmpty();
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void ReadEndianOverloads_Endianness_MatchBinaryPrimitives(EndianType endian)
    {
        byte[] data = s_MixedBytes;
        bool big = endian == EndianType.BIG;
        using var reader = CreateReader(data);

        reader.ReadInt16(endian).Should().Be(big ? BinaryPrimitives.ReadInt16BigEndian(data) : BinaryPrimitives.ReadInt16LittleEndian(data));
        reader.Position = 0;
        reader.ReadUInt16(endian).Should().Be(big ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data));
        reader.Position = 0;
        reader.ReadInt32(endian).Should().Be(big ? BinaryPrimitives.ReadInt32BigEndian(data) : BinaryPrimitives.ReadInt32LittleEndian(data));
        reader.Position = 0;
        reader.ReadUInt32(endian).Should().Be(big ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data));
        reader.Position = 0;
        reader.ReadInt64(endian).Should().Be(big ? BinaryPrimitives.ReadInt64BigEndian(data) : BinaryPrimitives.ReadInt64LittleEndian(data));
        reader.Position = 0;
        reader.ReadUInt64(endian).Should().Be(big ? BinaryPrimitives.ReadUInt64BigEndian(data) : BinaryPrimitives.ReadUInt64LittleEndian(data));

        int singleBits = big ? BinaryPrimitives.ReadInt32BigEndian(data) : BinaryPrimitives.ReadInt32LittleEndian(data);
        reader.Position = 0;
        reader.ReadSingle(endian).Should().Be(BitConverter.ToSingle(BitConverter.GetBytes(singleBits), 0));

        long doubleBits = big ? BinaryPrimitives.ReadInt64BigEndian(data) : BinaryPrimitives.ReadInt64LittleEndian(data);
        reader.Position = 0;
        reader.ReadDouble(endian).Should().Be(BitConverter.Int64BitsToDouble(doubleBits));

#if NET5_0_OR_GREATER
        reader.Position = 0;
        reader.ReadHalf(endian).Should().Be(big ? BinaryPrimitives.ReadHalfBigEndian(data) : BinaryPrimitives.ReadHalfLittleEndian(data));
#endif

        var expectedGuid = big
            ? new Guid(BinaryPrimitives.ReadInt32BigEndian(data), BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(4)), BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(6)), [.. data.Skip(8)])
            : new Guid(data);
        reader.Position = 0;
        reader.ReadGuid(endian).Should().Be(expectedGuid);
        reader.Position.Should().Be(16);
    }

    [Fact]
    public void ReadExactly_ArrayInsideRegionView_FillsRangeAndNotifiesAnalyzer()
    {
        byte[] data = SequentialBytes(64);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead: 5), analyzer: analyzer);
        reader.EnableRegionView(new RegionRange { StartPosition = 10, Size = 20 });
        byte[] buffer = new byte[24];

        reader.ReadExactly(buffer, 2, 20);

        buffer.Skip(2).Take(20).Should().Equal(data.Skip(10).Take(20));
        analyzer.Bytes.Should().Equal(data.Skip(10).Take(20));
        reader.Position.Should().Be(20);
        FluentActions.Invoking(() => reader.ReadExactly(buffer, 0, 1)).Should().ThrowExactly<EndOfStreamException>();
    }

    [Fact]
    public void ReadExactly_EmptyBufferAtEndOfStream_DoesNothing()
    {
        using var reader = CreateReader([]);

        FluentActions.Invoking(() => reader.ReadExactly([], 0, 0)).Should().NotThrow();
    }

    [Fact]
    public void ReadExactly_EnoughData_FillsBuffer()
    {
        using var reader = CreateReader([1, 2, 3, 4, 5]);
        byte[] buffer = new byte[5];

        reader.ReadExactly(buffer, 0, 5);

        buffer.Should().Equal(1, 2, 3, 4, 5);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void ReadExactly_LargeArrayInChunksWithXorAnalyzer_FillsRangeWithDecryptedBytes(int maxBytesPerRead)
    {
        byte[] plain = RandomBytes(200_000);
        byte[] encrypted = [.. plain.Select(b => (byte)(b ^ 0x3C))];
        using var reader = CreateReader(new ChunkedReadStream(encrypted, maxBytesPerRead), analyzer: new XorAnalyzer(0x3C));
        byte[] buffer = new byte[plain.Length + 10];

        reader.ReadExactly(buffer, 4, plain.Length - 1);

        buffer.Skip(4).Take(plain.Length - 1).Should().Equal(plain.Take(plain.Length - 1));
        buffer.Take(4).Should().AllBeEquivalentTo((byte)0);
        buffer.Skip(4 + plain.Length - 1).Should().AllBeEquivalentTo((byte)0);
        reader.Position.Should().Be(plain.Length - 1);
    }

    [Fact]
    public void ReadExactly_NonSeekableStreamEndingEarly_ThrowsEndOfStreamException()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new ChunkedReadStream([1, 2, 3, 4, 5], maxBytesPerRead: 2, canSeek: false), analyzer: analyzer);

        FluentActions.Invoking(() => reader.ReadExactly(new byte[8], 0, 8)).Should().ThrowExactly<EndOfStreamException>();

        analyzer.Bytes.Should().Equal(1, 2, 3, 4, 5);
        reader.ReadByteSafe().Should().Be(-1);
    }

    [Fact]
    public void ReadExactly_NonZeroOffset_FillsBufferFromOffset()
    {
        using var reader = CreateReader([10, 20, 30]);
        byte[] buffer = new byte[6];

        reader.ReadExactly(buffer, 2, 3);

        buffer.Should().Equal(0, 0, 10, 20, 30, 0);
    }

    [Fact]
    public void ReadExactly_NotEnoughData_ThrowsEndOfStreamException()
    {
        using var reader = CreateReader([1, 2, 3]);

        FluentActions.Invoking(() => reader.ReadExactly(new byte[10], 0, 10)).Should().ThrowExactly<EndOfStreamException>();
    }

    [Fact]
    public void ReadExactly_OneByteReadsEndingEarly_ThrowsAndRestoresPosition()
    {
        using var reader = CreateReader(new ChunkedReadStream([1, 2, 3, 4, 5], maxBytesPerRead: 1));

        FluentActions.Invoking(() => reader.ReadExactly(new byte[8], 0, 8)).Should().ThrowExactly<EndOfStreamException>();

        reader.Position.Should().Be(0);
        reader.ActualPosition.Should().Be(0);
    }

    [Fact]
    public void ReadExactly_Span_FillsSpan()
    {
        byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8];
        using var reader = CreateReader(WriteBytes(w => w.Write(expected)));
        Span<byte> buffer = stackalloc byte[8];

        reader.ReadExactly(buffer);

        buffer.ToArray().Should().Equal(expected);
    }

    [Fact]
    public void ReadInt16_AfterReadUntilEnd_ThrowsEndOfStreamException()
    {
        using var reader = CreateReader([33, 92, 82, 33]);
        reader.ReadUntilEnd();

        FluentActions.Invoking(() => reader.ReadInt16()).Should().ThrowExactly<EndOfStreamException>();
    }

    [Fact]
    public void ReadInt32_NonSeekableStreamWithPartialData_ThrowsEndOfStreamException()
    {
        using var reader = CreateReader(new ChunkedReadStream([0x01, 0x02], canSeek: false));

        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<EndOfStreamException>();
    }

    [Fact]
    public void ReadInt32_NonSeekableStream_ReturnsValue()
    {
        using var reader = CreateReader(new ChunkedReadStream([0x01, 0x02, 0x03, 0x04], canSeek: false));

        reader.ReadInt32().Should().Be(0x04030201);
    }

    [Fact]
    public void ReadInt64_OneByteReadsEndingEarly_KeepsPositionInSyncWithStream()
    {
        using var stream = new ChunkedReadStream([1, 2, 3, 4, 5], maxBytesPerRead: 1);
        using var reader = CreateReader(stream);

        FluentActions.Invoking(() => reader.ReadInt64()).Should().ThrowExactly<EndOfStreamException>();

        reader.Position.Should().Be(5);
        reader.ActualPosition.Should().Be(5);
        stream.Position.Should().Be(5);
    }

    [Theory]
    [InlineData("Int32", 2)]
    [InlineData("Int64", 4)]
    [InlineData("Guid", 10)]
    [InlineData("Sha1", 10)]
    public void ReadPrimitive_NotEnoughData_ThrowsEndOfStreamException(string type, int length)
    {
        var read = s_PrimitiveReads[type];
        using var reader = CreateReader(SequentialBytes(length));

        FluentActions.Invoking(() => read(reader)).Should().ThrowExactly<EndOfStreamException>();
    }

    [Fact]
    public void ReadSByte_EmptyStream_ThrowsEndOfStreamException()
    {
        using var reader = CreateReader([]);

        FluentActions.Invoking(() => reader.ReadSByte()).Should().ThrowExactly<EndOfStreamException>();
    }

    [Theory]
    [InlineData(7)]
    [InlineData(100_000)]
    public void ReadUntilEnd_ChunkedStreamWithAnalyzer_ReturnsAllBytesAndNotifiesEachOnce(int maxBytesPerRead)
    {
        byte[] data = RandomBytes(300_000);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), analyzer: analyzer);
        reader.Position = 3;

        byte[] result = reader.ReadUntilEnd();

        result.Should().Equal(data.Skip(3));
        analyzer.Bytes.Should().Equal(data.Skip(3));
        reader.Position.Should().Be(data.Length);
    }

    [Fact]
    public void ReadUntilEnd_EmptyStream_ReturnsEmptyArray()
    {
        using var reader = CreateReader([]);

        reader.ReadUntilEnd().Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void ReadUntilEnd_FiveMegabytes_ReturnsAllBytes()
    {
        byte[] data = RandomBytes(5 * 1024 * 1024);
        using var reader = CreateReader(data);

        byte[] result = reader.ReadUntilEnd();

        result.Length.Should().Be(data.Length);
        result.Should().Equal(data);
    }

    [Fact]
    public void ReadUntilEnd_FromMiddle_ReturnsRemainingBytesAndMovesToEnd()
    {
        byte[] data = RandomBytes(100);
        using var reader = CreateReader(data);
        reader.Position = 50;

        byte[] remaining = reader.ReadUntilEnd();

        remaining.Length.Should().Be(50);
        remaining.Should().Equal(data.Skip(50));
        reader.Position.Should().Be(100);
    }

    [Theory]
    [InlineData(48)]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    public void ReadUntilEnd_FromStart_ReturnsAllBytes(int size)
    {
        byte[] data = RandomBytes(size);
        using var reader = CreateReader(WriteBytes(w => w.Write(data)));

        byte[] result = reader.ReadUntilEnd();

        result.Length.Should().Be(data.Length);
        result.Should().Equal(data);
    }

    [Fact]
    public void ReadUntilEnd_InsideRegionViewWithXorAnalyzer_ReturnsDecryptedRegionBytes()
    {
        byte[] plain = RandomBytes(1000);
        byte[] encrypted = [.. plain.Select(b => (byte)(b ^ 0x77))];
        using var reader = CreateReader(encrypted, analyzer: new XorAnalyzer(0x77));
        reader.EnableRegionView(new RegionRange { StartPosition = 100, Size = 600 });
        reader.Position = 50;

        byte[] result = reader.ReadUntilEnd();

        result.Should().Equal(plain.Skip(150).Take(550));
        reader.Position.Should().Be(600);
        reader.ActualPosition.Should().Be(700);
    }

    [Fact]
    public void ReadUntilEnd_NegativeBufferSize_ThrowsArgumentOutOfRangeException()
    {
        using var reader = CreateReader(new byte[100]);

        FluentActions.Invoking(() => reader.ReadUntilEnd(-1)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadUntilEnd_SmallMaxBufferSize_ReturnsAllBytes()
    {
        using var reader = CreateReader(new byte[1000]);

        byte[] data = reader.ReadUntilEnd(maxBufferSize: 100);

        data.Length.Should().Be(1000);
    }

    [Fact]
    public void ReadUntilEnd_StreamAlreadyAdvanced_ReturnsBytesAfterStreamPosition()
    {
        byte[] data = RandomBytes(1000);
        var stream = new MemoryStream(WriteBytes(w => w.Write(data))) { Position = 500 };
        using var reader = CreateReader(stream);

        byte[] result = reader.ReadUntilEnd();

        result.Length.Should().Be(500);
        result.Should().Equal(data.Skip(500));
    }

    [Fact]
    public void ReadUntilEnd_StreamShorterThanItsLength_ThrowsEndOfStreamException()
    {
        using var reader = CreateReader(new OverstatedLengthStream(RandomBytes(10)));

        FluentActions.Invoking(() => reader.ReadUntilEnd()).Should().ThrowExactly<EndOfStreamException>();
    }

    [Fact]
    public void Read_CountBeyondEnd_ReturnsAvailableByteCount()
    {
        using var reader = CreateReader([1, 2, 3]);
        byte[] buffer = new byte[10];

        int bytesRead = reader.Read(buffer, 0, 10);

        bytesRead.Should().Be(3);
        buffer.Take(3).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Read_NonZeroOffset_FillsBufferFromOffset()
    {
        using var reader = CreateReader([10, 20, 30]);
        byte[] buffer = new byte[10];

        int read = reader.Read(buffer, 3, 3);

        read.Should().Be(3);
        buffer.Should().Equal(0, 0, 0, 10, 20, 30, 0, 0, 0, 0);
    }

    [Fact]
    public void Read_SpanLargerThanData_ReturnsAvailableByteCount()
    {
        byte[] data = [1, 2, 3];
        using var reader = CreateReader(data);
        Span<byte> buffer = stackalloc byte[10];

        int bytesRead = reader.Read(buffer);

        bytesRead.Should().Be(3);
        buffer[..3].ToArray().Should().Equal(data);
    }

    [Fact]
    public void Read_Span_FillsSpan()
    {
        byte[] expected = [1, 2, 3, 4, 5];
        using var reader = CreateReader(WriteBytes(w => w.Write(expected)));
        Span<byte> buffer = stackalloc byte[5];

        int bytesRead = reader.Read(buffer);

        bytesRead.Should().Be(5);
        buffer.ToArray().Should().Equal(expected);
    }

    /// <summary>
    /// A <see cref="MemoryStream"/> whose <see cref="Length"/> is larger than the data it returns.
    /// </summary>
    /// <param name="data">The bytes to read.</param>
    private sealed class OverstatedLengthStream(byte[] data) : MemoryStream(data)
    {
        public override long Length => base.Length + 4;
    }
}