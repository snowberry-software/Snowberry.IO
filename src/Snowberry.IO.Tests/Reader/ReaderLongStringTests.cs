using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for <c>ReadCString</c>, <c>ReadSizedCString</c> and <c>ReadLine</c> with strings that do not fit into the first
/// 128-byte read, in every supported kind of encoding, with short reads, analyzers, region views and invalid bytes.
/// </summary>
public class ReaderLongStringTests
{
    private static readonly string[] s_EncodingNames = ["utf-8", "utf-16", "utf-16BE", "utf-32", "utf-32BE", "iso-8859-1", "us-ascii"];

    /// <summary>
    /// The byte lengths at which the reads of a string end: the first 128-byte read, then reads of 1 KiB that double up to 64 KiB.
    /// </summary>
    private static readonly int[] s_ReadBoundaries = [128, 1152, 3200, 7296, 15488, 31872, 64640, 130176, 195712];

    public static TheoryData<string> Encodings()
    {
        var data = new TheoryData<string>();

        foreach (string name in s_EncodingNames)
            data.Add(name);

        return data;
    }

    public static TheoryData<string, int> EncodingsAndLengthsAroundReadBoundaries()
    {
        var data = new TheoryData<string, int>();

        foreach (string name in s_EncodingNames)
        {
            int unitSize = UnitSize(Encoding.GetEncoding(name));

            foreach (int length in new[] { 0, unitSize, 64 })
                data.Add(name, length);

            foreach (int boundary in s_ReadBoundaries)
            {
                data.Add(name, boundary - unitSize);
                data.Add(name, boundary);
                data.Add(name, boundary + unitSize);
            }
        }

        return data;
    }

    public static TheoryData<string, int> EncodingsAndShortReads()
    {
        var data = new TheoryData<string, int>();

        foreach (string name in s_EncodingNames)
        {
            foreach (int maxBytesPerRead in new[] { 1, 3, 7, 1000 })
                data.Add(name, maxBytesPerRead);
        }

        return data;
    }

    /// <summary>
    /// Encodes <paramref name="text"/>, <paramref name="terminator"/> and <paramref name="trailing"/> one after another.
    /// </summary>
    private static byte[] Encode(Encoding encoding, string text, string terminator, string trailing)
    {
        return [.. encoding.GetBytes(text), .. encoding.GetBytes(terminator), .. encoding.GetBytes(trailing)];
    }

    /// <summary>
    /// Creates text that <paramref name="encoding"/> encodes into exactly <paramref name="byteLength"/> bytes, with characters that need
    /// more than one byte or code unit wherever the encoding supports them.
    /// </summary>
    private static string SampleText(Encoding encoding, int byteLength)
    {
        string alphabet = encoding.CodePage switch
        {
            20127 => "Hello, World! 0123456789 abcdefghijklmnopqrstuvwxyz",
            28591 => "Gr\u00FC\u00DFe, caf\u00E9! \u00E0\u00E9\u00EE\u00F5\u00FC \u00C4\u00D6\u00DC\u00DF 0123456789",
            _ => "Gr\u00FC\u00DFe, \u4E16\u754C! \U0001F600 \u041F\u0440\u0438\u0432\u0435\u0442 \u0100\u0A0D\u0D0A\u2028 0123456789"
        };

        var builder = new StringBuilder();
        int length = 0;
        int index = 0;
        int padding = encoding.GetByteCount("a");
        int alphabetLength = encoding.GetByteCount(alphabet);

        while (length + alphabetLength <= byteLength - (4 * padding))
        {
            builder.Append(alphabet);
            length += alphabetLength;
        }

        while (true)
        {
            string next = char.IsHighSurrogate(alphabet[index]) ? alphabet.Substring(index, 2) : alphabet.Substring(index, 1);
            int nextLength = encoding.GetByteCount(next);

            if (length + nextLength > byteLength - (4 * padding))
                break;

            builder.Append(next);
            length += nextLength;
            index = (index + next.Length) % alphabet.Length;
        }

        while (length < byteLength)
        {
            builder.Append('a');
            length += padding;
        }

        length.Should().Be(byteLength);
        return builder.ToString();
    }

