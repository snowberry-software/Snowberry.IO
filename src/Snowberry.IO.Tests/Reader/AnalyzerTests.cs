using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for attaching an <see cref="Analyzer"/> to a reader.
/// </summary>
public class AnalyzerTests
{
    /// <summary>
    /// Runs <paramref name="read"/> once with an analyzer that declares that it modifies bytes and once with one that does not.
    /// </summary>
    private static (CallCountingAnalyzer PerUnit, CallCountingAnalyzer Batched) ReadWithBothAnalyzers(byte[] data, Encoding encoding, Action<IO.Reader.EndianStreamReader> read)
    {
        var perUnit = new CallCountingAnalyzer(modifiesBytes: true);
        var batched = new CallCountingAnalyzer(modifiesBytes: false);

        using (var reader = CreateReader(data, encoding, perUnit))
            read(reader);

        using (var reader = CreateReader(data, encoding, batched))
            read(reader);

        return (perUnit, batched);
    }

    private static byte[] Xor(byte[] data, byte key)
    {
        return [.. data.Select(b => (byte)(b ^ key))];
    }

    [Fact]
    public void Analyzer_Replaced_NotifiesOnlyNewAnalyzer()
    {
        var first = new RecordingAnalyzer();
        var second = new RecordingAnalyzer();
        using var reader = CreateReader(new byte[100], analyzer: first);

        reader.ReadInt32();
        first.TotalBytes.Should().Be(4);
        second.TotalBytes.Should().Be(0);

        reader.Analyzer = second;
        reader.ReadInt32();

        first.TotalBytes.Should().Be(4);
        second.TotalBytes.Should().Be(4);
    }

    [Fact]
    public void Analyzer_SetAfterConstruction_ReturnsNewAnalyzer()
    {
        var first = new RecordingAnalyzer();
        var second = new RecordingAnalyzer();
        using var reader = CreateReader(new byte[10], analyzer: first);

        reader.Analyzer.Should().Be(first);

        reader.Analyzer = second;

        reader.Analyzer.Should().Be(second);
    }

