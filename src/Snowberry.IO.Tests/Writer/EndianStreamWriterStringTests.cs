using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common.Writer.Interfaces;
using Snowberry.IO.Tests.Infrastructure;
using Snowberry.IO.Writer;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Writer;

/// <summary>
/// Contains tests for the text writes and the encoding of <see cref="EndianStreamWriter"/>.
/// </summary>
public class EndianStreamWriterStringTests
{
    private static readonly Dictionary<string, Action<IEndianWriter>> s_NullTextWrites = new()
    {
        ["WriteCString"] = w => w.WriteCString(null!),
        ["WriteSizedCString"] = w => w.WriteSizedCString(null!, 10),
        ["WriteStringCharacters"] = w => w.WriteStringCharacters(null!)
    };

    [Fact]
    public void Encoding_Default_IsUtf8WithoutPreambleOnEveryTarget()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);
        using var reader = CreateReader([]);

        writer.Encoding.Should().BeOfType<UTF8Encoding>();
        writer.Encoding.CodePage.Should().Be(65001);
        writer.Encoding.GetPreamble().Should().BeEmpty();
        reader.Encoding.Should().BeSameAs(writer.Encoding);
    }

    [Fact]
    public void Encoding_PassedToConstructor_IsReturned()
    {
        using var utf8Writer = new EndianStreamWriter(new MemoryStream(), true, Encoding.UTF8);
        using var asciiWriter = new EndianStreamWriter(new MemoryStream(), true, Encoding.ASCII);

        utf8Writer.Encoding.Should().Be(Encoding.UTF8);
        asciiWriter.Encoding.Should().Be(Encoding.ASCII);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(84)]
    [InlineData(85)]
    [InlineData(4096)]
    [InlineData(21845)]
    public void WriteCStringAndWriteLine_TextUpToSinglePassLength_WriteTextAndSuffixWithOneStreamCall(int length)
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);
        string text = new('a', length);

        writer.WriteCString(text);
        stream.WriteCalls.Should().Be(1);

        stream.Reset();
        writer.WriteLine(text);
        stream.WriteCalls.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(CStringTerminators))]
    public void WriteCString_Encoding_EndsWithTerminatorOfCodeUnitWidth(string encodingName, string text, byte[] expected)
    {
        byte[] bytes = WriteBytes(w => w.WriteCString(text), Encoding.GetEncoding(encodingName));

        bytes.Length.Should().Be(expected.Length);
        bytes.Should().Equal(expected);
    }

    [Fact]
    public void WriteSizedCString_EmptyTextAndZeroSize_DoesNotCallStream()
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.WriteSizedCString(string.Empty, 0);

        stream.WriteCalls.Should().Be(0);
    }

    [Fact]
    public void WriteSizedCString_EncodedTextLongerThanSize_ThrowsForText()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true, Encoding.UTF8);

        // The emoji is two UTF-16 chars, so it passes the length check against the size of 3, but it takes four UTF-8 bytes.
        FluentActions.Invoking(() => writer.WriteSizedCString("\U0001F600", 3))
            .Should().ThrowExactly<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("text");
    }

    [Fact]
    public void WriteSizedCString_NegativeSize_ThrowsArgumentOutOfRangeException()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        FluentActions.Invoking(() => writer.WriteSizedCString("test", -1)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WriteSizedCString_SizeSmallerThanTextLength_ThrowsForSize()
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true, Encoding.ASCII);

        FluentActions.Invoking(() => writer.WriteSizedCString("HelloWorld", 3))
            .Should().ThrowExactly<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("size");
    }

    [Theory]
    [InlineData("HelloWorld", 5)]
    [InlineData("Hello", 3)]
    [InlineData("\U0001F600\U0001F600\U0001F600\U0001F600", 5)]
    public void WriteSizedCString_SizeSmallerThanText_ThrowsArgumentOutOfRangeException(string text, int size)
    {
        using var writer = new EndianStreamWriter(new MemoryStream(), true, Encoding.UTF8);

        FluentActions.Invoking(() => writer.WriteSizedCString(text, size)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("Hello", 5)]
    [InlineData("Hello", 10)]
    [InlineData("Hello", 256)]
    [InlineData("Hello", 257)]
    [InlineData("Hello", 4096)]
    [InlineData("Hello", 65536)]
    public void WriteSizedCString_SizeUpTo64KiB_WritesTextAndPaddingWithOneStreamCall(string text, int size)
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.WriteSizedCString(text, size);

        stream.WriteCalls.Should().Be(1);
        stream.Length.Should().Be(size);
    }

    [Fact]
    public void WriteSizedCString_TextShorterThanSize_PadsWithZeros()
    {
        byte[] bytes = WriteBytes(w => w.WriteSizedCString("Hello", 10));

        bytes.Should().Equal((byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, 0, 0, 0, 0);
    }

    [Fact]
    public void WriteStringAndCString_DefaultEncoding_WriteUtf8()
    {
        byte[] bytes = WriteBytes(w => w.Write("\u00E9").WriteCString("\u00E9"));

        bytes.Should().Equal(0x02, 0xC3, 0xA9, 0xC3, 0xA9, 0x00);

        using var reader = CreateReader(bytes);
        reader.ReadString().Should().Be("\u00E9");
        reader.ReadCString().Should().Be("\u00E9");
    }

    [Fact]
    public void WriteStringCharacters_EmptyText_DoesNotCallStream()
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.WriteStringCharacters(string.Empty);

        stream.WriteCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(84)]
    [InlineData(85)]
    [InlineData(4096)]
    [InlineData(21845)]
    public void WriteStringCharacters_TextUpToSinglePassLength_WritesWithOneStreamCall(int length)
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);

        writer.WriteStringCharacters(new string('a', length));

        stream.WriteCalls.Should().Be(1);
    }

    [Fact]
    public void WriteStringCharacters_Text_WritesEncodedCharactersOnly()
    {
        const string text = "TestString123";

        WriteThenRead(
            w => w.WriteStringCharacters(text),
            r => Encoding.UTF8.GetString(r.ReadBytes(text.Length)).Should().Be(text),
            Encoding.UTF8);
    }

    [Theory]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(21845)]
    [InlineData(21846)]
    public void WriteString_EncodedLengthAtPrefixSizeBoundary_MatchesBinaryWriter(int length)
    {
        string text = new('a', length);
        byte[] expected = WriteBinaryWriterBytes(w => w.Write(text), Encoding.ASCII);

        WriteBytes(w => w.Write(text), Encoding.ASCII).Should().Equal(expected);
    }

