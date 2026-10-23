using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Extensions;
using Snowberry.IO.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for <see cref="ReaderExtensions"/>.
/// </summary>
public class ReaderExtensionsTests
{
    /// <summary>
    /// Finds the non-overlapping matches of <paramref name="pattern"/> byte by byte, the way the scan is specified.
    /// </summary>
    private static List<long> ReferenceScan(byte[] data, string pattern, long startPosition, long maxAddress)
    {
        int[] values = [.. pattern.Split(' ').Select(token => token == "??" ? -1 : Convert.ToInt32(token, 16))];
        var matches = new List<long>();
        long position = startPosition;

        while (position + values.Length <= data.Length && position <= maxAddress)
        {
            bool isMatch = true;

            for (int j = 0; j < values.Length && isMatch; j++)
                isMatch = values[j] < 0 || data[position + j] == values[j];

            if (isMatch)
            {
                matches.Add(position);
                position += values.Length;
            }
            else
            {
                position++;
            }
        }

        return matches;
    }

    [Fact]
    public void ReadAt_BigEndianValuesInInternalBuffer_ReturnsWrittenValues()
    {
        var hash = new Sha1(RandomBytes(Sha1.c_StructSize));
        byte[] data = WriteBytes(w => w
            .Write(30, EndianType.BIG)
            .Write(30L, EndianType.BIG)
            .Write(30F, EndianType.BIG)
            .Write(30u, EndianType.BIG)
            .Write(30d, EndianType.BIG)
            .Write(hash));

        const int size = 48;
        using var reader = new EndianStreamReader(new MemoryStream(data), null, size);
        reader.ReadInInternalBuffer(size, 0);

        reader.ReadInt32At(EndianType.BIG, 0).Should().Be(30);
        reader.ReadInt64At(EndianType.BIG, 4).Should().Be(30L);
        reader.ReadSingleAt(EndianType.BIG, 12).Should().Be(30F);
        reader.ReadUInt32At(EndianType.BIG, 16).Should().Be(30u);
        reader.ReadDoubleAt(EndianType.BIG, 20).Should().Be(30d);
        reader.ReadSha1At(28).Should().Be(hash);
    }

    [Fact]
    public void ReadAt_LittleEndianValuesInInternalBuffer_ReturnsWrittenValues()
    {
        byte[] data = WriteBytes(w => w
            .Write((short)100, EndianType.LITTLE)
            .Write((ushort)200, EndianType.LITTLE)
            .Write(Guid.NewGuid(), EndianType.LITTLE));

        using var reader = new EndianStreamReader(new MemoryStream(data), null, 100);
        reader.ReadInInternalBuffer(20, 0);

        reader.ReadInt16At(EndianType.LITTLE, 0).Should().Be(100);
        reader.ReadUInt16At(EndianType.LITTLE, 2).Should().Be(200);
    }

    [Theory]
    [InlineData("E8 ?? ?? ?? ?? 48 8B D8", 0)]
    [InlineData("48 8B 05 1D 2C 3B 4A 59", 0)]
    [InlineData("48 8B 05 ?? ?? ?? ?? 59", 4096)]
    [InlineData("00 00 E8 ?? 48", 0)]
    [InlineData("D8 ?? D8", 100_000)]
    public void ScanSignatures_CodeLikeDataWithZeroHeader_MatchesReferenceScan(string pattern, int headerLength)
    {
        // Bytes as frequent as in x64 code after a header of zeros, so the first bytes of the data are not like the rest.
        byte[] frequent = [0x00, 0x48, 0x8B, 0x89, 0xFF, 0xE8, 0x0F, 0x83, 0x4C, 0x24];
        var random = new Random(7);
        byte[] data = new byte[2 * 1024 * 1024];

        for (int i = headerLength; i < data.Length; i++)
            data[i] = random.Next(3) == 0 ? (byte)random.Next(256) : frequent[random.Next(frequent.Length)];

        byte[] signature = [0xE8, 0x11, 0x22, 0x33, 0x44, 0x48, 0x8B, 0xD8];

        for (int position = headerLength + 5; position + signature.Length <= data.Length; position += 99_991)
            signature.CopyTo(data, position);

        using var reader = CreateReader(data);

        reader.ScanSignatures(pattern, int.MaxValue, long.MaxValue).Should().Equal(ReferenceScan(data, pattern, 0, long.MaxValue));
    }

    [Theory]
    [InlineData("44 ZZ")]
    [InlineData("?A 44")]
    [InlineData("44 ?? 4G")]
    public void ScanSignatures_InvalidHexByte_ThrowsFormatException(string pattern)
    {
        using var reader = CreateReader([1, 2, 3]);

        FluentActions.Invoking(() => reader.ScanSignatures(pattern, 10, 100)).Should().ThrowExactly<FormatException>();
    }

