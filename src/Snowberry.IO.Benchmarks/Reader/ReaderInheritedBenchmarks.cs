using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Benchmarks.Reader;

/// <summary>
/// Measures the parameterless reads that <see cref="EndianStreamReader"/> inherits from <see cref="BinaryReader"/>, such as
/// <see cref="BinaryReader.ReadInt32"/>, against <see cref="BinaryReader"/> over the same stream and over a <see cref="PassThroughStream"/>.
/// </summary>
/// <remarks>
/// The inherited reads go through the stream that applies the region view and the analyzer, so <see cref="BinaryReader"/> over a
/// <see cref="MemoryStream"/>, which it reads through a private fast path, is a lower bound that the reader cannot reach; the
/// <see cref="PassThroughStream"/> variant shows what reading through any other stream costs.
/// </remarks>
[BenchmarkCategory("Inherited")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ReaderInheritedBenchmarks
{
    private const int c_PrimitiveSize = c_ValueCount * sizeof(long);
    private const int c_ValueCount = 4096;
    private BinaryReader _binaryReader = null!;

    private string? _directory;
    private BinaryReader _passThroughReader = null!;
    private EndianStreamReader _reader = null!;
    private Stream _stream = Stream.Null;

    private static byte[] CreateData()
    {
        byte[] primitives = BenchmarkData.RandomBytes(c_PrimitiveSize);

        // Clearing the highest exponent bit of every little-endian 32-bit word makes each one a finite float, so the sums can be compared.
        for (int i = 3; i < primitives.Length; i += sizeof(float))
            primitives[i] &= 0xBF;

        using var stream = new MemoryStream();
        stream.Write(primitives, 0, primitives.Length);

        // Values of every encoded length from 1 to 5 bytes.
        var random = new Random(42);

        for (int i = 0; i < c_ValueCount; i++)
        {
            uint value = (uint)(random.Next() >> random.Next(31));

            while (value > 0x7F)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        return stream.ToArray();
    }

    private static long SumByte(BinaryReader reader)
    {
        reader.BaseStream.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += reader.ReadByte();

        return sum;
    }

    private static long SumEncodedInt(BinaryReader reader)
    {
        var encodedReader = (EncodedIntBinaryReader)reader;
        encodedReader.BaseStream.Position = c_PrimitiveSize;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += encodedReader.ReadEncodedInt();

        return sum;
    }

    private static long SumInt32(BinaryReader reader)
    {
        reader.BaseStream.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += reader.ReadInt32();

        return sum;
    }

    private static long SumInt64(BinaryReader reader)
    {
        reader.BaseStream.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += reader.ReadInt64();

        return sum;
    }

    private static long SumSingle(BinaryReader reader)
    {
        reader.BaseStream.Position = 0;
        double sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += reader.ReadSingle();

        return BitConverter.DoubleToInt64Bits(sum);
    }

    private static void Verify(string method, long expected, long passThrough, long actual)
    {
        if (passThrough != expected || actual != expected)
            throw new InvalidOperationException($"{method} did not read the same values as {nameof(BinaryReader)}.");
    }

    /// <summary>
    /// Reads 7-bit encoded integers of every encoded length with the <see cref="BinaryReader"/> implementation of
    /// <c>Read7BitEncodedInt</c>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("7BitEncodedInt")]
    public long BinaryReader7BitEncodedInt()
    {
        return SumEncodedInt(_binaryReader);
    }

    /// <summary>
    /// Reads bytes with <see cref="BinaryReader.ReadByte"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Byte")]
    public long BinaryReaderByte()
    {
        return SumByte(_binaryReader);
    }

    /// <summary>
    /// Reads 32-bit integers with <see cref="BinaryReader.ReadInt32"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long BinaryReaderInt32()
    {
        return SumInt32(_binaryReader);
    }

    /// <summary>
    /// Reads 64-bit integers with <see cref="BinaryReader.ReadInt64"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long BinaryReaderInt64()
    {
        return SumInt64(_binaryReader);
    }

    /// <summary>
    /// Reads single-precision floating-point numbers with <see cref="BinaryReader.ReadSingle"/>.
    /// </summary>
    /// <returns>The bits of the sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Single")]
    public long BinaryReaderSingle()
    {
        return SumSingle(_binaryReader);
    }

    /// <summary>
    /// Closes the readers and the stream and deletes the temporary file.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _passThroughReader.Dispose();
        _binaryReader.Dispose();
        _reader.Dispose();
        _stream.Dispose();
        BenchmarkData.DeleteDirectory(_directory);
    }

    /// <summary>
    /// Reads 7-bit encoded integers of every encoded length with the <see cref="BinaryReader"/> implementation of
    /// <c>Read7BitEncodedInt</c> over a <see cref="PassThroughStream"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("7BitEncodedInt")]
    public long PassThrough7BitEncodedInt()
    {
        return SumEncodedInt(_passThroughReader);
    }

    /// <summary>
    /// Reads bytes with <see cref="BinaryReader.ReadByte"/> over a <see cref="PassThroughStream"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Byte")]
    public long PassThroughByte()
    {
        return SumByte(_passThroughReader);
    }

    /// <summary>
    /// Reads 32-bit integers with <see cref="BinaryReader.ReadInt32"/> over a <see cref="PassThroughStream"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long PassThroughInt32()
    {
        return SumInt32(_passThroughReader);
    }

    /// <summary>
    /// Reads 64-bit integers with <see cref="BinaryReader.ReadInt64"/> over a <see cref="PassThroughStream"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long PassThroughInt64()
    {
        return SumInt64(_passThroughReader);
    }

    /// <summary>
    /// Reads single-precision floating-point numbers with <see cref="BinaryReader.ReadSingle"/> over a <see cref="PassThroughStream"/>.
    /// </summary>
    /// <returns>The bits of the sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Single")]
    public long PassThroughSingle()
    {
        return SumSingle(_passThroughReader);
    }

    /// <summary>
    /// Reads 7-bit encoded integers of every encoded length with <see cref="EndianStreamReader"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("7BitEncodedInt")]
    public long Read7BitEncodedInt()
    {
        _reader.Position = c_PrimitiveSize;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.Read7BitEncodedInt();

        return sum;
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
    /// Reads 32-bit integers with the <see cref="BinaryReader.ReadInt32"/> that <see cref="EndianStreamReader"/> inherits.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long ReadInt32()
    {
        _reader.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt32();

        return sum;
    }

    /// <summary>
    /// Reads 64-bit integers with the <see cref="BinaryReader.ReadInt64"/> that <see cref="EndianStreamReader"/> inherits.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    public long ReadInt64()
    {
        _reader.Position = 0;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt64();

        return sum;
    }

    /// <summary>
    /// Reads single-precision floating-point numbers with the <see cref="BinaryReader.ReadSingle"/> that <see cref="EndianStreamReader"/> inherits.
    /// </summary>
    /// <returns>The bits of the sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Single")]
    public long ReadSingle()
    {
        _reader.Position = 0;
        double sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadSingle();

        return BitConverter.DoubleToInt64Bits(sum);
    }

    /// <summary>
    /// Creates the source data, opens the stream and the readers over it and checks that every reader reads the same values.
    /// </summary>
    /// <remarks>
    /// The data holds <see cref="c_PrimitiveSize"/> random bytes that form finite floats, followed by <see cref="c_ValueCount"/> 7-bit
    /// encoded integers.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The readers do not read the same values.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkStreams.CreateDirectory(Source, nameof(ReaderInheritedBenchmarks));
        _stream = BenchmarkStreams.OpenRead(Source, CreateData(), _directory, "data.bin");
        _reader = new EndianStreamReader(_stream);
        _binaryReader = new EncodedIntBinaryReader(_stream);
        _passThroughReader = new EncodedIntBinaryReader(new PassThroughStream(_stream));

        Verify(nameof(ReadInt32), BinaryReaderInt32(), PassThroughInt32(), ReadInt32());
        Verify(nameof(ReadInt64), BinaryReaderInt64(), PassThroughInt64(), ReadInt64());
        Verify(nameof(ReadSingle), BinaryReaderSingle(), PassThroughSingle(), ReadSingle());
        Verify(nameof(ReadByte), BinaryReaderByte(), PassThroughByte(), ReadByte());
        Verify(nameof(Read7BitEncodedInt), BinaryReader7BitEncodedInt(), PassThrough7BitEncodedInt(), Read7BitEncodedInt());
    }

    /// <summary>
    /// Gets or sets the kind of stream that is read.
    /// </summary>
    [Params(StreamKind.Memory, StreamKind.File, StreamKind.BufferedFile)]
    public StreamKind Source { get; set; }

    /// <summary>
    /// A <see cref="BinaryReader"/> that makes its <c>Read7BitEncodedInt</c> callable on every target framework; it is protected before .NET 5.
    /// </summary>
    /// <param name="input">The stream to read from; it is left open when the reader is disposed.</param>
    private sealed class EncodedIntBinaryReader(Stream input) : BinaryReader(input, Encoding.UTF8, leaveOpen: true)
    {
        /// <summary>
        /// Reads a 7-bit encoded 32-bit integer with the <see cref="BinaryReader"/> implementation.
        /// </summary>
        /// <returns>The read value.</returns>
        public int ReadEncodedInt()
        {
            return Read7BitEncodedInt();
        }
    }
}