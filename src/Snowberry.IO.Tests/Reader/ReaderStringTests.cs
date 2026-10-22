using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for the string reads of the readers: <c>ReadString</c>, <c>ReadCString</c>, <c>ReadSizedCString</c> and <c>ReadLine</c>.
/// </summary>
public class ReaderStringTests
{
    public static TheoryData<string, int> WideEncodingShortReads()
    {
        var data = new TheoryData<string, int>();

        foreach (string name in new[] { "utf-16", "utf-16BE", "utf-32", "utf-32BE" })
        {
            for (int maxBytesPerRead = 1; maxBytesPerRead <= 3; maxBytesPerRead++)
                data.Add(name, maxBytesPerRead);
        }

        return data;
    }

    [Fact]
    public void ReadCString_AsciiAndUtf8Streams_ReturnSameText()
    {
        const string text = "Hello";
        using var asciiReader = CreateReader(WriteBytes(w => w.WriteCString(text), Encoding.ASCII), Encoding.ASCII);
        using var utf8Reader = CreateReader(WriteBytes(w => w.WriteCString(text), Encoding.UTF8), Encoding.UTF8);

        asciiReader.ReadCString().Should().Be(text);
        utf8Reader.ReadCString().Should().Be(text);
    }

    [Fact]
    public void ReadCString_ConsecutiveStrings_ReturnsEachText()
    {
        WriteThenRead(
            w => w.WriteCString("First").WriteCString("Second string is longer").WriteCString("Third"),
            r =>
            {
                r.ReadCString().Should().Be("First");
                r.ReadCString().Should().Be("Second string is longer");
                r.ReadCString().Should().Be("Third");
            });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [Trait("Category", "Stress")]
    public void ReadCString_ManyStrings_ReturnsEachText(int count)
    {
        WriteThenRead(
            w =>
            {
                for (int i = 0; i < count; i++)
                    w.WriteCString($"String_{i}");
            },
            r =>
            {
                for (int i = 0; i < count; i++)
                    r.ReadCString().Should().Be($"String_{i}");
            });
    }

    [Theory]
    [InlineData("utf-8", "Hi")]
    [InlineData("utf-16", "Hi")]
    [InlineData("utf-8", "")]
    public void ReadCString_NoTerminator_ThrowsEndOfStreamException(string encodingName, string text)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        using var reader = CreateReader(WriteBytes(w => w.WriteStringCharacters(text), encoding), encoding);

        FluentActions.Invoking(reader.ReadCString).Should().ThrowExactly<EndOfStreamException>();
    }