    /// <summary>
    /// Gets the size of a code unit of <paramref name="encoding"/>.
    /// </summary>
    private static int UnitSize(Encoding encoding)
    {
        return encoding.GetByteCount("\0");
    }

    /// <summary>
    /// Replaces every aligned CR and LF code unit of <paramref name="data"/> with a different code unit.
    /// </summary>
    private static byte[] WithoutLineBreakUnits(byte[] data, Encoding encoding)
    {
        byte[] crUnit = encoding.GetBytes("\r");
        byte[] lfUnit = encoding.GetBytes("\n");
        int unitSize = crUnit.Length;

        for (int i = 0; i + unitSize <= data.Length; i += unitSize)
        {
            var unit = data.AsSpan(i, unitSize);

            if (unit.SequenceEqual(crUnit) || unit.SequenceEqual(lfUnit))
                unit[0] |= 0x40;
        }

        return data;
    }

    /// <summary>
    /// Replaces every aligned null code unit of <paramref name="data"/> with a non-null one.
    /// </summary>
    private static byte[] WithoutNullUnits(byte[] data, int unitSize)
    {
        for (int i = 0; i + unitSize <= data.Length; i += unitSize)
        {
            if (data.AsSpan(i, unitSize).SequenceEqual(new byte[unitSize]))
                data[i] = 0x41;
        }

        return data;
    }

    private static byte[] Xor(byte[] data, byte key)
    {
        return [.. data.Select(b => (byte)(b ^ key))];
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadCString_AnalyzerSetAndRemovedBetweenStrings_NotifiesOnlyAnalyzedStrings(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string shortText = SampleText(encoding, 20 * UnitSize(encoding));
        string longText = SampleText(encoding, 3000 * UnitSize(encoding));
        byte[] analyzed = Encode(encoding, longText, "\0", shortText + "\0");
        byte[] data = [.. Encode(encoding, shortText, "\0", longText + "\0"), .. analyzed, .. Encode(encoding, longText, "\0", "")];
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, encoding);

        reader.ReadCString().Should().Be(shortText);
        reader.ReadCString().Should().Be(longText);
        reader.Analyzer = analyzer;
        reader.ReadCString().Should().Be(longText);
        reader.ReadCString().Should().Be(shortText);
        reader.Analyzer = null;
        reader.ReadCString().Should().Be(longText);

        reader.Position.Should().Be(data.Length);
        analyzer.Bytes.Should().Equal(analyzed);
    }

    [Theory]
    [InlineData("utf-8", 10)]
    [InlineData("utf-8", 127)]
    [InlineData("utf-8", 5000)]
    [InlineData("utf-16", 10)]
    [InlineData("utf-16", 5000)]
    public void ReadCString_InvalidBytesWithLongReplacementFallback_MatchesEncodingGetString(string encodingName, int length)
    {
        // Every invalid code unit (a UTF-8 continuation byte, a lone UTF-16 low surrogate) decodes to more characters than it has bytes.
        var encoding = Encoding.GetEncoding(encodingName, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("[invalid]"));
        int unitSize = UnitSize(encoding);
        byte[] content = unitSize == 1
            ? [.. Enumerable.Range(0, length).Select(i => (byte)(0x80 + (i % 64)))]
            : [.. Enumerable.Range(0, length).SelectMany(_ => new byte[] { 0x00, 0xDC })];
        using var reader = CreateReader([.. content, .. new byte[unitSize]], encoding);

        reader.ReadCString().Should().Be(encoding.GetString(content));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(70_000)]
    public void ReadCString_InvalidBytesWithThrowingEncoding_ThrowsDecoderFallbackException(int validPrefixLength)
    {
        var encoding = new UTF8Encoding(false, throwOnInvalidBytes: true);
        byte[] data = [.. Enumerable.Repeat((byte)'a', validPrefixLength), 0xFF, (byte)'b', 0];
        using var reader = CreateReader(data, encoding);

        FluentActions.Invoking(reader.ReadCString).Should().ThrowExactly<DecoderFallbackException>();
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadCString_LongTextWithRecordingAnalyzer_NotifiesEveryConsumedByteOnce(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, 5000 * UnitSize(encoding));
        byte[] data = Encode(encoding, text, "\0", "trailing");
        int consumed = encoding.GetByteCount(text + "\0");
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, encoding, analyzer);

