using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Benchmarks.Reader;

/// <summary>
/// Measures primitive reads of <see cref="EndianStreamReader"/> over a <see cref="MemoryStream"/> with an <see cref="Analyzer"/> and with a
/// region view, against the same reads without either and against <see cref="BinaryReader"/>.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ReaderModeBenchmarks
{
    private const int c_GuidSize = 16;
    private const int c_ValueCount = 4096;

    private readonly byte[] _guidBuffer = new byte[c_GuidSize];
    private BinaryReader _binaryReader = null!;
    private EndianStreamReader _reader = null!;

    private Stream _stream = Stream.Null;

    /// <summary>
    /// Reads bytes with <see cref="BinaryReader.ReadByte"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Byte")]
    public long BinaryReaderByte()
    {
        _stream.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _binaryReader.ReadByte();

        return sum;
    }

    /// <summary>
    /// Reads <see cref="Guid"/> values with <see cref="BinaryReader.Read(byte[], int, int)"/> into a reused buffer.
    /// </summary>
    /// <returns>The sum of the hash codes of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    public long BinaryReaderGuid()
    {
        _stream.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            _ = _binaryReader.Read(_guidBuffer, 0, c_GuidSize);
            sum += BenchmarkData.ReadGuid(_guidBuffer, EndianType.LITTLE).GetHashCode();
        }

        return sum;
    }

    /// <summary>
    /// Reads 32-bit integers with <see cref="BinaryReader.ReadInt32"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long BinaryReaderInt32()
    {
        _stream.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _binaryReader.ReadInt32();

        return sum;
    }

    /// <summary>
    /// Reads 64-bit integers with <see cref="BinaryReader.ReadInt64"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long BinaryReaderInt64()
    {
        _stream.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _binaryReader.ReadInt64();

        return sum;
    }

    /// <summary>
    /// Closes the readers and the stream.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _binaryReader.Dispose();
        _reader.Dispose();
        _stream.Dispose();
    }

    /// <summary>
    /// Reads bytes with the <see cref="BinaryReader.ReadByte"/> that <see cref="EndianStreamReader"/> inherits.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Byte")]
    public long ReadByte()
    {
        _reader.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadByte();

        return sum;
    }

    /// <summary>
    /// Reads <see cref="Guid"/> values with <see cref="BaseEndianReader.ReadGuid(EndianType)"/> in little-endian byte order.
    /// </summary>
    /// <returns>The sum of the hash codes of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    public long ReadGuid()
    {
        _reader.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadGuid(EndianType.LITTLE).GetHashCode();

        return sum;
    }

    /// <summary>
    /// Reads 32-bit integers with <see cref="BaseEndianReader.ReadInt32(EndianType)"/> in little-endian byte order.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long ReadInt32()
    {
        _reader.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt32(EndianType.LITTLE);

        return sum;
    }

    /// <summary>
    /// Reads 32-bit integers with the <see cref="BinaryReader.ReadInt32"/> that <see cref="EndianStreamReader"/> inherits.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long ReadInt32Inherited()
    {
        _reader.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt32();

        return sum;
    }

    /// <summary>
    /// Reads 64-bit integers with <see cref="BaseEndianReader.ReadInt64(EndianType)"/> in little-endian byte order.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long ReadInt64()
    {
        _reader.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt64(EndianType.LITTLE);

        return sum;
    }

    /// <summary>
    /// Reads 64-bit integers with the <see cref="BinaryReader.ReadInt64"/> that <see cref="EndianStreamReader"/> inherits.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long ReadInt64Inherited()
    {
        _reader.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt64();

        return sum;
    }

    /// <summary>
    /// Creates the source data, opens the stream and both readers over it and checks that both readers read the same values.
    /// </summary>
    /// <exception cref="InvalidOperationException">The readers do not read the same values.</exception>
    [GlobalSetup]
    public void Setup()
    {
        byte[] data = BenchmarkData.RandomBytes(c_ValueCount * c_GuidSize);
        _stream = new MemoryStream(data, writable: false);
        _reader = new EndianStreamReader(_stream, Mode == ReaderMode.Analyzer ? new NoOpAnalyzer() : null);
        _binaryReader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);

        if (Mode == ReaderMode.RegionView)
            _reader.EnableRegionView(new RegionRange { StartPosition = 0, Size = data.Length });

        long int32 = BinaryReaderInt32();
        long int64 = BinaryReaderInt64();

        if (ReadInt32() != int32 || ReadInt32Inherited() != int32 || ReadInt64() != int64 || ReadInt64Inherited() != int64
            || ReadGuid() != BinaryReaderGuid() || ReadByte() != BinaryReaderByte())
        {
            throw new InvalidOperationException($"The reader did not read the same values as {nameof(BinaryReader)} ({Mode}).");
        }
    }

    /// <summary>
    /// Gets or sets the features that are enabled on the reader.
    /// </summary>
    [Params(ReaderMode.Plain, ReaderMode.Analyzer, ReaderMode.RegionView)]
    public ReaderMode Mode { get; set; }

    /// <summary>
    /// The features that are enabled on the reader.
    /// </summary>
    public enum ReaderMode
    {
        /// <summary>
        /// Neither an analyzer nor a region view.
        /// </summary>
        Plain,

        /// <summary>
        /// A <see cref="NoOpAnalyzer"/> that every read is reported to.
        /// </summary>
        Analyzer,

        /// <summary>
        /// A region view over the whole data, so the reads are clamped to the region but never cut short.
        /// </summary>
        RegionView
    }
}