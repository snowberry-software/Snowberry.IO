using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Common;
using Snowberry.IO.Extensions;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Benchmarks.Reader;

/// <summary>
/// Measures the <c>Read*At</c> methods of <see cref="ReaderExtensions"/>, which convert values from the internal buffer of a reader, against
/// <see cref="BinaryPrimitives"/> over the same bytes.
/// </summary>
/// <remarks>The buffer is filled once with <see cref="BaseEndianReader.ReadInInternalBuffer(int, int)"/> before the measurements.</remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ReaderAtBenchmarks
{
    private const int c_BufferSize = 16 * 1024;
    private const int c_GuidSize = 16;
    private const int c_ValueCount = 1024;

    private byte[] _data = [];
    private EndianStreamReader _reader = null!;

    /// <summary>
    /// Reads <see cref="Guid"/> values from the bytes with <see cref="BenchmarkData.ReadGuid"/>.
    /// </summary>
    /// <returns>The sum of the hash codes of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    public long BinaryPrimitivesGuid()
    {
        ReadOnlySpan<byte> data = _data;
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += BenchmarkData.ReadGuid(data.Slice(i * c_GuidSize, c_GuidSize), endian).GetHashCode();

        return sum;
    }

    /// <summary>
    /// Reads 32-bit integers from the bytes with <see cref="BinaryPrimitives"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long BinaryPrimitivesInt32()
    {
        ReadOnlySpan<byte> data = _data;
        bool little = Endian == EndianType.LITTLE;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            var value = data[(i * sizeof(int))..];
            sum += little ? BinaryPrimitives.ReadInt32LittleEndian(value) : BinaryPrimitives.ReadInt32BigEndian(value);
        }

        return sum;
    }

    /// <summary>
    /// Reads 64-bit integers from the bytes with <see cref="BinaryPrimitives"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long BinaryPrimitivesInt64()
    {
        ReadOnlySpan<byte> data = _data;
        bool little = Endian == EndianType.LITTLE;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            var value = data[(i * sizeof(long))..];
            sum += little ? BinaryPrimitives.ReadInt64LittleEndian(value) : BinaryPrimitives.ReadInt64BigEndian(value);
        }

        return sum;
    }

    /// <summary>
    /// Closes the reader.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _reader.Dispose();
    }

    /// <summary>
    /// Reads <see cref="Guid"/> values from the internal buffer with <see cref="ReaderExtensions.ReadGuidAt"/>.
    /// </summary>
    /// <returns>The sum of the hash codes of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    public long ReadGuidAt()
    {
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadGuidAt(endian, i * c_GuidSize).GetHashCode();

        return sum;
    }

    /// <summary>
    /// Reads 32-bit integers from the internal buffer with <see cref="ReaderExtensions.ReadInt32At"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long ReadInt32At()
    {
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt32At(endian, i * sizeof(int));

        return sum;
    }

    /// <summary>
    /// Reads 64-bit integers from the internal buffer with <see cref="ReaderExtensions.ReadInt64At"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long ReadInt64At()
    {
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt64At(endian, i * sizeof(long));

        return sum;
    }

    /// <summary>
    /// Fills the internal buffer of a reader with random bytes and checks that both methods read the same values.
    /// </summary>
    /// <exception cref="InvalidOperationException">The methods do not read the same values.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _data = BenchmarkData.RandomBytes(c_BufferSize);
        _reader = new EndianStreamReader(new MemoryStream(_data, writable: false), null, c_BufferSize);

        if (_reader.ReadInInternalBuffer(c_BufferSize, 0) != c_BufferSize)
            throw new InvalidOperationException("The internal buffer could not be filled.");

        if (ReadInt32At() != BinaryPrimitivesInt32() || ReadInt64At() != BinaryPrimitivesInt64() || ReadGuidAt() != BinaryPrimitivesGuid())
            throw new InvalidOperationException("The Read*At methods did not read the same values as BinaryPrimitives.");
    }

    /// <summary>
    /// Gets or sets the byte order of the read values.
    /// </summary>
    [Params(EndianType.LITTLE, EndianType.BIG)]
    public EndianType Endian { get; set; }
}