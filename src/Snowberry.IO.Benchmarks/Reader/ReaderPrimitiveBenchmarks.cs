using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Common;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Benchmarks.Reader;

/// <summary>
/// Measures reading primitive values with <see cref="EndianStreamReader"/> against <see cref="BinaryReader"/>, which only reads
/// little-endian values and is followed by a byte swap for <see cref="EndianType.BIG"/>.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ReaderPrimitiveBenchmarks
{
    private const int c_GuidSize = 16;
    private const int c_ValueCount = 4096;

    private readonly byte[] _guidBuffer = new byte[c_GuidSize];
    private BinaryReader _binaryReader = null!;

    private string? _directory;
    private EndianStreamReader _reader = null!;
    private Stream _stream = Stream.Null;

    /// <summary>
    /// Reads <see cref="Guid"/> values with <see cref="BinaryReader.Read(byte[], int, int)"/> into a reused buffer.
    /// </summary>
    /// <returns>The sum of the hash codes of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    public long BinaryReaderGuid()
    {
        _stream.Position = 0;
        var endian = Endian;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            _ = _binaryReader.Read(_guidBuffer, 0, c_GuidSize);
            sum += BenchmarkData.ReadGuid(_guidBuffer, endian).GetHashCode();
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
        bool swap = Endian == EndianType.BIG;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            int value = _binaryReader.ReadInt32();
            sum += swap ? BinaryPrimitives.ReverseEndianness(value) : value;
        }

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
        bool swap = Endian == EndianType.BIG;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            long value = _binaryReader.ReadInt64();
            sum += swap ? BinaryPrimitives.ReverseEndianness(value) : value;
        }

        return sum;
    }

    /// <summary>
    /// Reads single-precision floating-point numbers with <see cref="BinaryReader.ReadSingle"/>, or with
    /// <see cref="BinaryReader.ReadInt32"/> and a byte swap for <see cref="EndianType.BIG"/>.
    /// </summary>
    /// <returns>The bits of the sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Single")]
    public long BinaryReaderSingle()
    {
        _stream.Position = 0;
        bool swap = Endian == EndianType.BIG;
        double sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            if (swap)
            {
                int bits = BinaryPrimitives.ReverseEndianness(_binaryReader.ReadInt32());
                sum += Unsafe.As<int, float>(ref bits);
            }
            else
            {
                sum += _binaryReader.ReadSingle();
            }
        }

        return BitConverter.DoubleToInt64Bits(sum);
    }

    /// <summary>
    /// Closes the readers and the stream and deletes the temporary file.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _binaryReader.Dispose();
        _reader.Dispose();
        _stream.Dispose();
        BenchmarkData.DeleteDirectory(_directory);
    }

    /// <summary>
    /// Reads <see cref="Guid"/> values with <see cref="EndianStreamReader"/>.
    /// </summary>
    /// <returns>The sum of the hash codes of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
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
    /// Reads 32-bit integers with <see cref="EndianStreamReader"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
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
    /// Reads 64-bit integers with <see cref="EndianStreamReader"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
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
    /// Reads single-precision floating-point numbers with <see cref="EndianStreamReader"/>.
    /// </summary>
    /// <returns>The bits of the sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Single")]
    public long ReadSingle()
    {
        _reader.Position = 0;
        var endian = Endian;
        double sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadSingle(endian);

        return BitConverter.DoubleToInt64Bits(sum);
    }

    /// <summary>
    /// Creates the source data and opens the stream and both readers over it.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkStreams.CreateDirectory(Source, nameof(ReaderPrimitiveBenchmarks));
        _stream = BenchmarkStreams.OpenRead(Source, BenchmarkData.RandomBytes(c_ValueCount * c_GuidSize), _directory, "data.bin");
        _reader = new EndianStreamReader(_stream);
        _binaryReader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>
    /// Gets or sets the byte order of the read values.
    /// </summary>
    [Params(EndianType.LITTLE, EndianType.BIG)]
    public EndianType Endian { get; set; }

    /// <summary>
    /// Gets or sets the kind of stream that is read.
    /// </summary>
    [Params(StreamKind.Memory, StreamKind.File, StreamKind.BufferedFile)]
    public StreamKind Source { get; set; }
}