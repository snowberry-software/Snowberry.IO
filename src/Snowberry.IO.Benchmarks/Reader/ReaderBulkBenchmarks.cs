using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Benchmarks.Reader;

/// <summary>
/// Measures reading 1 MiB at once with <see cref="EndianStreamReader"/> against reading it from the stream directly.
/// </summary>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ReaderBulkBenchmarks
{
    private const int c_CopyBufferSize = 0x14000;
    private const int c_DataSize = 1024 * 1024;

    private readonly byte[] _buffer = new byte[c_DataSize];
    private readonly MemoryStream _destination = new(c_DataSize);
    private BinaryReader _binaryReader = null!;

    private string? _directory;
    private EndianStreamReader _reader = null!;
    private Stream _stream = Stream.Null;

    /// <summary>
    /// Reads the remaining data into a new array with <see cref="BinaryReader.ReadBytes(int)"/>.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ReadUntilEnd")]
    public long BinaryReaderReadBytes()
    {
        _stream.Position = 0;
        return _binaryReader.ReadBytes((int)_stream.Length).Length;
    }

    /// <summary>
    /// Closes the readers and the streams and deletes the temporary file.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _binaryReader.Dispose();
        _reader.Dispose();
        _stream.Dispose();
        _destination.Dispose();
        BenchmarkData.DeleteDirectory(_directory);
    }

    /// <summary>
    /// Copies the data to a <see cref="MemoryStream"/> with <see cref="EndianStreamReader.CopyTo(Stream, int, int)"/>.
    /// </summary>
    /// <returns>The number of bytes copied.</returns>
    [Benchmark]
    [BenchmarkCategory("CopyTo")]
    public long CopyTo()
    {
        _reader.Position = 0;
        _destination.Position = 0;
        _reader.CopyTo(_destination, c_DataSize, c_CopyBufferSize);
        return _destination.Position;
    }

    /// <summary>
    /// Fills a buffer with the data with <see cref="BaseEndianReader.ReadExactly(byte[], int, int)"/>.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    [Benchmark]
    [BenchmarkCategory("Read")]
    public long ReadExactlyArray()
    {
        _reader.Position = 0;
        _reader.ReadExactly(_buffer, 0, c_DataSize);
        return _reader.Position;
    }

    /// <summary>
    /// Fills a buffer with the data with <c>ReadExactly(Span&lt;byte&gt;)</c>.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    [Benchmark]
    [BenchmarkCategory("Read")]
    public long ReadExactlySpan()
    {
        _reader.Position = 0;
        _reader.ReadExactly(_buffer.AsSpan());
        return _reader.Position;
    }

    /// <summary>
    /// Reads the data into a buffer with a single <c>Read(Span&lt;byte&gt;)</c> call.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    [Benchmark]
    [BenchmarkCategory("Read")]
    public long ReadSpan()
    {
        _reader.Position = 0;
        return _reader.Read(_buffer.AsSpan());
    }

    /// <summary>
    /// Reads the remaining data into a new array with <see cref="BaseEndianReader.ReadUntilEnd(int)"/>.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    [Benchmark]
    [BenchmarkCategory("ReadUntilEnd")]
    public long ReadUntilEnd()
    {
        _reader.Position = 0;
        return _reader.ReadUntilEnd().Length;
    }

    /// <summary>
    /// Creates the source data, opens the stream and the readers over it and checks that every method reads all of it.
    /// </summary>
    /// <exception cref="InvalidOperationException">A method does not read the whole data.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkStreams.CreateDirectory(Source, nameof(ReaderBulkBenchmarks));
        _stream = BenchmarkStreams.OpenRead(Source, BenchmarkData.RandomBytes(c_DataSize), _directory, "data.bin");
        _reader = new EndianStreamReader(_stream);
        _binaryReader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);

        if (BinaryReaderReadBytes() != c_DataSize || ReadUntilEnd() != c_DataSize
            || StreamCopyTo() != c_DataSize || CopyTo() != c_DataSize
            || StreamRead() != c_DataSize || ReadSpan() != c_DataSize || ReadExactlySpan() != c_DataSize || ReadExactlyArray() != c_DataSize)
        {
            throw new InvalidOperationException($"A method did not read the whole data ({Source}).");
        }
    }

    /// <summary>
    /// Copies the data to a <see cref="MemoryStream"/> with <see cref="Stream.CopyTo(Stream, int)"/>.
    /// </summary>
    /// <returns>The number of bytes copied.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("CopyTo")]
    public long StreamCopyTo()
    {
        _stream.Position = 0;
        _destination.Position = 0;
        _stream.CopyTo(_destination, c_CopyBufferSize);
        return _destination.Position;
    }

    /// <summary>
    /// Fills a buffer with the data with <see cref="Stream.Read(byte[], int, int)"/> calls on the stream.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Read")]
    public long StreamRead()
    {
        _stream.Position = 0;
        return BenchmarkStreams.ReadFully(_stream, _buffer, c_DataSize);
    }

    /// <summary>
    /// Gets or sets the kind of stream that is read.
    /// </summary>
    [Params(StreamKind.Memory, StreamKind.File, StreamKind.BufferedFile)]
    public StreamKind Source { get; set; }
}