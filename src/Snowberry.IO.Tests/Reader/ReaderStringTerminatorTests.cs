using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Checks that <c>ReadCString</c>, <c>ReadSizedCString</c> and <c>ReadLine</c> find their terminators only on code unit boundaries of
/// UTF-16 and UTF-32 in either byte order, by comparing them with a byte-by-byte reference search over random data.
/// </summary>
public class ReaderStringTerminatorTests
{
    private const int c_Trials = 150;

    public static TheoryData<string, AnalyzerKind> WideEncodingsAndAnalyzers()
    {
        var data = new TheoryData<string, AnalyzerKind>();

        foreach (string name in new[] { "utf-16", "utf-16BE", "utf-32", "utf-32BE" })
        {
            data.Add(name, AnalyzerKind.None);
            data.Add(name, AnalyzerKind.Modifying);
            data.Add(name, AnalyzerKind.NonModifying);
        }

        return data;
    }

    private static CallCountingAnalyzer? CreateAnalyzer(AnalyzerKind kind)
    {
        return kind == AnalyzerKind.None ? null : new CallCountingAnalyzer(kind == AnalyzerKind.Modifying);
    }

    /// <summary>
    /// Creates random code units without a null, CR or LF code unit, but with many zero, CR and LF bytes inside and across code units.
    /// </summary>
    private static byte[] RandomContent(Random random, int unitSize, Encoding encoding, int unitCount)
    {
        byte[] nullUnit = encoding.GetBytes("\0");
        byte[] crUnit = encoding.GetBytes("\r");
        byte[] lfUnit = encoding.GetBytes("\n");
        byte[] data = new byte[unitCount * unitSize];

        for (int i = 0; i < data.Length; i++)
        {
            int kind = random.Next(8);
            data[i] = kind switch
            {
                < 4 => 0,
                4 => 0x0D,
                5 => 0x0A,
                _ => (byte)random.Next(256)
            };
        }

        for (int i = 0; i < data.Length; i += unitSize)
        {
            var unit = data.AsSpan(i, unitSize);

            // Turns a terminator unit into a different unit by setting a bit that every terminator unit has clear.
            while (unit.SequenceEqual(nullUnit) || unit.SequenceEqual(crUnit) || unit.SequenceEqual(lfUnit))
                unit[random.Next(unitSize)] |= 0x40;
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WideEncodingsAndAnalyzers))]
    public void ReadCString_RandomWideCodeUnits_StopsAtFirstAlignedNullUnit(string encodingName, AnalyzerKind analyzerKind)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unitSize = encoding.GetByteCount("\0");
        var random = new Random(1234);

        for (int trial = 0; trial < c_Trials; trial++)
        {
            byte[] content = RandomContent(random, unitSize, encoding, random.Next(0, 700));
            byte[] data = [.. content, .. encoding.GetBytes("\0"), .. RandomBytes(random.Next(0, 9), trial)];
            var analyzer = CreateAnalyzer(analyzerKind);
            using var reader = CreateReader(data, encoding, analyzer);

            reader.ReadCString().Should().Be(encoding.GetString(content), "trial {0}", trial);
            reader.Position.Should().Be(content.Length + unitSize, "trial {0}", trial);
            analyzer?.Bytes.Should().Equal(data.Take(content.Length + unitSize), "trial {0}", trial);
        }
    }

    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    public void ReadCString_ZeroBytesAcrossCodeUnits_AreNotATerminator(string encodingName)
    {
        // Every one of these encodings has a run of zero bytes as long as a code unit that straddles two code units of this text.
        const string text = "AĀA";
        var encoding = Encoding.GetEncoding(encodingName);
        using var reader = CreateReader(encoding.GetBytes(text + "\0rest"), encoding);

        reader.ReadCString().Should().Be(text);
        reader.Position.Should().Be(encoding.GetByteCount(text + "\0"));
    }

    [Theory]
    [MemberData(nameof(WideEncodingsAndAnalyzers))]
    public void ReadLine_RandomWideCodeUnits_StopsAtFirstAlignedLineBreak(string encodingName, AnalyzerKind analyzerKind)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unitSize = encoding.GetByteCount("\n");
        string[] lineBreaks = ["\n", "\r", "\r\n"];
        var random = new Random(5678);

        for (int trial = 0; trial < c_Trials; trial++)
        {
            byte[] content = RandomContent(random, unitSize, encoding, random.Next(0, 700));
            string lineBreak = lineBreaks[random.Next(lineBreaks.Length)];
            byte[] data = [.. content, .. encoding.GetBytes(lineBreak), .. encoding.GetBytes("tail")];
            int lineEnd = content.Length + encoding.GetByteCount(lineBreak);
            var analyzer = CreateAnalyzer(analyzerKind);
            using var reader = CreateReader(data, encoding, analyzer);

            reader.ReadLine().Should().Be(encoding.GetString(content), "trial {0}", trial);
            reader.Position.Should().Be(lineEnd, "trial {0}", trial);

            // After a lone CR, the analyzer also sees the code unit that is examined for an LF but not consumed.
            analyzer?.Bytes.Take(lineEnd).Should().Equal(data.Take(lineEnd), "trial {0}", trial);
        }
    }

    [Theory]
    [MemberData(nameof(WideEncodingsAndAnalyzers))]
    public void ReadSizedCString_RandomWideCodeUnits_StopsAtFirstAlignedNullUnit(string encodingName, AnalyzerKind analyzerKind)
    {
        var encoding = Encoding.GetEncoding(encodingName);
        int unitSize = encoding.GetByteCount("\0");
        var random = new Random(4321);

        for (int trial = 0; trial < c_Trials; trial++)
        {
            byte[] content = RandomContent(random, unitSize, encoding, random.Next(0, 700));
            byte[] data = [.. content, .. encoding.GetBytes("\0"), .. RandomBytes(random.Next(0, 9), trial)];
            var analyzer = CreateAnalyzer(analyzerKind);
            using var reader = CreateReader(data, encoding, analyzer);

            reader.ReadSizedCString(data.Length, adjustPosition: false).Should().Be(encoding.GetString(content), "trial {0}", trial);
            reader.Position.Should().Be(content.Length + unitSize, "trial {0}", trial);
            analyzer?.Bytes.Should().Equal(data.Take(content.Length + unitSize), "trial {0}", trial);
        }
    }

    /// <summary>
    /// The analyzers that the reads are checked with.
    /// </summary>
    public enum AnalyzerKind
    {
        /// <summary>
        /// No analyzer.
        /// </summary>
        None,

        /// <summary>
        /// An analyzer that may modify bytes, so the reads report one code unit at a time.
        /// </summary>
        Modifying,

        /// <summary>
        /// An analyzer that does not modify bytes, so the reads report the consumed bytes after the search.
        /// </summary>
        NonModifying
    }
}