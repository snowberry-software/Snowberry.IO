using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Common;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Benchmarks.Reader;

/// <summary>
/// Measures the <c>TryRead*</c> methods of <see cref="BaseEndianReader"/> against the reads that throw at the end of the data, such as
/// <see cref="BaseEndianReader.ReadInt32(EndianType)"/>, over a <see cref="MemoryStream"/>.
/// </summary>
/// <remarks>
/// The <c>AtEnd</c> benchmarks fail every read: they start two bytes before the end of the data, so each call reads the two bytes, reaches
/// the end and restores the position.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TryReadBenchmarks
{
    private const int c_DataSize = c_ValueCount * c_GuidSize;
    private const int c_GuidSize = 16;
    private const int c_ValueCount = 4096;

    private EndianStreamReader _reader = null!;

    /// <summary>
    /// Closes the reader.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _reader.Dispose();
    }

    /// <summary>
    /// Reads every value with <see cref="BaseEndianReader.ReadGuid(EndianType)"/>.
    /// </summary>
    /// <returns>The sum of the hash codes of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    public long ReadGuid()
    {
        _reader.Position = 0;
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadGuid(endian).GetHashCode();

        return sum;
    }

    /// <summary>
    /// Reads every value with <see cref="BaseEndianReader.ReadInt32(EndianType)"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long ReadInt32()
    {
        _reader.Position = 0;
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt32(endian);

        return sum;
    }

    /// <summary>
    /// Reads every value with <see cref="BaseEndianReader.ReadInt64(EndianType)"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long ReadInt64()
    {
        _reader.Position = 0;
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt64(endian);

        return sum;
    }

    /// <summary>
    /// Opens a reader over the source data and checks that the reads and the <c>TryRead*</c> methods read the same values.
    /// </summary>
    /// <exception cref="InvalidOperationException">A <c>TryRead*</c> method does not read the same values, or succeeds at the end of the data.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _reader = new EndianStreamReader(new MemoryStream(BenchmarkData.RandomBytes(c_DataSize), writable: false));

        if (TryReadInt32() != ReadInt32() || TryReadInt64() != ReadInt64() || TryReadGuid() != ReadGuid())
            throw new InvalidOperationException("The TryRead methods did not read the same values as the reads.");

        if (TryReadInt32AtEnd() != 0 || TryReadInt64AtEnd() != 0 || TryReadGuidAtEnd() != 0)
            throw new InvalidOperationException("A TryRead method succeeded at the end of the data.");
    }

    /// <summary>
    /// Reads every value with <see cref="BaseEndianReader.TryReadGuid(out Guid, EndianType)"/>.
    /// </summary>
    /// <returns>The sum of the hash codes of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    public long TryReadGuid()
    {
        _reader.Position = 0;
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            if (_reader.TryReadGuid(out var value, endian))
                sum += value.GetHashCode();
        }

        return sum;
    }

    /// <summary>
    /// Fails <see cref="BaseEndianReader.TryReadGuid(out Guid, EndianType)"/> two bytes before the end of the data.
    /// </summary>
    /// <returns>The number of successful reads.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    public long TryReadGuidAtEnd()
    {
        _reader.Position = c_DataSize - 2;
        var endian = Endian;
        long count = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            if (_reader.TryReadGuid(out _, endian))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Reads every value with <see cref="BaseEndianReader.TryReadInt32(out int, EndianType)"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long TryReadInt32()
    {
        _reader.Position = 0;
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            if (_reader.TryReadInt32(out int value, endian))
                sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Fails <see cref="BaseEndianReader.TryReadInt32(out int, EndianType)"/> two bytes before the end of the data.
    /// </summary>
    /// <returns>The number of successful reads.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long TryReadInt32AtEnd()
    {
        _reader.Position = c_DataSize - 2;
        var endian = Endian;
        long count = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            if (_reader.TryReadInt32(out _, endian))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Reads every value with <see cref="BaseEndianReader.TryReadInt64(out long, EndianType)"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long TryReadInt64()
    {
        _reader.Position = 0;
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            if (_reader.TryReadInt64(out long value, endian))
                sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Fails <see cref="BaseEndianReader.TryReadInt64(out long, EndianType)"/> two bytes before the end of the data.
    /// </summary>
    /// <returns>The number of successful reads.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long TryReadInt64AtEnd()
    {
        _reader.Position = c_DataSize - 2;
        var endian = Endian;
        long count = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            if (_reader.TryReadInt64(out _, endian))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Gets or sets the byte order of the read values.
    /// </summary>
    [Params(EndianType.LITTLE, EndianType.BIG)]
    public EndianType Endian { get; set; }
}