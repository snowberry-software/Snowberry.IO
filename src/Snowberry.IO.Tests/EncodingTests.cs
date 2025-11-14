using System.Text;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;
using static Snowberry.IO.Tests.TestHelper;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains tests for different text encodings.
/// </summary>
public class EncodingTests
{
    [Theory]
    [InlineData("Hello World")]
    [InlineData("Test123!@#")]
    [InlineData("")]
    public void ASCII_CString_RoundTrip(string text)
    {
        CreateShared((w, _) => w.WriteCString(text),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(text, result);
        }, Encoding.ASCII);
    }

    [Theory]
    [InlineData("Hello世界")]
    [InlineData("🎉🎊🎈")]
    [InlineData("Привет")]
    [InlineData("こんにちは")]
    [InlineData("مرحبا")]
    public void UTF8_CString_RoundTrip(string text)
    {
        CreateShared((w, _) => w.WriteCString(text),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(text, result);
        }, Encoding.UTF8);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("世界")]
    public void UTF16_CString_RoundTrip(string text)
    {
        CreateShared((w, _) => w.WriteCString(text),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(text, result);
        }, Encoding.Unicode);
    }

    [Theory]
    [InlineData("Hello")]
    [InlineData("Test")]
    public void UTF32_CString_RoundTrip(string text)
    {
        CreateShared((w, _) => w.WriteCString(text),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(text, result);
        }, Encoding.UTF32);
    }

    [Fact]
    public void MixedEncodings_SeparateStreams()
    {
        const string text = "Hello";

        var asciiStream = new MemoryStream();
        using (var writer = new EndianStreamWriter(asciiStream, true, Encoding.ASCII))
        {
            writer.WriteCString(text);
        }

        asciiStream.Position = 0;

        var utf8Stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(utf8Stream, true, Encoding.UTF8))
        {
            writer.WriteCString(text);
        }

        utf8Stream.Position = 0;

        using var asciiReader = new EndianStreamReader(asciiStream, null, 0, Encoding.ASCII);
        using var utf8Reader = new EndianStreamReader(utf8Stream, null, 0, Encoding.UTF8);
        Assert.Equal(text, asciiReader.ReadCString());
        Assert.Equal(text, utf8Reader.ReadCString());
    }

    [Theory]
    [InlineData("Hello", 10)]
    [InlineData("世界", 20)]
    [InlineData("🎉🎊", 30)]
    public void UTF8_SizedCString_WithMultiByteCharacters(string text, int size)
    {
        CreateShared((w, _) => w.WriteSizedCString(text, size),
        (r, _) =>
        {
            string result = r.ReadSizedCString(size);
            Assert.Equal(text, result);
        }, Encoding.UTF8);
    }

    [Fact]
    public void UTF8_Emoji_SizePrefixedString()
    {
        const string emoji = "😀😃😄😁😆😅🤣😂";
        CreateShared((w, _) => w.Write(emoji),
        (r, _) =>
        {
            string result = r.ReadString();
            Assert.Equal(emoji, result);
        }, Encoding.UTF8);
    }

    [Fact]
    public void UTF8_CombiningCharacters()
    {
        const string text = "é"; // e + combining acute accent
        CreateShared((w, _) => w.WriteCString(text),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(text, result);
        }, Encoding.UTF8);
    }

    [Fact]
    public void Latin1_ExtendedCharacters()
    {
        var encoding = Encoding.GetEncoding("ISO-8859-1");

        const string text = "café";
        CreateShared((w, _) => w.WriteCString(text),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(text, result);
        }, encoding);
    }

    [Theory]
    [InlineData("Hello\nWorld")]
    [InlineData("Line1\r\nLine2\nHello")]
    [InlineData("Tab\tSeparated\nHello")]
    public void UTF8_WhitespaceCharacters_CString(string text)
    {
        CreateShared((w, _) => w.WriteSizedCString(text, 50),
        (r, _) =>
        {
            string result = r.ReadSizedCString(50);
            Assert.Contains("Hello", result);
        }, Encoding.UTF8);
    }

    [Fact]
    public void UTF8_VeryLongString_SizePrefixed()
    {
        string longText = new('x', 100000);
        CreateShared((w, _) => w.Write(longText),
        (r, _) =>
        {
            string result = r.ReadString();
            Assert.Equal(longText.Length, result.Length);
            Assert.Equal(longText, result);
        }, Encoding.UTF8);
    }

    [Fact]
    public void BigEndianUnicode_CString()
    {
        const string text = "Hello";
        CreateShared((w, _) => w.WriteCString(text),
        (r, _) =>
        {
            string result = r.ReadCString();
            Assert.Equal(text, result);
        }, Encoding.BigEndianUnicode);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    [InlineData("abcde")]
    public void UTF8_VariableLengthStrings_SizePrefixed(string text)
    {
        CreateShared((w, _) => w.Write(text),
        (r, _) =>
        {
            string result = r.ReadString();
            Assert.Equal(text, result);
        }, Encoding.UTF8);
    }

    [Fact]
    public void Encoding_PropertyReturnsCorrectEncoding()
    {
        var utf8Stream = new MemoryStream();
        using var utf8Writer = new EndianStreamWriter(utf8Stream, true, Encoding.UTF8);
        Assert.Equal(Encoding.UTF8, utf8Writer.Encoding);

        var asciiStream = new MemoryStream();
        using var asciiWriter = new EndianStreamWriter(asciiStream, true, Encoding.ASCII);
        Assert.Equal(Encoding.ASCII, asciiWriter.Encoding);
    }

    [Fact]
    public void ReadLine_UTF8_MixedContent()
    {
        CreateShared((w, _) =>
        {
            w.WriteLine("English");
            w.WriteLine("日本語");
            w.WriteLine("Русский");
        },
        (r, _) =>
        {
            Assert.Equal("English", r.ReadLine());
            Assert.Equal("日本語", r.ReadLine());
            Assert.Equal("Русский", r.ReadLine());
        }, Encoding.UTF8);
    }

    [Fact]
    public void SizedCString_UTF8_ExactByteCount()
    {
        const string text = "Hello";
        int byteCount = Encoding.UTF8.GetByteCount(text);

        CreateShared((w, _) => w.WriteSizedCString(text, byteCount),
        (r, _) =>
        {
            string result = r.ReadSizedCString(byteCount);
            Assert.Equal(text, result);
        }, Encoding.UTF8);
    }

    [Fact]
    public void StringCharacters_WriteAndRead()
    {
        const string text = "TestString123";
        CreateShared((w, _) =>
        {
            w.WriteStringCharacters(text);
        },
        (r, _) =>
        {
            byte[] bytes = r.ReadBytes(text.Length);
            string result = Encoding.UTF8.GetString(bytes);
            Assert.Equal(text, result);
        }, Encoding.UTF8);
    }

    [Fact]
    public void WriteStringCharacters_NullText_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentNullException>(() => writer.WriteStringCharacters(null!));
    }

    [Fact]
    public void WriteCString_NullText_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentNullException>(() => writer.WriteCString(null!));
    }
}