    [Fact]
    public void ScanSignatures_LeadingWildcardBeforeStartPosition_DoesNotMatchBeforeStart()
    {
        byte[] data = [0x44, 0x44, 0x00, 0x00, 0x00];
        using var reader = CreateReader(data);
        reader.Position = 1;

        reader.ScanSignatures("?? 44", 10, data.Length).Should().BeEmpty();
    }

    [Theory]
    [InlineData("AA BB CC DD EE FF", 0)]
    [InlineData("AA BB CC DD EE FF", 1)]
    [InlineData("AA BB CC DD EE FF", 5)]
    [InlineData("?? BB CC ?? EE FF", 0)]
    [InlineData("AA ?? ?? ?? ?? FF", 3)]
    [InlineData("?? ?? ?? ?? ?? FF", 1)]
    public void ScanSignatures_MatchAcrossInternalBufferRefill_ReturnsMatchPosition(string pattern, int offset)
    {
        // The signature repeats without a gap, so some occurrence straddles every refill boundary of the scan buffer.
        byte[] signature = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];
        byte[] data = [.. new byte[offset], .. Enumerable.Range(0, 400_000).SelectMany(_ => signature)];

        using var reader = CreateReader(data);

        var results = reader.ScanSignatures(pattern, int.MaxValue, data.Length);

