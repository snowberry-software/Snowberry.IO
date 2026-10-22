using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Writer.Interfaces;
using Snowberry.IO.Writer;

namespace Snowberry.IO.Benchmarks.Writer;

/// <summary>
/// Measures <see cref="EndianStreamWriter"/> against <see cref="BinaryWriter"/>, both writing UTF-8 into the same stream.
/// </summary>
/// <remarks>
/// The 7-bit encoded 64-bit integers are written through <see cref="IEndianWriter"/>, because <see cref="EndianStreamWriter"/> only
/// implements that method explicitly. .NET Framework has no <c>BinaryWriter.Write7BitEncodedInt64</c>, so its baseline there is the loop of
/// <see cref="BinaryWriter.Write(byte)"/> calls that .NET uses.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class WriterBenchmarks
{
    private const byte c_Alignment = 16;
    private const int c_SizedCStringSize = 32;
    private const string c_SizedCStringText = "Snowberry.IO.Sized20";
    private const int c_ValueCount = 1024;
    private static readonly byte[] s_Zeros = new byte[c_Alignment];
    private static readonly char[] s_NewLine = Environment.NewLine.ToCharArray();


    private readonly Encoding _encoding = new UTF8Encoding(false);
    private readonly byte[] _guidBytes = new byte[16];
    private readonly string _longText = BenchmarkData.RandomText(4096, seed: 4096);
    private readonly string _shortText = BenchmarkData.RandomText(16, seed: 16);
    private readonly byte[] _sizedCStringBuffer = new byte[c_SizedCStringSize];

    private readonly string _text = BenchmarkData.RandomText(64);
    private BinaryWriter _binaryWriter = null!;

    private string? _directory;
    private EncodedIntBinaryWriter _encodedIntWriter = null!;
    private IEndianWriter _endianWriter = null!;
    private Guid _guid;
    private Sha1 _sha1;
    private byte[] _sha1Bytes = [];
    private Stream _stream = Stream.Null;
    private char[] _textChars = [];
    private int[] _varInts = [];
    private long[] _varLongs = [];
    private EndianStreamWriter _writer = null!;

    /// <summary>
    /// Writes 7-bit encoded integers of every encoded length with the <see cref="BinaryWriter"/> implementation of
    /// <c>Write7BitEncodedInt</c>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("7BitEncodedInt")]
    public long BinaryWriter7BitEncodedInt()
    {
        _stream.Position = 0;
        int[] values = _varInts;

        for (int i = 0; i < values.Length; i++)
            _encodedIntWriter.WriteEncodedInt(values[i]);

        return _stream.Position;
    }

    /// <summary>
    /// Writes 7-bit encoded 64-bit integers of every encoded length with the <see cref="BinaryWriter"/> implementation of
    /// <c>Write7BitEncodedInt64</c>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("7BitEncodedInt64")]
    public long BinaryWriter7BitEncodedInt64()
    {
        _stream.Position = 0;
        long[] values = _varLongs;

        for (int i = 0; i < values.Length; i++)
            _encodedIntWriter.WriteEncodedInt64(values[i]);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a 64-character text and a null byte with <see cref="BinaryWriter.Write(char[])"/> and <see cref="BinaryWriter.Write(byte)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("CString")]
    public long BinaryWriterCString()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            _binaryWriter.Write(_textChars);
            _binaryWriter.Write((byte)0);
        }

        return _stream.Position;
    }

    /// <summary>
    /// Writes a 64-character text with <see cref="BinaryWriter.Write(char[])"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("StringCharacters")]
    public long BinaryWriterChars()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _binaryWriter.Write(_textChars);

        return _stream.Position;
    }

    /// <summary>
    /// Writes double-precision floating-point numbers with <see cref="BinaryWriter.Write(double)"/>, or with
    /// <see cref="BinaryWriter.Write(long)"/> after a byte swap for <see cref="EndianType.BIG"/>.
    /// </summary>
    /// <param name="endian">The byte order to write in.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Double")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long BinaryWriterDouble(EndianType endian)
    {
        _stream.Position = 0;
        bool swap = endian == EndianType.BIG;

        for (int i = 0; i < c_ValueCount; i++)
        {
            double value = i * 0.5;

            if (swap)
                _binaryWriter.Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToInt64Bits(value)));
            else
                _binaryWriter.Write(value);
        }

        return _stream.Position;
    }

    /// <summary>
    /// Writes a <see cref="Guid"/> with <see cref="BinaryWriter.Write(byte[], int, int)"/> after converting it to bytes with the BCL:
    /// <c>Guid.TryWriteBytes</c> on .NET 8 and later, <see cref="Guid.ToByteArray"/> and a byte swap for <see cref="EndianType.BIG"/> before.
    /// </summary>
    /// <param name="endian">The byte order of the first three components.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long BinaryWriterGuid(EndianType endian)
    {
        _stream.Position = 0;
        bool bigEndian = endian == EndianType.BIG;
#if NET8_0_OR_GREATER
        byte[] bytes = _guidBytes;
#endif

        for (int i = 0; i < c_ValueCount; i++)
        {
#if NET8_0_OR_GREATER
            _ = _guid.TryWriteBytes(bytes, bigEndian, out _);
#else
            byte[] bytes = _guid.ToByteArray();

            if (bigEndian)
            {
                (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
                (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
                (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
                (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
            }
#endif
            _binaryWriter.Write(bytes, 0, 16);
        }

        return _stream.Position;
    }

    /// <summary>
    /// Writes 32-bit integers with <see cref="BinaryWriter.Write(int)"/>, swapping the bytes first for <see cref="EndianType.BIG"/>.
    /// </summary>
    /// <param name="endian">The byte order to write in.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long BinaryWriterInt32(EndianType endian)
    {
        _stream.Position = 0;
        bool swap = endian == EndianType.BIG;

        for (int i = 0; i < c_ValueCount; i++)
            _binaryWriter.Write(swap ? BinaryPrimitives.ReverseEndianness(i) : i);

        return _stream.Position;
    }

    /// <summary>
    /// Writes 64-bit integers with <see cref="BinaryWriter.Write(long)"/>, swapping the bytes first for <see cref="EndianType.BIG"/>.
    /// </summary>
    /// <param name="endian">The byte order to write in.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long BinaryWriterInt64(EndianType endian)
    {
        _stream.Position = 0;
        bool swap = endian == EndianType.BIG;

        for (long i = 0; i < c_ValueCount; i++)
            _binaryWriter.Write(swap ? BinaryPrimitives.ReverseEndianness(i) : i);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a 64-character text and <see cref="Environment.NewLine"/> with two <see cref="BinaryWriter.Write(char[])"/> calls.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("WriteLine")]
    public long BinaryWriterLine()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            _binaryWriter.Write(_textChars);
            _binaryWriter.Write(s_NewLine);
        }

        return _stream.Position;
    }

    /// <summary>
    /// Writes a byte followed by zero bytes up to the next 16-byte boundary with <see cref="BinaryWriter.Write(byte[], int, int)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Padding")]
    public long BinaryWriterPadding()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            _binaryWriter.Write((byte)1);
            _binaryWriter.Write(s_Zeros, 0, (int)BinaryUtils.CalculatePadding(_stream.Position, c_Alignment));
        }

        return _stream.Position;
    }

    /// <summary>
    /// Writes the 20 bytes of a <see cref="Sha1"/> hash, converted beforehand, with <see cref="BinaryWriter.Write(byte[], int, int)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Sha1")]
    public long BinaryWriterSha1()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _binaryWriter.Write(_sha1Bytes, 0, Sha1.c_StructSize);

        return _stream.Position;
    }

    /// <summary>
    /// Writes single-precision floating-point numbers with <see cref="BinaryWriter.Write(float)"/>, or with
    /// <see cref="BinaryWriter.Write(int)"/> after a byte swap for <see cref="EndianType.BIG"/>.
    /// </summary>
    /// <param name="endian">The byte order to write in.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Single")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long BinaryWriterSingle(EndianType endian)
    {
        _stream.Position = 0;
        bool swap = endian == EndianType.BIG;

        for (int i = 0; i < c_ValueCount; i++)
        {
            float value = i * 0.5f;

            if (swap)
                _binaryWriter.Write(BinaryPrimitives.ReverseEndianness(Unsafe.As<float, int>(ref value)));
            else
                _binaryWriter.Write(value);
        }

        return _stream.Position;
    }

    /// <summary>
    /// Writes a 20-character ASCII text padded with zero bytes to 32 bytes: encodes it with <see cref="Encoding.GetBytes(string, int, int, byte[], int)"/>
    /// into a reused buffer, clears the rest of the buffer and writes it with <see cref="BinaryWriter.Write(byte[], int, int)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("SizedCString")]
    public long BinaryWriterSizedCString()
    {
        _stream.Position = 0;
        byte[] buffer = _sizedCStringBuffer;

        for (int i = 0; i < c_ValueCount; i++)
        {
            int count = _encoding.GetBytes(c_SizedCStringText, 0, c_SizedCStringText.Length, buffer, 0);
            Array.Clear(buffer, count, c_SizedCStringSize - count);
            _binaryWriter.Write(buffer, 0, c_SizedCStringSize);
        }

        return _stream.Position;
    }

    /// <summary>
    /// Writes a length-prefixed text with <see cref="BinaryWriter.Write(string)"/>.
    /// </summary>
    /// <param name="length">The number of characters of the text: 16, 64 or 4096.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("String")]
    [Arguments(16)]
    [Arguments(64)]
    [Arguments(4096)]
    public long BinaryWriterString(int length)
    {
        _stream.Position = 0;
        string text = GetText(length);

        for (int i = 0; i < c_ValueCount; i++)
            _binaryWriter.Write(text);

        return _stream.Position;
    }

    /// <summary>
    /// Closes the writers and the stream and deletes the temporary file.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _binaryWriter.Dispose();
        _writer.Dispose();
        _stream.Dispose();
        BenchmarkData.DeleteDirectory(_directory);
    }

    /// <summary>
    /// Opens the stream, creates the writers and the written values and checks that every library method writes the same bytes as its
    /// baseline.
    /// </summary>
    /// <exception cref="InvalidOperationException">A library method does not write the same bytes as its baseline.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkStreams.CreateDirectory(Source, nameof(WriterBenchmarks));
        _stream = BenchmarkStreams.OpenWrite(Source, c_ValueCount * 128, _directory, "data.bin");
        _writer = new EndianStreamWriter(_stream, true, _encoding);
        _endianWriter = _writer;
        _encodedIntWriter = new EncodedIntBinaryWriter(_stream, _encoding);
        _binaryWriter = _encodedIntWriter;
        _textChars = _text.ToCharArray();

        byte[] guidBytes = BenchmarkData.RandomBytes(16, seed: 16);
        _guid = new Guid(guidBytes);
        _sha1Bytes = BenchmarkData.RandomBytes(Sha1.c_StructSize, seed: Sha1.c_StructSize);
        _sha1 = new Sha1((ReadOnlySpan<byte>)_sha1Bytes);

        // Values of every encoded length: 1 to 5 bytes for the 32-bit and 1 to 10 bytes for the 64-bit integers.
        var random = new Random(42);
        _varInts = new int[c_ValueCount];
        _varLongs = new long[c_ValueCount];

        for (int i = 0; i < c_ValueCount; i++)
        {
            _varInts[i] = random.Next() >> random.Next(31);

            ulong bits = ((ulong)(uint)random.Next() << 62) ^ ((ulong)(uint)random.Next() << 31) ^ (uint)random.Next();
            _varLongs[i] = (long)(bits >> random.Next(64));
        }

        foreach (var endian in new[] { EndianType.LITTLE, EndianType.BIG })
        {
            Verify("Int32", () => BinaryWriterInt32(endian), () => WriteInt32(endian));
            Verify("Int64", () => BinaryWriterInt64(endian), () => WriteInt64(endian));
            Verify("Single", () => BinaryWriterSingle(endian), () => WriteSingle(endian));
            Verify("Double", () => BinaryWriterDouble(endian), () => WriteDouble(endian));
            Verify("Guid", () => BinaryWriterGuid(endian), () => WriteGuid(endian));
        }

        foreach (int length in new[] { 16, 64, 4096 })
            Verify("String", () => BinaryWriterString(length), () => WriteString(length));

        Verify("Sha1", BinaryWriterSha1, WriteSha1);
        Verify("CString", BinaryWriterCString, WriteCString);
        Verify("SizedCString", BinaryWriterSizedCString, WriteSizedCString);
        Verify("StringCharacters", BinaryWriterChars, WriteStringCharacters);
        Verify("WriteLine", BinaryWriterLine, WriteLine);
        Verify("Padding", BinaryWriterPadding, WritePadding);
        Verify("7BitEncodedInt", BinaryWriter7BitEncodedInt, Write7BitEncodedInt);
        Verify("7BitEncodedInt64", BinaryWriter7BitEncodedInt64, Write7BitEncodedInt64);
    }

    /// <summary>
    /// Writes 7-bit encoded integers of every encoded length with <see cref="EndianStreamWriter.Write7BitEncodedInt(int)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("7BitEncodedInt")]
    public long Write7BitEncodedInt()
    {
        _stream.Position = 0;
        int[] values = _varInts;

        for (int i = 0; i < values.Length; i++)
            _writer.Write7BitEncodedInt(values[i]);

        return _stream.Position;
    }

    /// <summary>
    /// Writes 7-bit encoded 64-bit integers of every encoded length with <see cref="IEndianWriter.Write7BitEncodedInt64(long)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("7BitEncodedInt64")]
    public long Write7BitEncodedInt64()
    {
        _stream.Position = 0;
        long[] values = _varLongs;

        for (int i = 0; i < values.Length; i++)
            _endianWriter.Write7BitEncodedInt64(values[i]);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a 64-character text with <see cref="EndianStreamWriter.WriteCString(string)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("CString")]
    public long WriteCString()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.WriteCString(_text);

        return _stream.Position;
    }

    /// <summary>
    /// Writes double-precision floating-point numbers with <see cref="EndianStreamWriter.Write(double, EndianType)"/>.
    /// </summary>
    /// <param name="endian">The byte order to write in.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Double")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long WriteDouble(EndianType endian)
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.Write(i * 0.5, endian);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a <see cref="Guid"/> with <see cref="EndianStreamWriter.Write(Guid, EndianType)"/>.
    /// </summary>
    /// <param name="endian">The byte order of the first three components.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Guid")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long WriteGuid(EndianType endian)
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.Write(_guid, endian);

        return _stream.Position;
    }

    /// <summary>
    /// Writes 32-bit integers with <see cref="EndianStreamWriter.Write(int, EndianType)"/>.
    /// </summary>
    /// <param name="endian">The byte order to write in.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long WriteInt32(EndianType endian)
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.Write(i, endian);

        return _stream.Position;
    }

    /// <summary>
    /// Writes 64-bit integers with <see cref="EndianStreamWriter.Write(long, EndianType)"/>.
    /// </summary>
    /// <param name="endian">The byte order to write in.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int64")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long WriteInt64(EndianType endian)
    {
        _stream.Position = 0;

        for (long i = 0; i < c_ValueCount; i++)
            _writer.Write(i, endian);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a 64-character text and <see cref="Environment.NewLine"/> with <see cref="EndianStreamWriter.WriteLine(string)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("WriteLine")]
    public long WriteLine()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.WriteLine(_text);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a byte followed by zero bytes up to the next 16-byte boundary with <see cref="EndianStreamWriter.WritePadding(byte)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Padding")]
    public long WritePadding()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            _writer.Write((byte)1);
            _writer.WritePadding(c_Alignment);
        }

        return _stream.Position;
    }

    /// <summary>
    /// Writes a <see cref="Sha1"/> hash with <see cref="EndianStreamWriter.Write(Sha1)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Sha1")]
    public long WriteSha1()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.Write(_sha1);

        return _stream.Position;
    }

    /// <summary>
    /// Writes single-precision floating-point numbers with <see cref="EndianStreamWriter.Write(float, EndianType)"/>.
    /// </summary>
    /// <param name="endian">The byte order to write in.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Single")]
    [Arguments(EndianType.LITTLE)]
    [Arguments(EndianType.BIG)]
    public long WriteSingle(EndianType endian)
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.Write(i * 0.5f, endian);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a 20-character ASCII text padded with zero bytes to 32 bytes with <see cref="EndianStreamWriter.WriteSizedCString(string, int)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("SizedCString")]
    public long WriteSizedCString()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.WriteSizedCString(c_SizedCStringText, c_SizedCStringSize);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a length-prefixed text with the <see cref="BinaryWriter.Write(string)"/> overload of <see cref="EndianStreamWriter"/>.
    /// </summary>
    /// <param name="length">The number of characters of the text: 16, 64 or 4096.</param>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("String")]
    [Arguments(16)]
    [Arguments(64)]
    [Arguments(4096)]
    public long WriteString(int length)
    {
        _stream.Position = 0;
        string text = GetText(length);

        for (int i = 0; i < c_ValueCount; i++)
            _writer.Write(text);

        return _stream.Position;
    }

    /// <summary>
    /// Writes a 64-character text with <see cref="EndianStreamWriter.WriteStringCharacters(string)"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("StringCharacters")]
    public long WriteStringCharacters()
    {
        _stream.Position = 0;

        for (int i = 0; i < c_ValueCount; i++)
            _writer.WriteStringCharacters(_text);

        return _stream.Position;
    }

    private string GetText(int length)
    {
        return length switch
        {
            16 => _shortText,
            64 => _text,
            4096 => _longText,
            _ => throw new ArgumentOutOfRangeException(nameof(length), length, "The text length must be 16, 64 or 4096.")
        };
    }

    private void Verify(string category, Func<long> baseline, Func<long> library)
    {
        byte[] expected = WrittenBytes(baseline);
        byte[] actual = WrittenBytes(library);

        if (!expected.AsSpan().SequenceEqual(actual))
            throw new InvalidOperationException($"The {category} methods of {nameof(EndianStreamWriter)} and {nameof(BinaryWriter)} wrote different bytes ({Source}).");
    }

    private byte[] WrittenBytes(Func<long> write)
    {
        byte[] bytes = new byte[(int)write()];
        _stream.Position = 0;

        if (BenchmarkStreams.ReadFully(_stream, bytes, bytes.Length) != bytes.Length)
            throw new InvalidOperationException($"The written bytes could not be read back ({Source}).");

        return bytes;
    }

    /// <summary>
    /// Gets or sets the kind of stream that is written.
    /// </summary>
    [Params(StreamKind.Memory, StreamKind.File, StreamKind.BufferedFile)]
    public StreamKind Source { get; set; }

    /// <summary>
    /// A <see cref="BinaryWriter"/> that makes its 7-bit encoded integer writes callable on every target framework: <c>Write7BitEncodedInt</c>
    /// is protected before .NET 5, and <c>Write7BitEncodedInt64</c> only exists since .NET 5.
    /// </summary>
    /// <param name="output">The stream to write to; it is left open when the writer is disposed.</param>
    /// <param name="encoding">The encoding of the written text.</param>
    private sealed class EncodedIntBinaryWriter(Stream output, Encoding encoding) : BinaryWriter(output, encoding, leaveOpen: true)
    {
        /// <summary>
        /// Writes a 7-bit encoded 32-bit integer with the <see cref="BinaryWriter"/> implementation.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteEncodedInt(int value)
        {
            Write7BitEncodedInt(value);
        }

        /// <summary>
        /// Writes a 7-bit encoded 64-bit integer with the <see cref="BinaryWriter"/> implementation, or on .NET Framework with the same loop.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public void WriteEncodedInt64(long value)
        {
#if NET5_0_OR_GREATER
            Write7BitEncodedInt64(value);
#else
            ulong remaining = (ulong)value;

            while (remaining > 0x7Fu)
            {
                Write((byte)((uint)remaining | ~0x7Fu));
                remaining >>= 7;
            }

            Write((byte)remaining);
#endif
        }
    }
}