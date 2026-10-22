using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for the reads that go straight to the stream while neither an <see cref="Analyzer"/> nor a region view is
/// active, and for switching between them and the reads that go through the region view and the analyzer.
/// </summary>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class DirectReadTests
{
    private static readonly bool s_IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void ReadInt32_XorAnalyzerSetAndRemovedBetweenReads_DecryptsOnlyWhileSet(EndianType endian)
    {
        byte[] plain = WriteBytes(w => w.Write(0x11223344, endian).Write(0x55667788, endian).Write(0x7AABBCCD, endian).Write((byte)0x42));
        byte[] data = [.. plain.Take(4), .. plain.Skip(4).Take(4).Select(b => (byte)(b ^ 0x5A)), .. plain.Skip(8)];
        using var reader = CreateReader(data);

        reader.ReadInt32(endian).Should().Be(0x11223344);

        reader.Analyzer = new XorAnalyzer(0x5A);
        reader.ReadInt32(endian).Should().Be(0x55667788);

        reader.Analyzer = null;
        reader.ReadInt32(endian).Should().Be(0x7AABBCCD);
        reader.ReadByteSafe().Should().Be(0x42);
    }

    [Fact]
    public void ReadInt64_PartialDataAtEnd_ThrowsAndConsumesAvailableBytes()
    {
        byte[] data = SequentialBytes(13);
        using var stream = new ChunkedReadStream(data, maxBytesPerRead: 2);
        using var reader = CreateReader(stream);

        reader.ReadInt64(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt64BigEndian(data));
        FluentActions.Invoking(() => reader.ReadInt64(EndianType.BIG)).Should().ThrowExactly<EndOfStreamException>();

        reader.Position.Should().Be(13);
        stream.Position.Should().Be(13);
        reader.ReadByteSafe().Should().Be(-1);
    }

    [Fact]
    public void ReadInt64_PartialStreamReadsWithAnalyzer_NotifiesEveryByteOnce()
    {
        byte[] data = SequentialBytes(16);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead: 3), analyzer: analyzer);

        reader.ReadInt64(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt64BigEndian(data));
        reader.ReadInt32(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8)));

        analyzer.Bytes.Should().Equal(data.Take(12));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ReadInt64_PartialStreamReadsWithXorAnalyzer_DecryptsEveryByteOnce(int maxBytesPerRead)
    {
        byte[] plain = WriteBytes(w => w.Write(0x1122334455667788L, EndianType.BIG).Write(0x0102030405060708L, EndianType.LITTLE));
        byte[] data = [.. plain.Select(b => (byte)(b ^ 0x5A))];
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), analyzer: new XorAnalyzer(0x5A));

        reader.ReadInt64(EndianType.BIG).Should().Be(0x1122334455667788L);
        reader.ReadInt64(EndianType.LITTLE).Should().Be(0x0102030405060708L);
    }

    [Fact]
    public void ReadPrimitives_AnalyzerSetBetweenReads_NotifiesOnlyReadsWhileSet()
    {
        byte[] data = SequentialBytes(32);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data);

        reader.ReadInt32(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt32BigEndian(data));
        reader.ReadByteSafe().Should().Be(4);

        reader.Analyzer = analyzer;
        reader.ReadInt16(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(5)));
        reader.ReadByteSafe().Should().Be(7);
        reader.ReadInt64(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(8)));

        reader.Analyzer = null;
        reader.ReadInt32(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16)));
        reader.ReadByteSafe().Should().Be(20);

        analyzer.Bytes.Should().Equal(data.Skip(5).Take(11));
        reader.Position.Should().Be(21);
    }

    [Fact]
    public void ReadPrimitives_AnalyzerSetWhileRegionViewEnabled_NotifiesOnlyRegionBytes()
    {
        byte[] data = SequentialBytes(32);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data);

        reader.EnableRegionView(new RegionRange { StartPosition = 8, Size = 8 });
        reader.ReadInt16(EndianType.LITTLE);
        reader.Analyzer = analyzer;
        reader.ReadInt32(EndianType.LITTLE);
        reader.DisableRegionView();
        reader.ReadInt32(EndianType.LITTLE);
        reader.Analyzer = null;
        reader.EnableRegionView(new RegionRange { StartPosition = 0, Size = 4 });
        reader.ReadInt32(EndianType.LITTLE);

        analyzer.Bytes.Should().Equal(data.Skip(10).Take(8));
    }

    [Fact]
    public void ReadPrimitives_NonSeekableStream_ReturnValuesAndThrowAtEnd()
    {
        byte[] data = SequentialBytes(9);
        using var reader = CreateReader(new NonSeekableMemoryStream(data));

        reader.ReadInt32(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt32BigEndian(data));
        reader.ReadByteSafe().Should().Be(4);
        reader.ReadInt16(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(5)));

        FluentActions.Invoking(() => reader.ReadInt32(EndianType.LITTLE)).Should().ThrowExactly<EndOfStreamException>();
        reader.ReadByteSafe().Should().Be(-1);
    }

    [Fact]
    public void ReadPrimitives_NullStream_ThrowInvalidOperationException()
    {
        using var reader = new EndianStreamReader(null);

        FluentActions.Invoking(() => reader.ReadInt32(EndianType.BIG)).Should().ThrowExactly<InvalidOperationException>();
        FluentActions.Invoking(() => reader.ReadByteSafe()).Should().ThrowExactly<InvalidOperationException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ReadPrimitives_PartialStreamReads_ReturnValuesAndKeepPositionInSyncWithStream(int maxBytesPerRead)
    {
        byte[] data = RandomBytes(64);
        using var stream = new ChunkedReadStream(data, maxBytesPerRead);
        using var reader = CreateReader(stream);

        reader.ReadInt16(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt16BigEndian(data));
        reader.ReadInt32(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(2)));
        reader.ReadInt64(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(6)));
        reader.ReadSha1().Should().Be(new Sha1(data.AsSpan(14, Sha1.c_StructSize)));
        reader.ReadGuid().Should().Be(new Guid(data.Skip(34).Take(16).ToArray()));
        reader.ReadByteSafe().Should().Be(data[50]);

        reader.Position.Should().Be(51);
        stream.Position.Should().Be(51);
    }

    [Fact]
    public void ReadPrimitives_ProcessReaderAnalyzerAndRegionViewSwitchedBetweenReads_HonorEach()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        byte[] data = SequentialBytes(32);
        using var buffer = new PinnedBuffer(data);
        var analyzer = new RecordingAnalyzer();
        using var process = Process.GetCurrentProcess();
        using var reader = new Win32ProcessReader(process.Id, buffer.Address);

        reader.ReadInt32(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt32BigEndian(data));

        reader.Analyzer = analyzer;
        reader.ReadInt16(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(4)));
        reader.ReadByteSafe().Should().Be(6);

        reader.Analyzer = null;
        reader.EnableRegionView(new RegionRange { StartPosition = buffer.Address + 8, Size = 6 });
        reader.ReadInt32(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8)));
        FluentActions.Invoking(() => reader.ReadInt32(EndianType.LITTLE)).Should().ThrowExactly<EndOfStreamException>();
        reader.Position.Should().Be(6);

        reader.DisableRegionView();
        reader.ReadInt16(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(14)));
        reader.Position.Should().Be(buffer.Address + 16);
        analyzer.Bytes.Should().Equal(4, 5, 6);
    }

    [Fact]
    public void ReadPrimitives_RegionViewEnabledAndDisabledBetweenReads_StopAtRegionEndOnlyWhileEnabled()
    {
        byte[] data = SequentialBytes(32);
        using var reader = CreateReader(data);

        reader.ReadInt32(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt32LittleEndian(data));

        reader.EnableRegionView(new RegionRange { StartPosition = 4, Size = 6 });
        reader.ReadInt32(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4)));
        FluentActions.Invoking(() => reader.ReadInt32(EndianType.BIG)).Should().ThrowExactly<EndOfStreamException>();
        reader.ReadByteSafe().Should().Be(-1);
        reader.Position.Should().Be(6);
        reader.ActualPosition.Should().Be(10);

        reader.DisableRegionView();
        reader.ReadInt16(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(10)));
        reader.ReadByteSafe().Should().Be(12);
        reader.ReadInt64(EndianType.LITTLE).Should().Be(BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(13)));
        reader.Position.Should().Be(21);
    }

    [Fact]
    public void ReadPrimitives_StreamMovedOutsideReader_ReadFromStreamPosition()
    {
        byte[] data = SequentialBytes(32);
        using var stream = new MemoryStream(data);
        using var reader = CreateReader(stream);

        reader.ReadInt32(EndianType.LITTLE);
        stream.Position = 16;
        reader.ReadInt32(EndianType.BIG).Should().Be(BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16)));
        reader.Position.Should().Be(20);

        reader.BaseStream.ReadByte().Should().Be(20);
        reader.ReadByteSafe().Should().Be(21);
        reader.BaseStream.Position.Should().Be(22);
        stream.Position.Should().Be(22);
    }
}