    [Theory]
    [MemberData(nameof(WideEncodingShortReads))]
    public void ReadCString_ShortReads_KeepsCodeUnitAlignment(string encodingName, int maxBytesPerRead)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] data = encoding.GetBytes("H\u0100llo\0rest");
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), encoding);

        reader.ReadCString().Should().Be("H\u0100llo");
        reader.Position.Should().Be(encoding.GetByteCount("H\u0100llo\0"));
    }

    [Theory]
    [InlineData('A', 200)]
    [InlineData('B', 500)]
    [InlineData('x', 10000)]
    public void ReadCString_TextLongerThanReadChunk_ReturnsText(char character, int length)
    {
        string text = new(character, length);

        WriteThenRead(w => w.WriteCString(text), r => r.ReadCString().Should().Be(text));
    }

    [Theory]
    [InlineData("utf-8", "")]
    [InlineData("utf-8", "TestCString1✅🤣☆*: .｡. o(≧▽≦)o .｡.:*☆👨🏻")]
    [InlineData("utf-8", "TestCString\U0001F60A\U0001F60A\U0001F473\u200D\u2642\uFE0F_{}")]
    [InlineData("utf-8", "Hello世界")]
    [InlineData("utf-8", "🎉🎊🎈")]
    [InlineData("utf-8", "Привет")]
    [InlineData("utf-8", "こんにちは")]
    [InlineData("utf-8", "مرحبا")]
    [InlineData("utf-8", "é")]
    [InlineData("utf-8", "é")]
    [InlineData("utf-8", "Café à la crème")]
    [InlineData("utf-8", "Hello, World!")]
    [InlineData("utf-8", "a")]
    [InlineData("utf-8", "short")]
    [InlineData("utf-8", "This is exactly 128 characters long padding padding padding padding padding padding padding padding padding padding padding pads")]
    [InlineData("utf-8", "This is longer than 128 characters and will span multiple chunks for testing the chunk-based reading optimization pathway here")]
    [InlineData("us-ascii", "")]
    [InlineData("us-ascii", "Hello World")]
    [InlineData("us-ascii", "Test123!@#")]
    [InlineData("iso-8859-1", "café")]
    [InlineData("utf-16", "Hello")]
    [InlineData("utf-16", "世界")]
    [InlineData("utf-16", "é")]
    [InlineData("utf-16BE", "Hello")]
    [InlineData("utf-16BE", "世界")]
    [InlineData("utf-32", "Hello")]
    [InlineData("utf-32", "Test")]
    public void ReadCString_TextWrittenWithEncoding_ReturnsText(string encodingName, string text)
    {
        WriteThenRead(w => w.WriteCString(text), r => r.ReadCString().Should().Be(text), Encoding.GetEncoding(encodingName));
    }

    [Fact]
    public void ReadCString_TruncatedUtf8Sequence_MatchesEncodingGetString()
    {
        byte[] text = [0x41, 0xE2, 0x82];
        using var reader = CreateReader([.. text, 0x00], Encoding.UTF8);

        string result = reader.ReadCString();

        result.Should().Be(Encoding.UTF8.GetString(text));
        result.Should().Be("A\uFFFD");
        reader.Position.Should().Be(4);
    }

    [Fact]
    public void ReadLine_EmptyLineWrittenWithWriteLine_ReturnsEmptyString()
    {
        WriteThenRead(
            w => w.WriteLine("").WriteLine("test"),
            r =>
            {
                r.ReadLine().Should().Be("");
                r.ReadLine().Should().Be("test");
            });
    }

    [Fact]
    public void ReadLine_EmptyStream_ReturnsNull()
    {
        using var reader = CreateReader([]);

        reader.ReadLine().Should().BeNull();
    }

    [Theory]
    [InlineData("#+#+\uFFFD{}/``!^#/\uFFFD\uFFFD\uFFFD")]
    [InlineData("**```\uFFFD\uFFFD\uFFFD_.,\uFFFD\uFFFD!!\uFFFD@")]
    public void ReadLine_LinesWrittenWithWriteLine_ReturnsEachLine(string line)
    {
        WriteThenRead(
            w => w.WriteLine(line).WriteLine(line),
            r =>
            {
                r.ReadLine().Should().Be(line);
                r.ReadLine().Should().Be(line);
            });
    }

    [Fact]
    public void ReadLine_LoneCarriageReturn_EndsLineWithoutConsumingNextByte()
    {
        using var reader = CreateReader([(byte)'a', (byte)'\r', (byte)'b', (byte)'c']);

        string? first = reader.ReadLine();
        string? second = reader.ReadLine();

        first.Should().Be("a");
        second.Should().Be("bc");
    }

    [Theory]
    [MemberData(nameof(WideEncodingShortReads))]
    public void ReadLine_ShortReads_KeepsCodeUnitAlignment(string encodingName, int maxBytesPerRead)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] data = encoding.GetBytes("Hello\r\nW\u0100rld\rNext\nLast");
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), encoding);

        reader.ReadLine().Should().Be("Hello");
        reader.ReadLine().Should().Be("W\u0100rld");
        reader.ReadLine().Should().Be("Next");
        reader.ReadLine().Should().Be("Last");
        reader.Position.Should().Be(data.Length);
    }

    [Theory]
    [InlineData("\r\n", "")]
    [InlineData("test\nmore", "test")]
    [InlineData("test\r\nmore", "test")]
    [InlineData("abc", "abc")]
    public void ReadLine_Text_ReturnsFirstLineWithoutLineBreak(string text, string expected)
    {
        using var reader = CreateReader(WriteBytes(w => w.WriteStringCharacters(text)));

        reader.ReadLine().Should().Be(expected);
    }

    [Theory]
    [InlineData(new byte[] { 0x41, 0xE2, 0x82, 0x0A })]
    [InlineData(new byte[] { 0x41, 0xE2, 0x82 })]
    public void ReadLine_TruncatedUtf8Sequence_MatchesEncodingGetString(byte[] data)
    {
        using var reader = CreateReader(data, Encoding.UTF8);

        reader.ReadLine().Should().Be(Encoding.UTF8.GetString(data, 0, 3));
    }

    [Fact]
    public void ReadLine_Utf16CodeUnitWithLineBreakBytes_IsNotALineBreak()
    {
        // In UTF-16 LE, U+0A0D is the byte pair 0x0D 0x0A, which a byte-wise search would take for CR LF.
        using var reader = CreateReader(WriteBytes(w => w.WriteStringCharacters("A\u0A0DB"), Encoding.Unicode), Encoding.Unicode);

        reader.ReadLine().Should().Be("A\u0A0DB");
    }

    [Fact]
    public void ReadLine_Utf8LinesInSeveralScripts_ReturnsEachLine()
    {
        WriteThenRead(
            w => w.WriteLine("English").WriteLine("日本語").WriteLine("Русский"),
            r =>
            {
                r.ReadLine().Should().Be("English");
                r.ReadLine().Should().Be("日本語");
                r.ReadLine().Should().Be("Русский");
            },
            Encoding.UTF8);
    }

    [Theory]
    [InlineData(8, 2, true, 8)]
    [InlineData(8, 2, false, 3)]
    [InlineData(300, 5, true, 300)]
    [InlineData(300, 5, false, 6)]
    [InlineData(300, 200, true, 300)]
    [InlineData(300, 200, false, 201)]
    [InlineData(10, -1, true, 10)]
    [InlineData(10, -1, false, 10)]
    [InlineData(300, -1, false, 300)]
    public void ReadSizedCString_AdjustPosition_MovesToSizeOrPastTerminator(int size, int terminatorIndex, bool adjustPosition, long expectedPosition)
    {
        byte[] data = [.. Enumerable.Repeat((byte)'A', size + 16)];

        if (terminatorIndex >= 0)
            data[terminatorIndex] = 0;

        using var reader = CreateReader(data);

        string result = reader.ReadSizedCString(size, adjustPosition);

        result.Should().Be(new string('A', terminatorIndex >= 0 ? terminatorIndex : size));
        reader.Position.Should().Be(expectedPosition);
    }

    [Fact]
    public void ReadSizedCString_NullAtStart_ReturnsEmptyStringAndSkipsSize()
    {
        using var reader = CreateReader(new byte[200]);

        string result = reader.ReadSizedCString(200);

        result.Should().Be(string.Empty);
        reader.Position.Should().Be(200);
    }

    [Theory]
    [MemberData(nameof(WideEncodingShortReads))]
    public void ReadSizedCString_ShortReads_KeepsCodeUnitAlignment(string encodingName, int maxBytesPerRead)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] data = encoding.GetBytes("H\u0100llo\0rest");
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), encoding);

        reader.ReadSizedCString(data.Length, adjustPosition: false).Should().Be("H\u0100llo");
        reader.Position.Should().Be(encoding.GetByteCount("H\u0100llo\0"));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 100)]
    [InlineData(5, 8)]
    public void ReadSizedCString_SizeOutsideRemainingBytes_ThrowsArgumentOutOfRangeException(long position, int size)
    {
        using var reader = CreateReader(new byte[10]);
        reader.Position = position;

        FluentActions.Invoking(() => reader.ReadSizedCString(size)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReadSizedCString_TerminatorAtChunkBoundary_ReturnsTextAndSkipsSize()
    {
        byte[] data = new byte[300];

        // The terminator is the first byte of the second 128-byte read chunk.
        for (int i = 0; i < 128; i++)
            data[i] = (byte)'A';

        using var reader = CreateReader(data);

        string result = reader.ReadSizedCString(300);

        result.Should().Be(new string('A', 128));
        reader.Position.Should().Be(300);
    }

    [Fact]
    public void ReadSizedCString_TextWithEmbeddedNull_StartsWithTextBeforeNull()
    {
        const string text = "Hello\r\n\t\0World\u0000Test";

        WriteThenRead(w => w.WriteSizedCString(text, 50), r => r.ReadSizedCString(50).Should().StartWith("Hello"));
    }

    [Theory]
    [InlineData("Hello\nWorld")]
    [InlineData("Line1\r\nLine2\nHello")]
    [InlineData("Tab\tSeparated\nHello")]
    public void ReadSizedCString_TextWithLineBreaksAndTabs_ContainsText(string text)
    {
        WriteThenRead(w => w.WriteSizedCString(text, 50), r => r.ReadSizedCString(50).Should().Contain("Hello"));
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("Test", 10)]
    [InlineData("Content", 10)]
    [InlineData("Content", 100)]
    [InlineData("Content", 255)]
    [InlineData("Hello", 5)]
    [InlineData("Hello", 10)]
    [InlineData("世界", 20)]
    [InlineData("🎉🎊", 30)]
    public void ReadSizedCString_TextWrittenWithSize_ReturnsText(string text, int size)
    {
        WriteThenRead(w => w.WriteSizedCString(text, size), r => r.ReadSizedCString(size).Should().Be(text));
    }

    [Fact]
    public void ReadSizedCString_TruncatedUtf8SequenceWithoutTerminator_MatchesEncodingGetString()
    {
        byte[] text = [0x41, 0xE2, 0x82];
        using var reader = CreateReader(text, Encoding.UTF8);

        reader.ReadSizedCString(3).Should().Be(Encoding.UTF8.GetString(text));
    }

    [Fact]
    public void ReadSizedCString_TruncatedUtf8Sequence_MatchesEncodingGetString()
    {
        byte[] text = [0x41, 0xE2, 0x82];
        using var reader = CreateReader([.. text, 0x00], Encoding.UTF8);

        reader.ReadSizedCString(4).Should().Be(Encoding.UTF8.GetString(text));
    }

    [Theory]
    [InlineData("utf-8", "TestSizedString1", 0)]
    [InlineData("utf-8", "TestSizedString\U0001F60A_{}", 0)]
    [InlineData("utf-8", "TestSizedString\U0001F60A\U0001F60A\U0001F60A\U0001F473\u200D\u2642\uFE0F_{}\U0001F60A\U0001F60A\U0001F473\u200D\U0001F60A\U0001F60A\U0001F473\u200D\U0001F60A\U0001F60A\U0001F473\u200D\U0001F60A\U0001F60A\U0001F473\u200DTestSizedString\U0001F60A\U0001F60A\U0001F60A\U0001F473", 500)]
    [InlineData("us-ascii", "TestSizedString1", 0)]
    [InlineData("us-ascii", "TestSizedString{}", 0)]
    [InlineData("us-ascii", "TestSizedStringTestSizedString", 500)]
    public void ReadSizedCString_TwoStringsWrittenWithSize_ReturnsEachText(string encodingName, string text, int minimumSize)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int size = Math.Max(encoding.GetByteCount(text), minimumSize);

        WriteThenRead(
            w => w.WriteSizedCString(text, size).WriteSizedCString(text, size),
            r =>
            {
                r.ReadSizedCString(size).Should().Be(text);
                r.ReadSizedCString(size).Should().Be(text);
                r.ActualPosition.Should().Be(size * 2);
            },
            encoding);
    }

    [Fact]
    public void ReadSizedCString_ZeroSize_ReturnsEmptyString()
    {
        WriteThenRead(_ => { }, r => r.ReadSizedCString(0).Should().Be(""));
    }

    [Fact]
    public void ReadString_LengthPrefixBeyondEnd_ThrowsEndOfStreamException()
    {
        using var reader = CreateReader(WriteBytes(w => w.Write7BitEncodedInt(int.MaxValue)));

        FluentActions.Invoking(reader.ReadString).Should().ThrowExactly<EndOfStreamException>();
    }

    [Theory]
    [InlineData('x', 10)]
    [InlineData('x', 50)]
    [InlineData('x', 100)]
    [InlineData('x', 128)] // The size of the internal decoding buffer.
    [InlineData('x', 200)]
    [InlineData('x', 1000)]
    [InlineData('y', 10000)]
    [InlineData('x', 100000)]
    public void ReadString_LongText_ReturnsText(char character, int length)
    {
        string text = new(character, length);

        WriteThenRead(w => w.Write(text), r => r.ReadString().Should().Be(text));
    }

    [Fact]
    public void ReadString_ManyShortStrings_ReturnsEachText()
    {
        string[] texts = ["a", "bb", "ccc", "dddd", "eeeee", "ffffff", "ggggggg"];

        WriteThenRead(
            w =>
            {
                foreach (string text in texts)
                    w.Write(text);
            },
            r =>
            {
                foreach (string text in texts)
                    r.ReadString().Should().Be(text);
            });
    }

    [Fact]
    public void ReadString_NegativeLengthPrefix_ThrowsIOException()
    {
        using var reader = CreateReader(WriteBytes(w => w.Write7BitEncodedInt(-1)));

        FluentActions.Invoking(reader.ReadString).Should().ThrowExactly<IOException>();
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void ReadString_OneMillionCharacters_ReturnsText()
    {
        string text = new('x', 1_000_000);

        WriteThenRead(w => w.Write(text), r => r.ReadString().Should().Be(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#+#+\uFFFD{}/``!^#/\uFFFD\uFFFD\uFFFD")]
    [InlineData("**```\uFFFD\uFFFD\uFFFD_.,\uFFFD\uFFFD!!\uFFFD@")]
    [InlineData("Short")]
    [InlineData("This is a medium length test string that should hit the optimized path")]
    [InlineData("This is a very long string that will exceed the MaxCharBytesSize and use the StringBuilder path for reading, testing the non-optimized code path that handles larger strings with multiple chunk reads and proper character decoding across boundaries")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    [InlineData("abcde")]
    [InlineData("😀😃😄😁😆😅🤣😂")]
    public void ReadString_TextWrittenByWriter_ReturnsText(string text)
    {
        WriteThenRead(w => w.Write(text), r => r.ReadString().Should().Be(text));
    }

    [Fact]
    public void ReadString_TruncatedMultiByteSequenceAtEnd_MatchesBinaryReader()
    {
        byte[] payload = new byte[130];

        for (int i = 0; i < 129; i++)
            payload[i] = (byte)'A';

        // A UTF-8 lead byte without its continuation byte ends the string.
        payload[129] = 0xC3;

        byte[] data = WriteBytes(w => w.Write7BitEncodedInt(payload.Length).Write(payload).Write("B"), Encoding.UTF8);
        using var expected = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        using var reader = CreateReader(data, Encoding.UTF8);

        reader.ReadString().Should().Be(expected.ReadString());
        reader.ReadString().Should().Be(expected.ReadString());
        reader.Position.Should().Be(expected.BaseStream.Position);
    }
}