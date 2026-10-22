using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common.Writer.Interfaces;
using Snowberry.IO.Writer;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Writer;

/// <summary>
/// Compares the text writes of <see cref="EndianStreamWriter"/> byte for byte with <see cref="Encoding.GetBytes(string)"/> and
/// <see cref="BinaryWriter"/>, for several encodings and for texts around the internal buffer sizes.
/// </summary>
public class EndianStreamWriterTextParityTests
{
    private const string c_Mixed = "Aé世\U0001F600";

    // Lengths around the 256-byte scratch buffer for every code unit width, the 42-char encoder chunks of the .NET Framework
    // BinaryWriter, and the length above which the text is encoded in two passes.
    private static readonly int[] s_TextLengths = [0, 1, 2, 41, 42, 43, 63, 64, 65, 81, 82, 83, 84, 85, 86, 127, 128, 129, 170, 171, 255, 256, 257, 4096, 21844, 21845, 21846, 70000];

    private static readonly int[] s_FixedSizes = [255, 256, 257, 4096, 65536, 65537];

    private static readonly Dictionary<string, Func<Encoding>> s_Encodings = new()
    {
        ["utf-8"] = () => new UTF8Encoding(false),
        ["utf-8 with preamble"] = () => Encoding.UTF8,
        ["us-ascii"] = () => Encoding.ASCII,
        ["utf-16"] = () => Encoding.Unicode,
        ["utf-16BE"] = () => Encoding.BigEndianUnicode,
        ["utf-32"] = () => Encoding.UTF32,
        ["iso-8859-1"] = () => Encoding.GetEncoding("iso-8859-1"),
        ["us-ascii with long replacement"] = () => Encoding.GetEncoding("us-ascii", new EncoderReplacementFallback("[?]"), DecoderFallback.ReplacementFallback),
        ["us-ascii with empty replacement"] = () => Encoding.GetEncoding("us-ascii", new EncoderReplacementFallback(string.Empty), DecoderFallback.ReplacementFallback),
#if !NETCOREAPP
        // Code page encodings, including stateful ones, are built into .NET Framework only.
        ["shift_jis"] = () => Encoding.GetEncoding("shift_jis"),
        ["gb18030"] = () => Encoding.GetEncoding("gb18030"),
        ["iso-2022-jp"] = () => Encoding.GetEncoding("iso-2022-jp"),
        ["utf-7"] = () => Encoding.GetEncoding("utf-7"),
#endif
    };

    private static readonly Dictionary<string, Action<EndianStreamWriter, string>> s_TextWrites = new()
    {
        ["WriteStringCharacters"] = (w, t) => w.WriteStringCharacters(t),
        ["WriteCString"] = (w, t) => w.WriteCString(t),
        ["WriteLine"] = (w, t) => w.WriteLine(t),
        ["WriteSizedCString"] = (w, t) => w.WriteSizedCString(t, t.Length + 300),
        ["Write(string)"] = (w, t) => w.Write(t),
        ["IEndianWriter.Write(string)"] = (w, t) => ((IEndianWriter)w).Write(t),
        ["Write(char[])"] = (w, t) => w.Write(t.ToCharArray()),
        ["Write(char[], int, int)"] = (w, t) => w.Write(t.ToCharArray(), 0, t.Length)
    };

    public static TheoryData<string> EncodingNames => [.. s_Encodings.Keys];

    public static TheoryData<string> TextWriteNames => [.. s_TextWrites.Keys];

#if NETCOREAPP
    // The .NET BinaryWriter encodes long char arrays in chunks and writes each chunk before it encodes the next one.
    public static TheoryData<string> LongTextWriteNames => [.. s_TextWrites.Keys.Where(k => !k.StartsWith("Write(char[]", StringComparison.Ordinal))];
#else
    public static TheoryData<string> LongTextWriteNames => [.. s_TextWrites.Keys];
#endif

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void WriteStringCharacters_EveryText_WritesEncodedBytes(string encodingName)
    {
        var encoding = s_Encodings[encodingName]();

        AssertEachText(encoding, (w, t) => w.WriteStringCharacters(t), encoding.GetBytes);
    }

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void WriteCString_EveryText_WritesEncodedBytesAndTerminator(string encodingName)
    {
        var encoding = s_Encodings[encodingName]();
        byte[] terminator = encoding.GetBytes("\0");

        AssertEachText(encoding, (w, t) => w.WriteCString(t), t => [.. encoding.GetBytes(t), .. terminator]);
    }

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void WriteLine_EveryText_WritesEncodedBytesAndNewLine(string encodingName)
    {
        var encoding = s_Encodings[encodingName]();
        byte[] newLine = encoding.GetBytes(Environment.NewLine);

        AssertEachText(encoding, (w, t) => w.WriteLine(t), t => [.. encoding.GetBytes(t), .. newLine]);
    }

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void WriteSizedCString_EveryTextAndSize_WritesEncodedBytesAndZeroPadding(string encodingName)
    {
        var encoding = s_Encodings[encodingName]();

        foreach (var (name, text) in Texts())
        {
            byte[] encoded = encoding.GetBytes(text);
            int minimum = Math.Max(encoded.Length, text.Length);
            int[] sizes = [minimum, minimum + 1, minimum + 7, .. s_FixedSizes.Where(s => s >= minimum)];

            AssertEachText(encoding, [(name, text)], sizes, (w, t, size) => w.WriteSizedCString(t, size), (t, size) => [.. encoded, .. new byte[size - encoded.Length]]);
        }
    }

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void WriteString_EveryText_MatchesBinaryWriter(string encodingName)
    {
        var encoding = s_Encodings[encodingName]();

        AssertEachText(encoding, (w, t) => w.Write(t), t => WriteBinaryWriterBytes(b => b.Write(t), encoding));
    }

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void WriteString_EveryTextThroughInterface_MatchesBinaryWriter(string encodingName)
    {
        var encoding = s_Encodings[encodingName]();

        AssertEachText(encoding, (w, t) => ((IEndianWriter)w).Write(t), t => WriteBinaryWriterBytes(b => b.Write(t), encoding));
    }

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void WriteCharArray_EveryText_MatchesBinaryWriter(string encodingName)
    {
        var encoding = s_Encodings[encodingName]();

        AssertEachText(encoding, (w, t) => w.Write(t.ToCharArray()), t => WriteBinaryWriterBytes(b => b.Write(t.ToCharArray()), encoding));
    }