#if !NETCOREAPP
    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(82)]
    [InlineData(83)]
    [InlineData(4096)]
    [InlineData(21845)]
    public void WriteString_TextUpToSinglePassLength_WritesLengthAndTextWithOneStreamCall(int length)
    {
        var stream = new WriteCallCountingStream();
        using var writer = new EndianStreamWriter(stream, true);
        string text = new('a', length);

        writer.Write(text);
        stream.WriteCalls.Should().Be(1);

        stream.Reset();
        ((IEndianWriter)writer).Write(text);
        stream.WriteCalls.Should().Be(1);
    }
#endif

    [Theory]
    [InlineData("WriteCString")]
    [InlineData("WriteSizedCString")]
    [InlineData("WriteStringCharacters")]
    public void WriteText_NullText_ThrowsArgumentNullException(string method)
    {
        var write = s_NullTextWrites[method];
        using var writer = new EndianStreamWriter(new MemoryStream(), true);

        FluentActions.Invoking(() => write(writer)).Should().ThrowExactly<ArgumentNullException>();
    }

    public static TheoryData<string, string, byte[]> CStringTerminators => new()
    {
        { "us-ascii", "Hi", [(byte)'H', (byte)'i', 0] },
        { "utf-16", "A", [(byte)'A', 0, 0, 0] },
        { "utf-16BE", "A", [0, (byte)'A', 0, 0] },
        { "utf-32", "A", [(byte)'A', 0, 0, 0, 0, 0, 0, 0] }
    };
}