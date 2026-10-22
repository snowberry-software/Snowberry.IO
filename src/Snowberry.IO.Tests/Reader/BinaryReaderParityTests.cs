using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Checks that the readers behave like <see cref="BinaryReader"/> for the members they inherit from it, and that those
/// members still go through the region view and the <see cref="Analyzer"/>.
/// </summary>
public class BinaryReaderParityTests
{
    private static readonly Dictionary<string, (int Size, Func<BinaryReader, object> Read)> s_FixedSizeReads = new()
    {
        ["Boolean"] = (1, r => r.ReadBoolean()),
        ["Byte"] = (1, r => r.ReadByte()),
        ["SByte"] = (1, r => r.ReadSByte()),
        ["Int16"] = (2, r => r.ReadInt16()),
        ["UInt16"] = (2, r => r.ReadUInt16()),
        ["Int32"] = (4, r => r.ReadInt32()),
        ["UInt32"] = (4, r => r.ReadUInt32()),
        ["Int64"] = (8, r => r.ReadInt64()),
        ["UInt64"] = (8, r => r.ReadUInt64()),
        ["Single"] = (4, r => r.ReadSingle()),
        ["Double"] = (8, r => r.ReadDouble()),
#if NET5_0_OR_GREATER
        ["Half"] = (2, r => r.ReadHalf()),
#endif
    };

    private static object? CreateArgument(Type type)
    {
        if (type == typeof(int))
            return 4;

        if (type == typeof(byte[]))
            return new byte[8];

