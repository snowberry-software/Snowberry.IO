using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Checks that <c>ReadString</c> of the readers returns the same strings, throws the same exceptions and leaves the same position as
/// <see cref="BinaryReader.ReadString"/> on the same bytes.
/// </summary>
public class ReaderReadStringTests
{
    private static readonly string[] s_EncodingNames = ["utf-8", "utf-16", "utf-16BE", "utf-32", "utf-32BE", "iso-8859-1", "us-ascii"];

    public static TheoryData<string, int> EncodingsAndLengths()
    {
        var data = new TheoryData<string, int>();

        foreach (string name in s_EncodingNames)
        {
            foreach (int length in new[] { 0, 1, 2, 4, 127, 128, 129, 132, 4096, 70_001, 1_300_000 })
                data.Add(name, length);
        }

        return data;
    }

    public static TheoryData<string, int> EncodingsAndReadSizes()
    {
        var data = new TheoryData<string, int>();

        foreach (string name in s_EncodingNames)
        {
            foreach (int maxBytesPerRead in new[] { int.MaxValue, 1, 7, 1000 })
                data.Add(name, maxBytesPerRead);
        }

        return data;
    }

    /// <summary>
    /// Reads <paramref name="stringCount"/> strings from <paramref name="data"/> with a <see cref="BinaryReader"/> and with an
    /// <see cref="IO.Reader.EndianStreamReader"/> and asserts that both return the same strings and positions.
    /// </summary>
    private static void AssertSameStrings(byte[] data, Encoding encoding, int maxBytesPerRead, bool canSeek, int stringCount)
    {
        var expectedStream = new ChunkedReadStream(data, maxBytesPerRead, canSeek);
        using var expected = new BinaryReader(expectedStream, encoding);
        using var actual = CreateReader(new ChunkedReadStream(data, maxBytesPerRead, canSeek), encoding);

        for (int i = 0; i < stringCount; i++)
        {
            actual.ReadString().Should().Be(expected.ReadString(), "string {0}", i);

            if (canSeek)
                actual.Position.Should().Be(expectedStream.Position, "string {0}", i);
        }

        if (canSeek)
            actual.Position.Should().Be(data.Length);
    }

    private static byte[] Prefix(int length)
    {
        var prefix = new List<byte>();
        uint value = (uint)length;

        while (value >= 0x80)
        {
            prefix.Add((byte)(value | 0x80));
            value >>= 7;
        }

        prefix.Add((byte)value);
        return [.. prefix];
    }

    private static byte[] Prefixed(params byte[][] payloads)
    {
        var data = new List<byte>();

        foreach (byte[] payload in payloads)
        {
            data.AddRange(Prefix(payload.Length));
            data.AddRange(payload);
        }

        return [.. data];
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    public void ReadString_AnalyzerSetAndRemovedBetweenStrings_MatchesBinaryReaderAndNotifiesOnlyAnalyzedStrings(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] first = Prefixed(RandomBytes(7, 1));
        byte[] second = Prefixed(RandomBytes(9, 2));
        byte[] data = [.. first, .. second, .. Prefixed(RandomBytes(11, 3), RandomBytes(200, 4))];
        var analyzer = new RecordingAnalyzer();

        using var expected = new BinaryReader(new MemoryStream(data), encoding);
        using var actual = CreateReader(data, encoding);

        actual.ReadString().Should().Be(expected.ReadString());
        actual.Analyzer = analyzer;
        actual.ReadString().Should().Be(expected.ReadString());
        actual.Analyzer = null;
        actual.ReadString().Should().Be(expected.ReadString());
        actual.ReadString().Should().Be(expected.ReadString());

        actual.Position.Should().Be(data.Length);
        analyzer.Bytes.Should().Equal(second);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(100, 99)]
    [InlineData(500, 300)]
    [InlineData(70_000, 50_000)]
    [InlineData(int.MaxValue, 1000)]
    public void ReadString_DataEndsInsideString_ThrowsEndOfStreamExceptionLikeBinaryReader(int length, int available)
    {
        byte[] data = [.. Prefix(length), .. RandomBytes(available, length)];
        var analyzer = new RecordingAnalyzer();

        using var expected = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        using var actual = CreateReader(data, Encoding.UTF8, analyzer);

        var expectedException = FluentActions.Invoking(expected.ReadString).Should().ThrowExactly<EndOfStreamException>().Which;
        FluentActions.Invoking(actual.ReadString).Should().ThrowExactly<EndOfStreamException>().WithMessage(expectedException.Message);

        actual.Position.Should().Be(expected.BaseStream.Position).And.Be(data.Length);
        analyzer.Bytes.Should().Equal(data);
    }

    [Theory]
    [InlineData("utf-8", 10)]
    [InlineData("utf-8", 128)]
    [InlineData("utf-8", 5000)]
    [InlineData("utf-16", 10)]
    [InlineData("utf-16", 5000)]
    public void ReadString_InvalidBytesWithLongReplacementFallback_MatchesBinaryReader(string encodingName, int length)
    {
        // Every invalid code unit (a UTF-8 continuation byte, a lone UTF-16 low surrogate) decodes to more characters than it has bytes.
        var encoding = Encoding.GetEncoding(encodingName, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("[invalid]"));
        byte[] payload = encodingName == "utf-8"
            ? [.. Enumerable.Range(0, length).Select(i => (byte)(0x80 + (i % 64)))]
            : [.. Enumerable.Range(0, length / 2).SelectMany(_ => new byte[] { 0x00, 0xDC })];

        AssertSameStrings(Prefixed(payload, payload), encoding, int.MaxValue, canSeek: true, stringCount: 2);
    }

