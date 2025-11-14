using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains tests for analyzer functionality and advanced scenarios.
/// </summary>
public class AnalyzerTests
{
    [Fact]
    public void Analyzer_TracksBytesRead()
    {
        var analyzer = new TestAnalyzer();
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        using var reader = new EndianStreamReader(stream, analyzer);

        reader.ReadInt32();
        Assert.Equal(4, analyzer.TotalBytesRead);

        reader.ReadInt32();
        Assert.Equal(8, analyzer.TotalBytesRead);
    }

    [Fact]
    public void Analyzer_CanModifyBytes()
    {
        var analyzer = new XorAnalyzer(0xFF);
        var stream = new MemoryStream();

        using (var writer = new EndianStreamWriter(stream, true))
        {
            writer.Write(100);
        }

        stream.Position = 0;

        // XOR the bytes
        byte[] data = stream.ToArray();
        for (int i = 0; i < data.Length; i++)
        {
            data[i] ^= 0xFF;
        }

        stream = new MemoryStream(data);

        using var reader = new EndianStreamReader(stream, analyzer);
        int value = reader.ReadInt32();

        Assert.Equal(100, value);
    }

    [Fact]
    public void Analyzer_PropertyGetterSetter()
    {
        var analyzer1 = new TestAnalyzer();
        var stream = new MemoryStream(new byte[10]);

        using var reader = new EndianStreamReader(stream, analyzer1);

        Assert.Equal(analyzer1, reader.Analyzer);

        var analyzer2 = new TestAnalyzer();
        reader.Analyzer = analyzer2;

        Assert.Equal(analyzer2, reader.Analyzer);
    }

    [Fact]
    public void Analyzer_Null_NoError()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        using var reader = new EndianStreamReader(stream, null);

        reader.ReadInt32(); // Should not throw
        Assert.Null(reader.Analyzer);
    }

    [Fact]
    public void Analyzer_Initialize_CalledOnConstruction()
    {
        var analyzer = new TestAnalyzer();
        var stream = new MemoryStream();

        using var reader = new EndianStreamReader(stream, analyzer);

        Assert.True(analyzer.Initialized);
    }

    [Fact]
    public void Analyzer_MultipleReads_AllTracked()
    {
        var analyzer = new TestAnalyzer();
        var stream = new MemoryStream(new byte[100]);

        using var reader = new EndianStreamReader(stream, analyzer);

        reader.ReadByte();
        reader.ReadInt16();
        reader.ReadInt32();
        reader.ReadInt64();

        int expectedBytes = 1 + 2 + 4 + 8;
        Assert.Equal(expectedBytes, analyzer.TotalBytesRead);
    }

    [Fact]
    public void Analyzer_ReadBytes_Tracked()
    {
        var analyzer = new TestAnalyzer();
        var stream = new MemoryStream(new byte[100]);

        using var reader = new EndianStreamReader(stream, analyzer);

        reader.ReadBytes(50);
        Assert.Equal(50, analyzer.TotalBytesRead);
    }

    [Fact]
    public void Analyzer_ReadUntilEnd_Tracked()
    {
        var analyzer = new TestAnalyzer();
        var stream = new MemoryStream(new byte[100]);

        using var reader = new EndianStreamReader(stream, analyzer);

        reader.ReadUntilEnd();
        Assert.Equal(100, analyzer.TotalBytesRead);
    }

    [Fact]
    public void Analyzer_CopyTo_Tracked()
    {
        var analyzer = new TestAnalyzer();
        var stream = new MemoryStream(new byte[100]);
        var destination = new MemoryStream();

        using var reader = new EndianStreamReader(stream, analyzer);

        reader.CopyTo(destination, 50);
        Assert.Equal(50, analyzer.TotalBytesRead);
    }

    [Fact]
    public void Analyzer_CanDecrypt_SimpleXor()
    {
        const byte xorKey = 0xAA;
        var analyzer = new XorAnalyzer(xorKey);

        // Write encrypted data
        var stream = new MemoryStream();
        using (var writer = new EndianStreamWriter(stream, true))
        {
            int value = 12345;
            byte[] bytes = BitConverter.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] ^= xorKey;
            }

            writer.Write(bytes);
        }

        stream.Position = 0;

        // Read with decryption analyzer
        using var reader = new EndianStreamReader(stream, analyzer);
        int decrypted = reader.ReadInt32();

        Assert.Equal(12345, decrypted);
    }

    [Fact]
    public void Analyzer_RegionView_StillTracked()
    {
        var analyzer = new TestAnalyzer();
        var stream = new MemoryStream(new byte[100]);

        using var reader = new EndianStreamReader(stream, analyzer);

        reader.EnableRegionView(20..80);
        reader.ReadBytes(30);

        Assert.Equal(30, analyzer.TotalBytesRead);
    }

    [Fact]
    public void Analyzer_SetToNull_RemovesTracking()
    {
        var analyzer = new TestAnalyzer();
        var stream = new MemoryStream(new byte[100]);

        using var reader = new EndianStreamReader(stream, analyzer);

        reader.ReadInt32();
        Assert.Equal(4, analyzer.TotalBytesRead);

        reader.Analyzer = null;
        reader.ReadInt32();

        // Still 4 because analyzer was removed
        Assert.Equal(4, analyzer.TotalBytesRead);
    }

    [Fact]
    public void Analyzer_SwapAnalyzer_NewOneTracks()
    {
        var analyzer1 = new TestAnalyzer();
        var analyzer2 = new TestAnalyzer();
        var stream = new MemoryStream(new byte[100]);

        using var reader = new EndianStreamReader(stream, analyzer1);

        reader.ReadInt32();
        Assert.Equal(4, analyzer1.TotalBytesRead);
        Assert.Equal(0, analyzer2.TotalBytesRead);

        reader.Analyzer = analyzer2;
        reader.ReadInt32();

        Assert.Equal(4, analyzer1.TotalBytesRead);
        Assert.Equal(4, analyzer2.TotalBytesRead);
    }

    // Test analyzer implementation
    private class TestAnalyzer : Analyzer
    {
        public int TotalBytesRead { get; private set; }
        public bool Initialized { get; private set; }

        public override void Initialize(IEndianReader reader)
        {
            Initialized = true;
        }

        public override void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int count)
        {
            TotalBytesRead += count;
        }
    }

    // XOR decryption analyzer
    private class XorAnalyzer : Analyzer
    {
        private readonly byte _key;

        public XorAnalyzer(byte key)
        {
            _key = key;
        }

        public override void Initialize(IEndianReader reader)
        {
            // No initialization needed
        }

        public override void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[i] ^= _key;
            }
        }
    }
}