    [Theory]
    [MemberData(nameof(EncodingNames))]
    public void WriteCharArrayWithIndexAndCount_EveryText_MatchesBinaryWriter(string encodingName)
    {
        var encoding = s_Encodings[encodingName]();

        AssertEachText(
            encoding,
            (w, t) => w.Write(t.ToCharArray(), Math.Min(1, t.Length), Math.Max(0, t.Length - 2)),
            t => WriteBinaryWriterBytes(b => b.Write(t.ToCharArray(), Math.Min(1, t.Length), Math.Max(0, t.Length - 2)), encoding));
    }

    [Theory]
    [MemberData(nameof(TextWriteNames))]
    public void TextWrite_UnencodableShortText_ThrowsEncoderFallbackExceptionAndWritesNothing(string method)
    {
        AssertThrowsEncoderFallbackAndWritesNothing(s_TextWrites[method], "abé");
    }

    [Theory]
    [MemberData(nameof(LongTextWriteNames))]
    public void TextWrite_UnencodableLongText_ThrowsEncoderFallbackExceptionAndWritesNothing(string method)
    {
        AssertThrowsEncoderFallbackAndWritesNothing(s_TextWrites[method], new string('a', 70000) + "é");
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(2, 5)]
    [InlineData(6, 0)]
    public void WriteCharArrayWithIndexAndCount_InvalidRange_ThrowsLikeBinaryWriterAndWritesNothing(int index, int count)
    {
        char[] chars = "Hello".ToCharArray();

        foreach (string name in s_Encodings.Keys)
        {
            var encoding = s_Encodings[name]();
            var expected = FluentActions.Invoking(() => WriteBinaryWriterBytes(b => b.Write(chars, index, count), encoding)).Should().Throw<ArgumentException>().Which;
            var stream = new MemoryStream();
            using var writer = new EndianStreamWriter(stream, true, encoding);

            var actual = FluentActions.Invoking(() => writer.Write(chars, index, count)).Should().Throw<ArgumentException>().Which;

            actual.GetType().Should().Be(expected.GetType(), "the encoding is {0}", name);
            actual.ParamName.Should().Be(expected.ParamName, "the encoding is {0}", name);
            stream.Length.Should().Be(0);
        }
    }