    // NOTE(VNC): The reader follows the BinaryReader of .NET; the one of .NET Framework accepts a fifth length byte above 0x0F and
    // words its exceptions differently, so it is no reference.
#if !NETFRAMEWORK
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F })]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 })]
    public void ReadString_InvalidLengthPrefix_ThrowsLikeBinaryReader(byte[] prefix)
    {
        using var expected = new BinaryReader(new MemoryStream(prefix), Encoding.UTF8);
        using var actual = CreateReader(prefix, Encoding.UTF8);

        var expectedException = FluentActions.Invoking(expected.ReadString).Should().Throw<Exception>().Which;
        var actualException = FluentActions.Invoking(actual.ReadString).Should().Throw<Exception>().Which;

        actualException.GetType().Should().Be(expectedException.GetType());
        actualException.Message.Should().Be(expectedException.Message);
        actual.Position.Should().Be(expected.BaseStream.Position);
    }
#endif

    [Fact]
    [Trait("Category", "Stress")]
    public void ReadString_LongerThanSinglePassSize_MatchesBinaryReader()
    {
        byte[] data = Prefixed(RandomBytes((17 * 1024 * 1024) + 3, 17), Encoding.UTF8.GetBytes("after"));

        AssertSameStrings(data, Encoding.UTF8, int.MaxValue, canSeek: true, stringCount: 2);
        AssertSameStrings(data, Encoding.UTF8, 100_000, canSeek: false, stringCount: 2);
    }

    [Theory]
    [MemberData(nameof(EncodingsAndReadSizes))]
    public void ReadString_ManyRandomPayloads_MatchesBinaryReader(string encodingName, int maxBytesPerRead)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        var random = new Random(maxBytesPerRead);
        var payloads = new List<byte[]>();

        for (int i = 0; i < 120; i++)
        {
            int length = i % 40 == 39 ? random.Next(129, 5000) : random.Next(0, 300);

            // Half of the strings are valid text that may be cut in the middle of a character, the other half random bytes.
            byte[] payload = random.Next(2) == 0
                ? encoding.GetBytes(new string('\u00E9', length)).Take(length).ToArray()
                : RandomBytes(length, i);

            payloads.Add(payload);
        }

        AssertSameStrings(Prefixed([.. payloads]), encoding, maxBytesPerRead, canSeek: true, payloads.Count);
    }

    [Theory]
    [MemberData(nameof(EncodingsAndReadSizes))]
    public void ReadString_NonSeekableStream_MatchesBinaryReader(string encodingName, int maxBytesPerRead)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] data = Prefixed(encoding.GetBytes("short"), RandomBytes(4096, 1), encoding.GetBytes(new string('x', 200)), RandomBytes(129, 2));

        AssertSameStrings(data, encoding, maxBytesPerRead, canSeek: false, stringCount: 4);
    }

    [Theory]
    [MemberData(nameof(EncodingsAndLengths))]
    public void ReadString_RandomPayloadOfLength_MatchesBinaryReader(string encodingName, int length)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        byte[] data = Prefixed(RandomBytes(length, length), RandomBytes(3, length + 1));

        AssertSameStrings(data, encoding, int.MaxValue, canSeek: true, stringCount: 2);
    }

    [Fact]
    public void ReadString_RegionViewEndsInsideLongString_ThrowsAndConsumesRegion()
    {
        byte[] data = Prefixed(RandomBytes(5000));
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader([.. new byte[8], .. data], Encoding.UTF8, analyzer);
        reader.EnableRegionView(new RegionRange { StartPosition = 8, Size = 3000 });

        FluentActions.Invoking(reader.ReadString).Should().ThrowExactly<EndOfStreamException>();

        reader.Position.Should().Be(3000);
        analyzer.Bytes.Should().Equal(data.Take(3000));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(5000)]
    public void ReadString_WithRecordingAnalyzer_NotifiesEveryByteOnce(int length)
    {
        byte[] data = Prefixed(RandomBytes(length), Encoding.UTF8.GetBytes("next"));
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, Encoding.UTF8, analyzer);

        reader.ReadString();
        reader.ReadString().Should().Be("next");

        analyzer.Bytes.Should().Equal(data);
    }

    [Theory]
    [InlineData("utf-8", 5)]
    [InlineData("utf-8", 5000)]
    [InlineData("utf-16", 5000)]
    [InlineData("utf-32BE", 5000)]
    public void ReadString_XorAnalyzer_ReturnsDecryptedText(string encodingName, int length)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string text = string.Concat(Enumerable.Repeat("Gr\u00FC\u00DFe, \u4E16\u754C! ", (length / 10) + 1))[..length];
        byte[] data = WriteBinaryWriterBytes(w => w.Write(text), encoding);
        using var reader = CreateReader([.. data.Select(b => (byte)(b ^ 0x5A))], encoding, new XorAnalyzer(0x5A));

        reader.ReadString().Should().Be(text);
        reader.Position.Should().Be(data.Length);
    }
}