        reader.ReadCString().Should().Be(text);

        analyzer.Bytes.Should().Equal(data.Take(consumed));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadCString_LongTextWithXorAnalyzer_ReturnsDecryptedText(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, 5000 * UnitSize(encoding));
        byte[] data = Xor(Encode(encoding, text, "\0", "trailing"), 0x5A);
        using var reader = CreateReader(data, encoding, new XorAnalyzer(0x5A));

        reader.ReadCString().Should().Be(text);
        reader.Position.Should().Be(encoding.GetByteCount(text + "\0"));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadCString_LongTextsOnNonSeekableStream_ReturnsEachText(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string first = SampleText(encoding, 2000 * UnitSize(encoding));
        string second = SampleText(encoding, 300 * UnitSize(encoding));
        using var reader = CreateReader(new NonSeekableMemoryStream(Encode(encoding, first, "\0", second + "\0")), encoding);

        reader.ReadCString().Should().Be(first);
        reader.ReadCString().Should().Be(second);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [Trait("Category", "Stress")]
    public void ReadCString_LongerThanSinglePassSize_ReturnsText(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, (17 * 1024 * 1024) + 4);
        byte[] data = Encode(encoding, text, "\0", "trailing");
        using var reader = CreateReader(data, encoding);

        reader.ReadCString().Should().Be(text);
        reader.Position.Should().Be(encoding.GetByteCount(text + "\0"));
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadCString_NoTerminatorAfterLongText_ThrowsEndOfStreamException(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        using var reader = CreateReader(encoding.GetBytes(SampleText(encoding, 5000 * UnitSize(encoding))), encoding);

        FluentActions.Invoking(reader.ReadCString).Should().ThrowExactly<EndOfStreamException>();
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadCString_RandomBytes_MatchesEncodingGetString(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unitSize = UnitSize(encoding);

        foreach (int byteLength in new[] { 100, 127 * unitSize, 129 * unitSize, 3001 * unitSize, 70_001 * unitSize })
        {
            byte[] content = WithoutNullUnits(RandomBytes(byteLength, byteLength), unitSize);
            using var reader = CreateReader([.. content, .. new byte[unitSize], 0x41], encoding);

            reader.ReadCString().Should().Be(encoding.GetString(content), "{0} bytes", byteLength);
            reader.Position.Should().Be(content.Length + unitSize);
        }
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadCString_RegionViewEndsInsideLongText_ThrowsEndOfStreamExceptionAtRegionEnd(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unitSize = UnitSize(encoding);
        string text = SampleText(encoding, 5000 * unitSize);
        byte[] data = Encode(encoding, "", "", text + "\0");
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader([.. new byte[16], .. data], encoding, analyzer);
        reader.EnableRegionView(new RegionRange { StartPosition = 16, Size = 3000 * unitSize });

        FluentActions.Invoking(reader.ReadCString).Should().ThrowExactly<EndOfStreamException>();

        reader.ActualPosition.Should().Be(16 + (3000 * unitSize));
        analyzer.Bytes.Should().Equal(data.Take(3000 * unitSize));
    }

    [Theory]
    [MemberData(nameof(EncodingsAndShortReads))]
    public void ReadCString_ShortReadsOfLongText_ReturnsTextAndMovesPastTerminator(string encodingName, int maxBytesPerRead)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, 5000 * UnitSize(encoding));
        byte[] data = Encode(encoding, text, "\0", "trailing");
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), encoding);