    [Fact]
    public void WriteCharArrayWithIndexAndCount_NullArray_ThrowsArgumentNullExceptionForChars()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        FluentActions.Invoking(() => writer.Write((char[])null!, 0, 0))
            .Should().ThrowExactly<ArgumentNullException>()
            .Which.ParamName.Should().Be("chars");
    }

    [Fact]
    public void WriteSizedCString_EncodedTextLongerThanSize_ReportsByteCountAndWritesNothing()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true, Encoding.UTF8);

        FluentActions.Invoking(() => writer.WriteSizedCString("\U0001F600", 3))
            .Should().ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("The byte count of the text `4` is greater than the specified size `3`.\nCheck the data of the text or the encoding that is used.*")
            .Which.ParamName.Should().Be("text");

        stream.Length.Should().Be(0);
    }

    [Fact]
    public void WriteSizedCString_LongEncodedTextLongerThanSize_ReportsByteCountAndWritesNothing()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true, Encoding.UTF8);
        string text = new('é', 300);

        FluentActions.Invoking(() => writer.WriteSizedCString(text, 599))
            .Should().ThrowExactly<ArgumentOutOfRangeException>()
            .WithMessage("The byte count of the text `600` is greater than the specified size `599`.*")
            .Which.ParamName.Should().Be("text");

        stream.Length.Should().Be(0);
    }

    [Fact]
    public void WriteSizedCString_SizeSmallerThanTextLength_WritesNothing()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true, Encoding.UTF8);

        FluentActions.Invoking(() => writer.WriteSizedCString("Hello", 4))
            .Should().ThrowExactly<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("size");

        stream.Length.Should().Be(0);
    }

    [Fact]
    public void WriteString_Null_ThrowsArgumentNullExceptionForValue()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        FluentActions.Invoking(() => writer.Write((string)null!))
            .Should().ThrowExactly<ArgumentNullException>()
            .Which.ParamName.Should().Be("value");
        FluentActions.Invoking(() => ((IEndianWriter)writer).Write((string)null!))
            .Should().ThrowExactly<ArgumentNullException>()
            .Which.ParamName.Should().Be("value");
    }

    [Fact]
    public void WriteCharArray_Null_ThrowsArgumentNullExceptionForChars()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        FluentActions.Invoking(() => writer.Write((char[])null!))
            .Should().ThrowExactly<ArgumentNullException>()
            .Which.ParamName.Should().Be("chars");
    }

    [Fact]
    public void WriteLine_NullText_ThrowsArgumentNullExceptionForText()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        FluentActions.Invoking(() => writer.WriteLine(null!))
            .Should().ThrowExactly<ArgumentNullException>()
            .Which.ParamName.Should().Be("text");
    }

    [Fact]
    public void TextWrites_Interleaved_WriteEachTextInOrder()
    {
        var encoding = new UTF8Encoding(false);
        string longText = new('世', 300);
        byte[] expected =
        [
            .. encoding.GetBytes("abc"), 0,
            .. encoding.GetBytes(longText), 0,
            .. encoding.GetBytes("d"), .. new byte[9],
            .. encoding.GetBytes(longText), .. encoding.GetBytes(Environment.NewLine),
            .. WriteBinaryWriterBytes(b => b.Write("ef"), encoding),
            .. encoding.GetBytes("g"), 0
        ];

        byte[] actual = WriteBytes(w => w.WriteCString("abc").WriteCString(longText).WriteSizedCString("d", 10).WriteLine(longText).Write("ef").WriteCString("g"), encoding);

        actual.Should().Equal(expected);
    }

    /// <summary>
    /// Creates the texts that every encoding is checked with: ASCII and mixed texts of every length in <see cref="s_TextLengths"/>,
    /// where the mixed texts end in a lone high surrogate whenever the length cuts a surrogate pair, and texts with lone surrogates.
    /// </summary>
    /// <returns>The texts with a name for the assertion messages.</returns>
    private static IEnumerable<(string Name, string Text)> Texts()
    {
        foreach (int length in s_TextLengths)
        {
            yield return ($"ascii[{length}]", new string('a', length));
            yield return ($"mixed[{length}]", Repeat(c_Mixed, length));
        }

        yield return ("lone high surrogate", "lone\uD800x");
        yield return ("lone low surrogate", "\uDC00");
        yield return ("trailing high surrogate", "abc\uD800");
        yield return ("embedded null", "a\0b");
        yield return ("surrogate pair across a 42-char chunk", new string('a', 41) + "\U0001F600" + new string('b', 300));
    }

    private static string Repeat(string pattern, int length)
    {
        var builder = new StringBuilder(length + pattern.Length);

        while (builder.Length < length)
            builder.Append(pattern);

        return builder.ToString(0, length);
    }

    /// <summary>
    /// Writes every text of <see cref="Texts"/> with one writer, so that buffers reused between the writes are covered, and compares
    /// the bytes of each write with <paramref name="expected"/>.
    /// </summary>
    private static void AssertEachText(Encoding encoding, Action<EndianStreamWriter, string> write, Func<string, byte[]> expected)
    {
        AssertEachText(encoding, Texts(), [0], (w, t, _) => write(w, t), (t, _) => expected(t));
    }

    private static void AssertEachText(
        Encoding encoding,
        IEnumerable<(string Name, string Text)> texts,
        int[] sizes,
        Action<EndianStreamWriter, string, int> write,
        Func<string, int, byte[]> expected)
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true, encoding);

        foreach (var (name, text) in texts)
        {
            foreach (int size in sizes)
            {
                stream.SetLength(0);

                write(writer, text, size);

                byte[] actual = stream.ToArray();
                byte[] expectedBytes = expected(text, size);

                if (!actual.AsSpan().SequenceEqual(expectedBytes))
                    actual.Should().Equal(expectedBytes, "the text is {0} and the size is {1}", name, size);
            }
        }
    }

    private static void AssertThrowsEncoderFallbackAndWritesNothing(Action<EndianStreamWriter, string> write, string text)
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true, Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));

        FluentActions.Invoking(() => write(writer, text)).Should().ThrowExactly<EncoderFallbackException>();

        stream.Length.Should().Be(0);
    }
}