    [Fact]
    public void Analyzer_SetToNull_StopsNotifications()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new byte[100], analyzer: analyzer);

        reader.ReadInt32();
        analyzer.TotalBytes.Should().Be(4);

        reader.Analyzer = null;
        reader.ReadInt32();

        analyzer.TotalBytes.Should().Be(4);
    }

    [Fact]
    public void Constructor_WithAnalyzer_InitializesAnalyzer()
    {
        var analyzer = new RecordingAnalyzer();

        using var reader = CreateReader([], analyzer: analyzer);

        analyzer.Initialized.Should().BeTrue();
    }

    [Fact]
    public void CopyTo_WithAnalyzer_NotifiesCopiedBytes()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new byte[100], analyzer: analyzer);

        reader.CopyTo(new MemoryStream(), 50);

        analyzer.TotalBytes.Should().Be(50);
    }

    [Fact]
    public void ModifiesBytes_NotOverridden_IsTrue()
    {
        new RecordingAnalyzer().ModifiesBytes.Should().BeTrue();
    }

    [Fact]
    public void ReadBytes_WithAnalyzerInsideRegionView_NotifiesReadBytes()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new byte[100], analyzer: analyzer);
        reader.EnableRegionView(20..80);

        reader.ReadBytes(30);

        analyzer.TotalBytes.Should().Be(30);
    }

    [Fact]
    public void ReadBytes_WithAnalyzer_NotifiesReadBytes()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new byte[100], analyzer: analyzer);

        reader.ReadBytes(50);

        analyzer.TotalBytes.Should().Be(50);
    }

    [Theory]
    [MemberData(nameof(StringEncodings))]
    public void ReadCString_AnalyzerThatDoesNotModifyBytes_NotifiesSameBytesInFewerCalls(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string longText = new('x', 3000);
        byte[] data = encoding.GetBytes("Hi\0" + longText + "\0tail");

        var (perUnit, batched) = ReadWithBothAnalyzers(data, encoding, reader =>
        {
            reader.ReadCString().Should().Be("Hi");
            reader.ReadCString().Should().Be(longText);
        });

        batched.Bytes.Should().Equal(perUnit.Bytes).And.Equal(data.Take(encoding.GetByteCount("Hi\0" + longText + "\0")));
        batched.Calls.Should().BeLessThan(perUnit.Calls);
    }

    [Fact]
    public void ReadCString_WithAnalyzer_NotifiesOnlyConsumedBytes()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader([(byte)'H', (byte)'i', 0, (byte)'W', (byte)'o', (byte)'r', (byte)'l', (byte)'d'], analyzer: analyzer);

        string result = reader.ReadCString();

        result.Should().Be("Hi");
        analyzer.TotalBytes.Should().Be(3);
        reader.Position.Should().Be(3);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void ReadCString_XorAnalyzer_ReturnsDecryptedText(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        using var reader = CreateReader(Xor(encoding.GetBytes("Hi\0World"), 0x5A), encoding, new XorAnalyzer(0x5A));

        reader.ReadCString().Should().Be("Hi");
        reader.Position.Should().Be(encoding.GetByteCount("Hi\0"));
    }

    [Fact]
    public void ReadInt32_WithAnalyzer_NotifiesEachRead()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader([1, 2, 3, 4, 5, 6, 7, 8], analyzer: analyzer);

        reader.ReadInt32();
        analyzer.TotalBytes.Should().Be(4);

        reader.ReadInt32();
        analyzer.TotalBytes.Should().Be(8);
    }

    [Fact]
    public void ReadInt32_WithoutAnalyzer_ReadsWithoutError()
    {
        using var reader = CreateReader([1, 2, 3, 4], analyzer: null);

        FluentActions.Invoking(() => reader.ReadInt32()).Should().NotThrow();

        reader.Analyzer.Should().BeNull();
    }

    [Theory]
    [InlineData(0xFF, 100)]
    [InlineData(0xAA, 12345)]
    public void ReadInt32_XorAnalyzer_ReturnsDecryptedValue(byte key, int value)
    {
        using var reader = CreateReader(Xor(WriteBytes(w => w.Write(value)), key), analyzer: new XorAnalyzer(key));

        reader.ReadInt32().Should().Be(value);
    }

    [Theory]
    [MemberData(nameof(StringEncodings))]
    public void ReadLine_AnalyzerThatDoesNotModifyBytes_NotifiesSameBytesInFewerCalls(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string longText = new('x', 3000);
        byte[] data = encoding.GetBytes("One\r\nTwo\rThree\n" + longText + "\nLast");

        var (perUnit, batched) = ReadWithBothAnalyzers(data, encoding, reader =>
        {
            reader.ReadLine().Should().Be("One");
            reader.ReadLine().Should().Be("Two");
            reader.ReadLine().Should().Be("Three");
            reader.ReadLine().Should().Be(longText);
            reader.ReadLine().Should().Be("Last");
        });

        batched.Bytes.Should().Equal(perUnit.Bytes);
        batched.Calls.Should().BeLessThan(perUnit.Calls);
    }

    [Fact]
    public void ReadLine_WithAnalyzer_NotifiesEveryByteOnce()
    {
        byte[] data = Encoding.UTF8.GetBytes("One\r\nTwo\nThree");
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, analyzer: analyzer);

        reader.ReadLine().Should().Be("One");
        reader.ReadLine().Should().Be("Two");
        reader.ReadLine().Should().Be("Three");

        analyzer.Bytes.Should().Equal(data);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void ReadLine_XorAnalyzer_ReturnsDecryptedLines(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        using var reader = CreateReader(Xor(encoding.GetBytes("One\r\nTwo\rThree"), 0x5A), encoding, new XorAnalyzer(0x5A));

        reader.ReadLine().Should().Be("One");
        reader.ReadLine().Should().Be("Two");
        reader.ReadLine().Should().Be("Three");
    }

    [Fact]
    public void ReadPrimitives_WithAnalyzer_NotifiesAllBytes()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new byte[100], analyzer: analyzer);

        reader.ReadByte();
        reader.ReadInt16();
        reader.ReadInt32();
        reader.ReadInt64();

        analyzer.TotalBytes.Should().Be(1 + 2 + 4 + 8);
    }

    [Theory]
    [MemberData(nameof(StringEncodings))]
    public void ReadSizedCString_AnalyzerThatDoesNotModifyBytes_NotifiesSameBytesInFewerCalls(string encodingName)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        string longText = new('x', 3000);
        byte[] first = encoding.GetBytes("Hi\0padding");
        byte[] data = [.. first, .. encoding.GetBytes(longText)];

        var (perUnit, batched) = ReadWithBothAnalyzers(data, encoding, reader =>
        {
            reader.ReadSizedCString(first.Length).Should().Be("Hi");
            reader.ReadSizedCString(data.Length - first.Length).Should().Be(longText);
        });

        batched.Bytes.Should().Equal(perUnit.Bytes).And.Equal([.. encoding.GetBytes("Hi\0"), .. encoding.GetBytes(longText)]);
        batched.Calls.Should().BeLessThan(perUnit.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadSizedCString_WithAnalyzer_NotifiesOnlyBytesUpToTerminator(bool adjustPosition)
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(Encoding.UTF8.GetBytes("Hi\0WorldXX"), analyzer: analyzer);

        string result = reader.ReadSizedCString(10, adjustPosition);

        result.Should().Be("Hi");
        analyzer.Bytes.Should().Equal((byte)'H', (byte)'i', 0);
    }

    [Fact]
    public void ReadSizedCString_XorAnalyzer_ReturnsDecryptedText()
    {
        using var reader = CreateReader(Xor(Encoding.UTF8.GetBytes("Hi\0WorldXX"), 0x5A), analyzer: new XorAnalyzer(0x5A));

        reader.ReadSizedCString(10, adjustPosition: false).Should().Be("Hi");
        reader.Position.Should().Be(3);
    }

    [Fact]
    public void ReadUntilEnd_WithAnalyzer_NotifiesAllBytes()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new byte[100], analyzer: analyzer);

        reader.ReadUntilEnd();

        analyzer.TotalBytes.Should().Be(100);
    }

    [Fact]
    public void TryReadInt32_PartialDataOnNonSeekableStream_NotifiesTheConsumedBytes()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(new NonSeekableMemoryStream([1, 2]), analyzer: analyzer);

        reader.TryReadInt32(out _).Should().BeFalse();

        analyzer.Bytes.Should().Equal(1, 2);
    }

    [Fact]
    public void TryReadInt32_PartialDataOnSeekableStream_NotifiesNothing()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader([1, 2], analyzer: analyzer);

        reader.TryReadInt32(out _).Should().BeFalse();
        analyzer.TotalBytes.Should().Be(0);

        reader.ReadBytes(2);

        analyzer.Bytes.Should().Equal(1, 2);
    }

    [Fact]
    public void TryReadInt32_WithAnalyzer_NotifiesTheValueBytesOnce()
    {
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader([1, 2, 3, 4], analyzer: analyzer);

        reader.TryReadInt32(out _).Should().BeTrue();

        analyzer.Bytes.Should().Equal(1, 2, 3, 4);
    }

    [Theory]
    [InlineData(EndianType.LITTLE)]
    [InlineData(EndianType.BIG)]
    public void TryReadInt32_XorAnalyzer_ReturnsDecryptedValue(EndianType endian)
    {
        using var reader = CreateReader(Xor(WriteBytes(w => w.Write(12345, endian)), 0xAA), analyzer: new XorAnalyzer(0xAA));

        reader.TryReadInt32(out int value, endian).Should().BeTrue();

        value.Should().Be(12345);
    }

    public static TheoryData<string> StringEncodings => ["utf-8", "utf-16", "utf-16BE", "utf-32", "utf-32BE", "iso-8859-1", "us-ascii"];
}