        if (type == typeof(char[]))
            return new char[8];

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    /// <summary>
    /// Creates a plain <see cref="BinaryReader"/> and an <see cref="EndianStreamReader"/> over the same bytes.
    /// </summary>
    private static (BinaryReader Expected, EndianStreamReader Actual) CreateReaderPair(byte[] data, Encoding encoding, int maxBytesPerRead = int.MaxValue)
    {
        var expected = new BinaryReader(new ChunkedReadStream(data, maxBytesPerRead), encoding);
        var actual = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), encoding);
        return (expected, actual);
    }

    private static void WriteLeb128(BinaryWriter writer, ulong value)
    {
        while (value >= 0x80)
        {
            writer.Write((byte)(value | 0x80));
            value >>= 7;
        }

        writer.Write((byte)value);
    }

    [Fact]
    public void BaseReaderStream_EndianStreamReader_IsReaderStream()
    {
        using var reader = new EndianStreamReader(new MemoryStream(new byte[8]));
        using var nullReader = new EndianStreamReader(null);

        BinaryReaderAssertions.AssertReadsThroughReaderStream(reader);
        BinaryReaderAssertions.AssertReadsThroughReaderStream(nullReader);
    }

    [Fact]
    public void BaseStreamReadByte_AtEndWithAnalyzer_NotifiesEmptyRead()
    {
        var analyzer = new AmountRecordingAnalyzer();
        using var reader = CreateReader([7], analyzer: analyzer);
        var stream = reader.BaseStream;

        stream.ReadByte().Should().Be(7);
        stream.ReadByte().Should().Be(-1);
        reader.ReadByteSafe().Should().Be(-1);

        analyzer.Amounts.Should().Equal(1, 0, 0);
    }

    [Fact]
    public void BaseStreamReads_NullStream_ThrowInvalidOperationException()
    {
        using var reader = new EndianStreamReader(null);
        var stream = reader.BaseStream;

        FluentActions.Invoking(() => stream.ReadByte()).Should().ThrowExactly<InvalidOperationException>().WithMessage("The Stream is null.");
        FluentActions.Invoking(() => stream.Read(new byte[4], 0, 4)).Should().ThrowExactly<InvalidOperationException>().WithMessage("The Stream is null.");
        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<InvalidOperationException>().WithMessage("The Stream is null.");
    }

    [Fact]
    public void BaseStreamReads_XorAnalyzer_ReturnDecryptedBytes()
    {
        byte[] plain = SequentialBytes(12);
        byte[] encrypted = [.. plain.Select(b => (byte)(b ^ 0x5A))];
        using var reader = CreateReader(encrypted, analyzer: new XorAnalyzer(0x5A));
        var stream = reader.BaseStream;

        stream.ReadByte().Should().Be(0);

        byte[] buffer = new byte[6];
        stream.Read(buffer, 1, 4).Should().Be(4);
        buffer.Should().Equal(0, 1, 2, 3, 4, 0);

#if NET5_0_OR_GREATER
        Span<byte> span = stackalloc byte[4];
        stream.Read(span).Should().Be(4);
        span.ToArray().Should().Equal(5, 6, 7, 8);
#else
        stream.Read(buffer, 0, 4).Should().Be(4);
        buffer.Take(4).Should().Equal(5, 6, 7, 8);
#endif

        reader.ReadByte().Should().Be(9);
    }

    [Theory]
    [MemberData(nameof(FixedSizeReads))]
    public void FixedSizeRead_AfterDispose_ThrowsObjectDisposedException(string type)
    {
        var (_, read) = s_FixedSizeReads[type];
        var reader = new EndianStreamReader(new MemoryStream(new byte[16])) { KeepStreamOpen = true };
        reader.Dispose();

        FluentActions.Invoking(() => read(reader)).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Theory]
    [MemberData(nameof(FixedSizeReads))]
    public void FixedSizeRead_NonSeekableStream_MatchesBinaryReader(string type)
    {
        var (size, read) = s_FixedSizeReads[type];
        byte[] data = RandomBytes((2 * size) - 1);

        using var expected = new BinaryReader(new ChunkedReadStream(data, canSeek: false));
        using var actual = CreateReader(new ChunkedReadStream(data, canSeek: false));

        read(actual).Should().Be(read(expected));
        FluentActions.Invoking(() => read(expected)).Should().ThrowExactly<EndOfStreamException>();
        FluentActions.Invoking(() => read(actual)).Should().ThrowExactly<EndOfStreamException>();
        actual.ReadByteSafe().Should().Be(-1);
    }

    [Theory]
    [MemberData(nameof(FixedSizeReadsAndChunkSizes))]
    public void FixedSizeRead_PartialValueAtEnd_MatchesBinaryReader(string type, int maxBytesPerRead)
    {
        var (size, read) = s_FixedSizeReads[type];
        byte[] data = RandomBytes((2 * size) - 1);
        var analyzer = new RecordingAnalyzer();

        using var expected = new BinaryReader(new ChunkedReadStream(data, maxBytesPerRead));
        using var actual = CreateReader(new ChunkedReadStream(data, maxBytesPerRead));
        using var analyzed = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), analyzer: analyzer);

        object value = read(expected);
        read(actual).Should().Be(value);
        read(analyzed).Should().Be(value);

        string message = FluentActions.Invoking(() => read(expected)).Should().ThrowExactly<EndOfStreamException>().Which.Message;
        FluentActions.Invoking(() => read(actual)).Should().ThrowExactly<EndOfStreamException>().WithMessage(message);
        FluentActions.Invoking(() => read(analyzed)).Should().ThrowExactly<EndOfStreamException>().WithMessage(message);

        actual.Position.Should().Be(expected.BaseStream.Position).And.Be(data.Length);
        analyzed.Position.Should().Be(data.Length);
        analyzer.Bytes.Should().Equal(data);
    }

    [Theory]
    [MemberData(nameof(FixedSizeReads))]
    public void FixedSizeRead_RegionViewWithoutAnalyzer_StopsAtRegionEnd(string type)
    {
        var (size, read) = s_FixedSizeReads[type];
        byte[] data = RandomBytes(64);

        using var expected = new BinaryReader(new MemoryStream(data, 8, size));
        using var actual = CreateReader(data);
        actual.EnableRegionView(new RegionRange { StartPosition = 8, Size = size });

        read(actual).Should().Be(read(expected));
        FluentActions.Invoking(() => read(actual)).Should().ThrowExactly<EndOfStreamException>();
        actual.ActualPosition.Should().Be(8 + size);

        actual.DisableRegionView();
        actual.Position = 8 + size;
        read(actual).Should().Be(read(new BinaryReader(new MemoryStream(data, 8 + size, size))));
    }

    [Theory]
    [MemberData(nameof(FixedSizeReads))]
    public void FixedSizeRead_XorAnalyzerSetBetweenReads_DecryptsOnlyWhileSet(string type)
    {
        var (size, read) = s_FixedSizeReads[type];
        byte[] plain = RandomBytes(3 * size);
        byte[] data = [.. plain.Select((b, i) => i >= size && i < 2 * size ? (byte)(b ^ 0x5A) : b)];

        using var expected = new BinaryReader(new MemoryStream(plain));
        using var actual = CreateReader(data);

        read(actual).Should().Be(read(expected));
        actual.Analyzer = new XorAnalyzer(0x5A);
        read(actual).Should().Be(read(expected));
        actual.Analyzer = null;
        read(actual).Should().Be(read(expected));
    }

    /// <summary>
    /// Every public read of <see cref="BinaryReader"/> that can be invoked through reflection must stop at the end of an
    /// empty region view, even though the underlying stream has more data. A future BinaryReader read that bypassed the
    /// reader's stream would read the data after the region and fail this test.
    /// </summary>
    [Fact]
    public void InheritedReads_EmptyRegionView_ReadNothing()
    {
        byte[] data = [.. Enumerable.Repeat((byte)0x41, 64)];
        var methods = typeof(BinaryReader).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(BinaryReader) && (m.Name.StartsWith("Read", StringComparison.Ordinal) || m.Name == nameof(BinaryReader.PeekChar)))
            .ToList();

        methods.Should().NotBeEmpty();

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();

            if (parameters.Any(p => p.ParameterType.IsByRef || (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(Span<>))))
                continue;

            var analyzer = new RecordingAnalyzer();
            using var reader = CreateReader(data, analyzer: analyzer);
            reader.EnableRegionView(new RegionRange { StartPosition = 8, Size = 0 });

            object?[] arguments = [.. parameters.Select(p => CreateArgument(p.ParameterType))];
            object? result = null;

            try
            {
                result = method.Invoke(reader, arguments);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is EndOfStreamException)
            {
            }

            string name = method.ToString()!;

            if (result is int number)
                number.Should().BeOneOf([-1, 0], name);
            else if (result is Array array)
                array.Length.Should().Be(0, name);
            else if (result is string text)
                text.Should().BeEmpty(name);
            else
                result.Should().BeNull(name);

            reader.ActualPosition.Should().Be(8, name);
            analyzer.Bytes.Should().BeEmpty(name);
        }
    }

    [Fact]
    public void InheritedReads_XorAnalyzer_ReturnDecryptedValues()
    {
        byte[] plain = WriteBinaryWriterBytes(w =>
        {
            w.Write(0x12345678);
            w.Write("Secret");
            w.Write('Z');
            w.Write(2.5);
        });

        byte[] encrypted = [.. plain.Select(b => (byte)(b ^ 0x5A))];
        using var reader = CreateReader(encrypted, analyzer: new XorAnalyzer(0x5A));

        reader.ReadInt32().Should().Be(0x12345678);
        reader.ReadString().Should().Be("Secret");
        reader.ReadChar().Should().Be('Z');
        reader.ReadDouble().Should().Be(2.5);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(300L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Read7BitEncodedInt64_EncodedValue_ReturnsValue(long value)
    {
        byte[] data = WriteBinaryWriterBytes(w => WriteLeb128(w, (ulong)value));

        using var reader = CreateReader(data);
        reader.Read7BitEncodedInt64().Should().Be(value);

#if NET5_0_OR_GREATER
        using var expected = new BinaryReader(new MemoryStream(data));
        expected.Read7BitEncodedInt64().Should().Be(value);
#endif
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Read7BitEncodedInt_EncodedValue_ReturnsValue(int value)
    {
        byte[] data = WriteBinaryWriterBytes(w => WriteLeb128(w, (uint)value));

        using var reader = CreateReader(data);
        reader.Read7BitEncodedInt().Should().Be(value);

#if NET5_0_OR_GREATER
        using var expected = new BinaryReader(new MemoryStream(data));
        expected.Read7BitEncodedInt().Should().Be(value);
#endif
    }

    [Fact]
    public void ReadBytes_AtEndOfStream_MatchesBinaryReader()
    {
        byte[] data = [1, 2, 3, 4, 5];

        var (expected, actual) = CreateReaderPair(data, Encoding.UTF8, maxBytesPerRead: 2);
        using (expected)
        using (actual)
        {
            actual.ReadBytes(3).Should().Equal(expected.ReadBytes(3));
            actual.ReadBytes(10).Should().Equal(expected.ReadBytes(10)).And.Equal(4, 5);
            actual.ReadBytes(10).Should().Equal(expected.ReadBytes(10)).And.BeEmpty();
            actual.ReadBytes(0).Should().Equal(expected.ReadBytes(0));
        }
    }

    [Fact]
    public void ReadBytes_InsideRegionView_StopsAtRegionEndAndNotifiesAnalyzer()
    {
        byte[] data = SequentialBytes(32);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, analyzer: analyzer);
        reader.EnableRegionView(new RegionRange { StartPosition = 8, Size = 4 });

        reader.ReadBytes(100).Should().Equal(8, 9, 10, 11);
        reader.ReadByteSafe().Should().Be(-1);
        FluentActions.Invoking(() => reader.ReadByte()).Should().ThrowExactly<EndOfStreamException>();
        analyzer.Bytes.Should().Equal(8, 9, 10, 11);
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadChar_InsideRegionView_StopsAtRegionEnd(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] data = encoding.GetBytes("xab");
        int unit = encoding.GetByteCount("a");

        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, encoding, analyzer);
        reader.EnableRegionView(new RegionRange { StartPosition = unit, Size = unit });

        reader.ReadChar().Should().Be('a');
        reader.PeekChar().Should().Be(-1);
        FluentActions.Invoking(() => reader.ReadChar()).Should().ThrowExactly<EndOfStreamException>();

        reader.ActualPosition.Should().Be(2 * unit);
        analyzer.Bytes.Should().Equal(encoding.GetBytes("a"));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadChar_MixedCharacters_MatchesBinaryReader(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] data = encoding.GetBytes("aé世bcdéfghij€k");

        var (expected, actual) = CreateReaderPair(data, encoding);
        using (expected)
        using (actual)
        {
            actual.PeekChar().Should().Be(expected.PeekChar());
            actual.ReadChar().Should().Be(expected.ReadChar());
            actual.ReadChar().Should().Be(expected.ReadChar());
            actual.Read().Should().Be(expected.Read());
            actual.ReadChars(3).Should().Equal(expected.ReadChars(3));

            char[] actualBuffer = new char[6];
            char[] expectedBuffer = new char[6];
            actual.Read(actualBuffer, 1, 4).Should().Be(expected.Read(expectedBuffer, 1, 4));
            actualBuffer.Should().Equal(expectedBuffer);

            actual.PeekChar().Should().Be(expected.PeekChar());
            actual.ReadChars(10).Should().Equal(expected.ReadChars(10));
            actual.PeekChar().Should().Be(expected.PeekChar()).And.Be(-1);
            actual.Read().Should().Be(expected.Read()).And.Be(-1);
            actual.Position.Should().Be(expected.BaseStream.Position);

            FluentActions.Invoking(() => actual.ReadChar()).Should().ThrowExactly<EndOfStreamException>();
            FluentActions.Invoking(() => expected.ReadChar()).Should().ThrowExactly<EndOfStreamException>();
        }
    }

    [Fact]
    public void ReadExactly_ShortStream_MatchesBinaryReader()
    {
        byte[] data = [1, 2, 3, 4, 5];

        var (expected, actual) = CreateReaderPair(data, Encoding.UTF8, maxBytesPerRead: 1);
        using (expected)
        using (actual)
        {
            byte[] actualBuffer = new byte[3];
            actual.ReadExactly(actualBuffer.AsSpan());
            actualBuffer.Should().Equal(1, 2, 3);

            FluentActions.Invoking(() => actual.ReadExactly(new byte[3].AsSpan())).Should().ThrowExactly<EndOfStreamException>();

#if NET7_0_OR_GREATER
            byte[] expectedBuffer = new byte[3];
            expected.ReadExactly(expectedBuffer);
            actualBuffer.Should().Equal(expectedBuffer);
            FluentActions.Invoking(() => expected.ReadExactly(new byte[3])).Should().ThrowExactly<EndOfStreamException>();
            actual.Position.Should().Be(expected.BaseStream.Position);
#endif
        }
    }

#if NET5_0_OR_GREATER
    [Fact]
    public void ReadHalf_SameBytes_MatchesBinaryReader()
    {
        byte[] data = WriteBinaryWriterBytes(w =>
        {
            w.Write((Half)1.5f);
            w.Write(Half.NegativeInfinity);
        });

        var (expected, actual) = CreateReaderPair(data, Encoding.UTF8);
        using (expected)
        using (actual)
        {
            actual.ReadHalf().Should().Be(expected.ReadHalf());
            actual.ReadHalf().Should().Be(expected.ReadHalf());
        }
    }
#endif

    [Fact]
    public void ReadInt32_InsideRegionView_StopsAtRegionEnd()
    {
        byte[] data = SequentialBytes(32);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, analyzer: analyzer);
        reader.EnableRegionView(new RegionRange { StartPosition = 4, Size = 6 });

        reader.ReadInt32().Should().Be(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)));
        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<EndOfStreamException>();

        reader.Position.Should().Be(6);
        reader.ActualPosition.Should().Be(10);
        analyzer.Bytes.Should().Equal(data.Skip(4).Take(6));
    }

    [Fact]
    public void ReadPrimitivesAndString_BinaryWriterOutput_ReturnWrittenValues()
    {
        using var reader = CreateReader(WriteBinaryWriterBytes(w =>
        {
            w.Write(100);
            w.Write(200L);
            w.Write("Test");
        }));

        reader.ReadInt32().Should().Be(100);
        reader.ReadInt64().Should().Be(200L);
        reader.ReadString().Should().Be("Test");
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    public void ReadPrimitives_SameBytes_MatchBinaryReader(int maxBytesPerRead)
    {
        byte[] data = WriteBinaryWriterBytes(w =>
        {
            w.Write(true);
            w.Write(false);
            w.Write((byte)0xFE);
            w.Write((sbyte)-5);
            w.Write((short)-12345);
            w.Write((ushort)54321);
            w.Write(-123456789);
            w.Write(3456789012u);
            w.Write(-1234567890123456789L);
            w.Write(12345678901234567890UL);
            w.Write(3.25f);
            w.Write(-2.5e300);
            w.Write(-79228162514264337593543950335m);
            w.Write(0.1m);
        });

        var (expected, actual) = CreateReaderPair(data, Encoding.UTF8, maxBytesPerRead);
        using (expected)
        using (actual)
        {
            actual.ReadBoolean().Should().Be(expected.ReadBoolean());
            actual.ReadBoolean().Should().Be(expected.ReadBoolean());
            actual.ReadByte().Should().Be(expected.ReadByte());
            actual.ReadSByte().Should().Be(expected.ReadSByte());
            actual.ReadInt16().Should().Be(expected.ReadInt16());
            actual.ReadUInt16().Should().Be(expected.ReadUInt16());
            actual.ReadInt32().Should().Be(expected.ReadInt32());
            actual.ReadUInt32().Should().Be(expected.ReadUInt32());
            actual.ReadInt64().Should().Be(expected.ReadInt64());
            actual.ReadUInt64().Should().Be(expected.ReadUInt64());
            actual.ReadSingle().Should().Be(expected.ReadSingle());
            actual.ReadDouble().Should().Be(expected.ReadDouble());
            actual.ReadDecimal().Should().Be(expected.ReadDecimal());
            actual.ReadDecimal().Should().Be(expected.ReadDecimal());

            actual.Position.Should().Be(expected.BaseStream.Position);
        }
    }

    [Fact]
    public void ReadString_InsideRegionView_StopsAtRegionEnd()
    {
        byte[] data = WriteBinaryWriterBytes(w =>
        {
            w.Write((byte)0xAA);
            w.Write("Hello");
            w.Write("World");
        });

        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, analyzer: analyzer);

        // The region ends in the middle of "World".
        reader.EnableRegionView(new RegionRange { StartPosition = 1, Size = 6 + 3 });

        reader.ReadString().Should().Be("Hello");
        FluentActions.Invoking(reader.ReadString).Should().ThrowExactly<EndOfStreamException>();

        reader.ActualPosition.Should().Be(10);
        analyzer.Bytes.Should().Equal(data.Skip(1).Take(9));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadString_ShortAndLongStrings_MatchesBinaryReader(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string longText = string.Concat(Enumerable.Repeat("Grüße, 世界! ", 40));

        byte[] data = WriteBinaryWriterBytes(
            w =>
            {
                w.Write(string.Empty);
                w.Write("Hello");
                w.Write(longText);
            },
            encoding);

        var (expected, actual) = CreateReaderPair(data, encoding, maxBytesPerRead: 7);
        using (expected)
        using (actual)
        {
            actual.ReadString().Should().Be(expected.ReadString());
            actual.ReadString().Should().Be(expected.ReadString());
            actual.ReadString().Should().Be(expected.ReadString()).And.Be(longText);

            actual.Position.Should().Be(expected.BaseStream.Position);
            FluentActions.Invoking(actual.ReadString).Should().ThrowExactly<EndOfStreamException>();
            FluentActions.Invoking(expected.ReadString).Should().ThrowExactly<EndOfStreamException>();
        }
    }

    [Fact]
    public void Read_SpanFromChunkedStream_MatchesBinaryReader()
    {
        byte[] data = [1, 2, 3, 4, 5];

        var (expected, actual) = CreateReaderPair(data, Encoding.UTF8, maxBytesPerRead: 2);
        using (expected)
        using (actual)
        {
            byte[] actualBuffer = new byte[4];
            byte[] expectedBuffer = new byte[4];

            actual.Read(actualBuffer.AsSpan()).Should().Be(expected.Read(expectedBuffer, 0, 4));
            actualBuffer.Should().Equal(expectedBuffer);
            actual.Read(actualBuffer, 1, 3).Should().Be(expected.Read(expectedBuffer, 1, 3));
            actualBuffer.Should().Equal(expectedBuffer);
        }
    }

    public static TheoryData<string> Encodings => ["utf-8", "utf-16"];

    public static TheoryData<string> FixedSizeReads => [.. s_FixedSizeReads.Keys];

    public static TheoryData<string, int> FixedSizeReadsAndChunkSizes
    {
        get
        {
            var cases = new TheoryData<string, int>();

            foreach (string type in s_FixedSizeReads.Keys)
            {
                cases.Add(type, 1);
                cases.Add(type, int.MaxValue);
            }

            return cases;
        }
    }

    /// <summary>
    /// An <see cref="Analyzer"/> that records the amount of every notification, including the empty ones.
    /// </summary>
    private sealed class AmountRecordingAnalyzer : Analyzer
    {
        public override void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int amount)
        {
            Amounts.Add(amount);
        }

        public override void Initialize(IEndianReader reader)
        {
        }

        public List<int> Amounts { get; } = [];
    }
}