        reader.ReadCString().Should().Be(text);
        reader.Position.Should().Be((5000 + 1) * UnitSize(encoding));
    }

    [Theory]
    [MemberData(nameof(EncodingsAndLengthsAroundReadBoundaries))]
    public void ReadCString_TextAroundReadBoundaries_ReturnsTextAndMovesPastTerminator(string encodingName, int byteLength)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, byteLength);
        byte[] data = Encode(encoding, text, "\0", "trailing");
        using var reader = CreateReader(data, encoding);

        reader.ReadCString().Should().Be(text);
        reader.Position.Should().Be(byteLength + UnitSize(encoding));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(70_000)]
    public void ReadLine_InvalidBytesWithThrowingEncoding_ThrowsDecoderFallbackException(int validPrefixLength)
    {
        var encoding = new UTF8Encoding(false, throwOnInvalidBytes: true);
        byte[] data = [.. Enumerable.Repeat((byte)'a', validPrefixLength), 0xE2, 0x82, (byte)'\n'];
        using var reader = CreateReader(data, encoding);

        FluentActions.Invoking(reader.ReadLine).Should().ThrowExactly<DecoderFallbackException>();
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadLine_LongLinesWithRecordingAnalyzer_NotifiesEveryByteOnce(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, 5000 * UnitSize(encoding));
        byte[] data = Encode(encoding, text, "\r\n", text + "\n" + text);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, encoding, analyzer);

        reader.ReadLine().Should().Be(text);
        reader.ReadLine().Should().Be(text);
        reader.ReadLine().Should().Be(text);

        analyzer.Bytes.Should().Equal(data);
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadLine_LongLinesWithXorAnalyzer_ReturnsDecryptedLines(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, 5000 * UnitSize(encoding));
        byte[] data = Xor(Encode(encoding, text, "\r", text + "\r\n" + text), 0x5A);
        using var reader = CreateReader(data, encoding, new XorAnalyzer(0x5A));

        reader.ReadLine().Should().Be(text);
        reader.ReadLine().Should().Be(text);
        reader.ReadLine().Should().Be(text);
        reader.Position.Should().Be(data.Length);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [Trait("Category", "Stress")]
    public void ReadLine_LongerThanSinglePassSizeWithXorAnalyzer_ReturnsEachLine(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, (17 * 1024 * 1024) + 4);
        byte[] data = Xor(Encode(encoding, text, "\r\n", "next"), 0x5A);
        using var reader = CreateReader(data, encoding, new XorAnalyzer(0x5A));

        reader.ReadLine().Should().Be(text);
        reader.ReadLine().Should().Be("next");
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadLine_RandomBytesWithoutLineBreak_MatchesEncodingGetString(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unitSize = UnitSize(encoding);

        foreach (int byteLength in new[] { 1, 127, 129, 3001, 70_001 })
        {
            // A trailing partial code unit belongs to the last line as well.
            byte[] content = WithoutLineBreakUnits(RandomBytes(byteLength, byteLength), encoding);
            using var reader = CreateReader(content, encoding);

            reader.ReadLine().Should().Be(encoding.GetString(content), "{0} bytes", byteLength);
            reader.Position.Should().Be(content.Length);
        }
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadLine_RegionViewEndsInsideLongLine_ReturnsLineUpToRegionEnd(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unitSize = UnitSize(encoding);
        string text = SampleText(encoding, 5000 * unitSize);
        byte[] data = Encode(encoding, text, "\n", "");
        using var reader = CreateReader([.. new byte[16], .. data], encoding);
        reader.EnableRegionView(new RegionRange { StartPosition = 16, Size = 3001 * unitSize });

        reader.ReadLine().Should().Be(encoding.GetString(data, 0, 3001 * unitSize));
        reader.Position.Should().Be(3001 * unitSize);
        reader.ReadLine().Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(EncodingsAndShortReads))]
    public void ReadLine_ShortReadsOfLongLines_ReturnsEachLine(string encodingName, int maxBytesPerRead)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, 3000 * UnitSize(encoding));
        byte[] data = Encode(encoding, text, "\r\n", text + "\r" + text);
        using var reader = CreateReader(new ChunkedReadStream(data, maxBytesPerRead), encoding);

        reader.ReadLine().Should().Be(text);
        reader.ReadLine().Should().Be(text);
        reader.ReadLine().Should().Be(text);
        reader.Position.Should().Be(data.Length);
    }

    [Theory]
    [MemberData(nameof(EncodingsAndLengthsAroundReadBoundaries))]
    public void ReadLine_TextAroundReadBoundaries_ReturnsEachLine(string encodingName, int byteLength)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, byteLength);
        byte[] data = Encode(encoding, text, "\r\n", "x" + text + "\r" + "y" + text + "\n" + "z" + text);
        using var reader = CreateReader(data, encoding);

        reader.ReadLine().Should().Be(text);
        reader.Position.Should().Be(byteLength + (2 * UnitSize(encoding)));
        reader.ReadLine().Should().Be("x" + text);
        reader.ReadLine().Should().Be("y" + text);
        reader.ReadLine().Should().Be("z" + text);
        reader.Position.Should().Be(data.Length);
        reader.ReadLine().Should().BeNull();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(500)]
    [InlineData(70_000)]
    public void ReadSizedCString_InvalidBytesWithThrowingEncoding_ThrowsDecoderFallbackException(int validPrefixLength)
    {
        var encoding = new UTF8Encoding(false, throwOnInvalidBytes: true);
        byte[] data = [.. Enumerable.Repeat((byte)'a', validPrefixLength), 0xC3];
        using var reader = CreateReader(data, encoding);

        FluentActions.Invoking(() => reader.ReadSizedCString(data.Length)).Should().ThrowExactly<DecoderFallbackException>();
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadSizedCString_LongTextWithRecordingAnalyzer_NotifiesEveryConsumedByteOnce(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, 5000 * UnitSize(encoding));
        byte[] data = Encode(encoding, text, "\0", "trailing");
        int consumed = encoding.GetByteCount(text + "\0");
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, encoding, analyzer);

        reader.ReadSizedCString(data.Length).Should().Be(text);

        analyzer.Bytes.Should().Equal(data.Take(consumed));
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void ReadSizedCString_RandomBytesLongerThanSinglePassSize_MatchesEncodingGetString()
    {
        byte[] content = WithoutNullUnits(RandomBytes((17 * 1024 * 1024) + 1, 17), 1);
        using var reader = CreateReader(content, Encoding.UTF8);

        reader.ReadSizedCString(content.Length).Should().Be(Encoding.UTF8.GetString(content));
        reader.Position.Should().Be(content.Length);
    }

    [Theory]
    [MemberData(nameof(Encodings))]
    public void ReadSizedCString_RegionViewHoldsLongText_ReturnsTextAndMovesToSize(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unitSize = UnitSize(encoding);
        string text = SampleText(encoding, 5000 * unitSize);
        byte[] data = Encode(encoding, text, "\0", "trailing");
        using var reader = CreateReader([.. new byte[16], .. data], encoding);
        reader.EnableRegionView(new RegionRange { StartPosition = 16, Size = data.Length });

        reader.ReadSizedCString(data.Length - unitSize).Should().Be(text);
        reader.Position.Should().Be(data.Length - unitSize);
    }

    [Theory]
    [MemberData(nameof(EncodingsAndLengthsAroundReadBoundaries))]
    public void ReadSizedCString_TextAroundReadBoundaries_ReturnsTextAndMovesPastTerminator(string encodingName, int byteLength)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, byteLength);
        byte[] data = Encode(encoding, text, "\0", "trailing");
        using var reader = CreateReader(data, encoding);

        reader.ReadSizedCString(data.Length, adjustPosition: false).Should().Be(text);
        reader.Position.Should().Be(byteLength + UnitSize(encoding));

        reader.Position = 0;
        reader.ReadSizedCString(data.Length).Should().Be(text);
        reader.Position.Should().Be(data.Length);
    }

    [Theory]
    [MemberData(nameof(EncodingsAndLengthsAroundReadBoundaries))]
    public void ReadSizedCString_TextWithoutTerminatorFillingSize_ReturnsText(string encodingName, int byteLength)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = SampleText(encoding, byteLength);
        byte[] data = Encode(encoding, text, "", "trailing");
        using var reader = CreateReader(data, encoding);

        if (byteLength == 0)
        {
            reader.ReadSizedCString(0).Should().BeEmpty();
            return;
        }

        reader.ReadSizedCString(byteLength).Should().Be(text);
        reader.Position.Should().Be(byteLength);
    }
}