        results.Should().Equal(Enumerable.Range(0, 400_000).Select(i => (long)offset + (i * signature.Length)));
    }

    [Fact]
    public void ScanSignatures_MatchAfterMaxAddress_IsNotReadOrReturned()
    {
        byte[] data = new byte[4096];
        data[100] = 0xAB;
        data[101] = 0xCD;
        data[3000] = 0xAB;
        data[3001] = 0xCD;

        using var reader = CreateReader(data);

        var results = reader.ScanSignatures("AB CD", 10, maxAddress: 200);

        results.Should().Equal(100L);
        reader.Position.Should().BeLessThanOrEqualTo(202);
    }

    [Fact]
    public void ScanSignatures_MatchStartingAtMaxAddress_ReturnsMatchPosition()
    {
        byte[] data = new byte[64];
        data[40] = 0xAB;
        data[41] = 0xCD;

        using var reader = CreateReader(data);

        reader.ScanSignatures("AB CD", 10, maxAddress: 40).Should().Equal(40L);
    }

    [Fact]
    public void ScanSignatures_MoreMatchesThanMaxCount_ReturnsMaxCountMatches()
    {
        byte[] data = new byte[20];

        for (int i = 0; i < data.Length; i += 2)
            data[i] = 0xAB;

        using var reader = CreateReader(data);

        var results = reader.ScanSignatures("AB", 3, data.Length);

        results.Count.Should().Be(3);
    }

    [Fact]
    public void ScanSignatures_NonSeekableStreamWithXorAnalyzer_ReturnsDecryptedMatches()
    {
        byte[] data = new byte[300_000];
        data[10] = 0xAB;
        data[299_990] = 0xAB;
        using var reader = CreateReader(new NonSeekableMemoryStream([.. data.Select(b => (byte)(b ^ 0x5A))]), analyzer: new XorAnalyzer(0x5A));

        reader.ScanSignatures("AB 00", 10, long.MaxValue).Should().Equal(10L, 299_990L);
    }

    [Fact]
    public void ScanSignatures_OddLengthPattern_ThrowsFormatException()
    {
        using var reader = CreateReader([1, 2, 3]);

        FluentActions.Invoking(() => reader.ScanSignatures("AAB", 10, 100)).Should().ThrowExactly<FormatException>();
    }

    [Theory]
    [InlineData("??")]
    [InlineData("?? ??")]
    [InlineData("????????")]
    public void ScanSignatures_OnlyWildcards_ThrowsFormatException(string pattern)
    {
        using var reader = CreateReader([1, 2, 3]);

        FluentActions.Invoking(() => reader.ScanSignatures(pattern, 10, 100)).Should().ThrowExactly<FormatException>()
            .WithMessage("Pattern must contain at least one known byte!");
    }

    [Fact]
    public void ScanSignatures_PatternLongerThanScanBuffer_ReturnsMatchPositions()
    {
        // Known first and last bytes 999,999 bytes apart, more than the scan buffer leaves for new data after the carried-over bytes.
        const int patternLength = 1_000_000;
        string pattern = "AA" + string.Concat(Enumerable.Repeat("??", patternLength - 2)) + "BB";
        byte[] data = new byte[3 * 1024 * 1024];

        foreach (int position in new[] { 100, 1_500_000 })
        {
            data[position] = 0xAA;
            data[position + patternLength - 1] = 0xBB;
        }

        using var reader = CreateReader(data);

        reader.ScanSignatures(pattern, 10, long.MaxValue).Should().Equal(100L, 1_500_000L);
    }

    [Theory]
    [InlineData("?? 44 ?? 44", new long[] { 1 })]
    [InlineData("?? ?? 44", new long[] { 0 })]
    [InlineData("44 ??", new long[] { 2, 4 })]
    [InlineData("?? 22", new long[] { 0, 4 })]
    public void ScanSignatures_PatternWithWildcardsAtEdges_ReturnsMatchPositionsInsideData(string pattern, long[] expected)
    {
        byte[] data = [0x11, 0x22, 0x44, 0x11, 0x44, 0x22];
        using var reader = CreateReader(data);

        reader.ScanSignatures(pattern, 10, data.Length).Should().Equal(expected).And.Equal(ReferenceScan(data, pattern, 0, data.Length));
    }

    [Theory]
    [InlineData("48 8B 05 ?? ?? ?? ?? 59", 0, 4096)]
    [InlineData("48 8B 05 ?? ?? ?? ?? 59", 100, 300)]
    [InlineData("48 ?? 05", 1_000_000, 2_500_000)]
    [InlineData("59", 0, long.MaxValue)]
    [InlineData("48 ?? ?? 1D 2C 3B 4A 59", 0, long.MaxValue)]
    [InlineData("?? 8B ?? 1D 2C ?? ?? 59", 3, long.MaxValue)]
    [InlineData("?? ?? 05 1D ??", 0, 2_000_000)]
    [InlineData("05 ?? 2C ??", 0, long.MaxValue)]
    public void ScanSignatures_RandomDataWithSignatures_MatchesReferenceScan(string pattern, long startPosition, long maxAddress)
    {
        byte[] data = RandomBytes(3 * 1024 * 1024);
        byte[] signature = [0x48, 0x8B, 0x05, 0x1D, 0x2C, 0x3B, 0x4A, 0x59];

        for (int position = 7; position + signature.Length <= data.Length; position += 65_537)
            signature.CopyTo(data, position);

        using var reader = CreateReader(data);
        reader.Position = startPosition;

        var results = reader.ScanSignatures(pattern, int.MaxValue, maxAddress);

        results.Should().Equal(ReferenceScan(data, pattern, startPosition, maxAddress));
        reader.Position.Should().BeLessThanOrEqualTo(maxAddress >= data.Length ? data.Length : maxAddress + (pattern.Replace(" ", "").Length / 2));
    }

    [Fact]
    public void ScanSignatures_RegionView_ReturnsPositionsInsideRegion()
    {
        byte[] data = new byte[10_000];
        data[100] = 0xAB;
        data[5000] = 0xAB;
        data[9000] = 0xAB;
        using var reader = CreateReader(data);
        reader.EnableRegionView(new RegionRange { StartPosition = 1000, Size = 5000 });

        reader.ScanSignatures("AB", 10, long.MaxValue).Should().Equal(4000L);
        reader.Position.Should().Be(5000);
    }

    [Theory]
    [InlineData("AA AA", 0)]
    [InlineData("AA AA", 1)]
    [InlineData("AA ?? AA", 0)]
    [InlineData("AA AA AA AA AA", 3)]
    public void ScanSignatures_SelfOverlappingPatternAcrossRefills_ReturnsNonOverlappingMatches(string pattern, long startPosition)
    {
        // Every byte matches, so a match ends right at or crosses every refill of the scan buffer.
        byte[] data = [.. Enumerable.Repeat((byte)0xAA, (3 * 1024 * 1024) + 7)];
        int patternLength = pattern.Replace(" ", "").Length / 2;
        using var reader = CreateReader(data);
        reader.Position = startPosition;

        var results = reader.ScanSignatures(pattern, int.MaxValue, long.MaxValue);

        var expected = new List<long>();

        for (long position = startPosition; position + patternLength <= data.Length; position += patternLength)
            expected.Add(position);

        results.Should().Equal(expected);
    }

    [Fact]
    public void ScanSignatures_WildcardPattern_ReturnsMatchPosition()
    {
        byte[] data = [0x11, 0x44, 0x99, 0x44, 0x77, 0x44, 0x44, 0x22];
        using var reader = CreateReader(data);

        var results = reader.ScanSignatures("44 ?? 44 44", 10, data.Length);

        results.Should().Equal(3L);
    }

    [Fact]
    public void ScanSignatures_WithRecordingAnalyzer_NotifiesEveryReadByteOnce()
    {
        byte[] data = RandomBytes(700_000);
        var analyzer = new RecordingAnalyzer();
        using var reader = CreateReader(data, analyzer: analyzer);

        reader.ScanSignatures("12 34 56", int.MaxValue, long.MaxValue);

        analyzer.Bytes.Should().Equal(data);
    }
}