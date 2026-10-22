using System.Runtime.InteropServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Benchmarks.Reader;

/// <summary>
/// Measures the string reading methods of <see cref="EndianStreamReader"/> against <see cref="BinaryReader.ReadString"/>,
/// <see cref="StreamReader.ReadLine"/> and a lower bound for the null-terminated strings.
/// </summary>
/// <remarks>
/// The lower bound of <see cref="EndianStreamReader.ReadCString"/> and <see cref="EndianStreamReader.ReadSizedCString"/> reads the whole data
/// from the stream at once, searches the terminator with <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T}, T)"/> and decodes the
/// string with <see cref="Encoding.GetString(byte[], int, int)"/>. Every method reads its own stream, so the <see cref="BinaryReader"/>
/// and <see cref="StreamReader"/> buffers never hold data of another method.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ReaderStringBenchmarks
{
    private const int c_StreamReaderBufferSize = 1024;

    private readonly List<IDisposable> _disposables = [];

    private BinaryReader _binaryReader = null!;
    private byte[] _buffer = [];
    private int _cStringLength;
    private EndianStreamReader _cStringReader = null!;
    private Stream _cStringStream = Stream.Null;
    private string? _directory;
    private Encoding _encoding = null!;
    private EndianStreamReader _lineReader = null!;
    private Stream _lineStream = Stream.Null;
    private EndianStreamReader _prefixedReader = null!;
    private int _sizedLength;
    private EndianStreamReader _sizedReader = null!;
    private Stream _sizedStream = Stream.Null;
    private StreamReader _streamReader = null!;

    private string _text = string.Empty;

    /// <summary>
    /// Reads a length-prefixed string with <see cref="BinaryReader.ReadString"/>.
    /// </summary>
    /// <returns>The read string.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ReadString")]
    public string BinaryReaderReadString()
    {
        _binaryReader.BaseStream.Position = 0;
        return _binaryReader.ReadString();
    }

    /// <summary>
    /// Closes the readers and the streams and deletes the temporary files.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        for (int i = _disposables.Count - 1; i >= 0; i--)
            _disposables[i].Dispose();

        _disposables.Clear();
        BenchmarkData.DeleteDirectory(_directory);
    }

    /// <summary>
    /// Reads the null-terminated data at once, searches the terminator and decodes the string before it.
    /// </summary>
    /// <returns>The read string.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ReadCString")]
    public string IndexOfCString()
    {
        _cStringStream.Position = 0;
        int read = BenchmarkStreams.ReadFully(_cStringStream, _buffer, _cStringLength);
        return DecodeUntilTerminator(read);
    }

    /// <summary>
    /// Reads the data of a known byte length at once, searches a terminator and decodes the string before it.
    /// </summary>
    /// <returns>The read string.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ReadSizedCString")]
    public string IndexOfSizedCString()
    {
        _sizedStream.Position = 0;
        int read = BenchmarkStreams.ReadFully(_sizedStream, _buffer, _sizedLength);
        return DecodeUntilTerminator(read);
    }

    /// <summary>
    /// Reads a null-terminated string with <see cref="EndianStreamReader.ReadCString"/>.
    /// </summary>
    /// <returns>The read string.</returns>
    [Benchmark]
    [BenchmarkCategory("ReadCString")]
    public string ReadCString()
    {
        _cStringReader.Position = 0;
        return _cStringReader.ReadCString();
    }

    /// <summary>
    /// Reads a line that ends with a line feed with <see cref="EndianStreamReader.ReadLine"/>.
    /// </summary>
    /// <returns>The read line.</returns>
    [Benchmark]
    [BenchmarkCategory("ReadLine")]
    public string? ReadLine()
    {
        _lineReader.Position = 0;
        return _lineReader.ReadLine();
    }

    /// <summary>
    /// Reads a string of a known byte length with <see cref="EndianStreamReader.ReadSizedCString"/>.
    /// </summary>
    /// <returns>The read string.</returns>
    [Benchmark]
    [BenchmarkCategory("ReadSizedCString")]
    public string ReadSizedCString()
    {
        _sizedReader.Position = 0;
        return _sizedReader.ReadSizedCString(_sizedLength);
    }

    /// <summary>
    /// Reads a length-prefixed string with the <see cref="BinaryReader.ReadString"/> that <see cref="EndianStreamReader"/> inherits.
    /// </summary>
    /// <returns>The read string.</returns>
    [Benchmark]
    [BenchmarkCategory("ReadString")]
    public string ReadString()
    {
        _prefixedReader.Position = 0;
        return _prefixedReader.ReadString();
    }

    /// <summary>
    /// Encodes the text in each layout, opens the readers over it and checks that every method reads the text back.
    /// </summary>
    /// <exception cref="InvalidOperationException">A method does not read the text back unchanged.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _encoding = Encoding == TextEncoding.UTF8 ? new UTF8Encoding(false) : new UnicodeEncoding(false, false);
        _text = BenchmarkData.RandomText(Length);
        _directory = BenchmarkStreams.CreateDirectory(Source, nameof(ReaderStringBenchmarks));

        byte[] prefixed;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, _encoding, leaveOpen: true))
                writer.Write(_text);

            prefixed = stream.ToArray();
        }

        byte[] textBytes = _encoding.GetBytes(_text);
        byte[] cString = [.. textBytes, .. _encoding.GetBytes("\0")];
        byte[] line = [.. textBytes, .. _encoding.GetBytes("\n")];
        _cStringLength = cString.Length;
        _sizedLength = textBytes.Length;
        _buffer = new byte[cString.Length];

        _binaryReader = Track(new BinaryReader(Open(prefixed, "prefixed-bcl.bin"), _encoding));
        _cStringStream = Open(cString, "cstring-bcl.bin");
        _sizedStream = Open(textBytes, "sized-bcl.bin");
        _lineStream = Open(line, "line-bcl.bin");
        _streamReader = Track(new StreamReader(_lineStream, _encoding, detectEncodingFromByteOrderMarks: false, c_StreamReaderBufferSize, leaveOpen: true));

        _prefixedReader = CreateReader(prefixed, "prefixed.bin");
        _cStringReader = CreateReader(cString, "cstring.bin");
        _sizedReader = CreateReader(textBytes, "sized.bin");
        _lineReader = CreateReader(line, "line.bin");

        Verify(nameof(BinaryReaderReadString), BinaryReaderReadString());
        Verify(nameof(ReadString), ReadString());
        Verify(nameof(IndexOfCString), IndexOfCString());
        Verify(nameof(ReadCString), ReadCString());
        Verify(nameof(IndexOfSizedCString), IndexOfSizedCString());
        Verify(nameof(ReadSizedCString), ReadSizedCString());
        Verify(nameof(StreamReaderReadLine), StreamReaderReadLine());
        Verify(nameof(ReadLine), ReadLine());
    }

    /// <summary>
    /// Reads a line that ends with a line feed with <see cref="StreamReader.ReadLine"/>, which does not detect byte order marks.
    /// </summary>
    /// <returns>The read line.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ReadLine")]
    public string? StreamReaderReadLine()
    {
        _lineStream.Position = 0;
        _streamReader.DiscardBufferedData();
        return _streamReader.ReadLine();
    }

    private EndianStreamReader CreateReader(byte[] data, string fileName)
    {
        return Track(new EndianStreamReader(Open(data, fileName), WithAnalyzer ? new NoOpAnalyzer() : null, 0, _encoding));
    }

    private string DecodeUntilTerminator(int count)
    {
        var data = new ReadOnlySpan<byte>(_buffer, 0, count);
        int end;

        if (Encoding == TextEncoding.UTF8)
        {
            end = data.IndexOf((byte)0);
        }
        else
        {
            end = MemoryMarshal.Cast<byte, char>(data).IndexOf('\0');
            end = end < 0 ? -1 : end * sizeof(char);
        }

        return _encoding.GetString(_buffer, 0, end < 0 ? count : end);
    }

    private Stream Open(byte[] data, string fileName)
    {
        return Track(BenchmarkStreams.OpenRead(Source, data, _directory, fileName));
    }

    private T Track<T>(T disposable)
        where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    private void Verify(string method, string? actual)
    {
        if (!string.Equals(actual, _text, StringComparison.Ordinal))
            throw new InvalidOperationException($"{method} did not read the text back unchanged ({Length} characters, {Encoding}, {Source}).");
    }

    /// <summary>
    /// Gets or sets the encoding of the read string.
    /// </summary>
    [Params(TextEncoding.UTF8, TextEncoding.Unicode)]
    public TextEncoding Encoding { get; set; }

    /// <summary>
    /// Gets or sets the number of characters of the read string.
    /// </summary>
    [Params(16, 4096, 65536)]
    public int Length { get; set; }

    /// <summary>
    /// Gets or sets the kind of stream that is read.
    /// </summary>
    [Params(StreamKind.Memory, StreamKind.File)]
    public StreamKind Source { get; set; }

    /// <summary>
    /// Gets or sets whether the <see cref="EndianStreamReader"/> instances report their reads to a <see cref="NoOpAnalyzer"/>.
    /// </summary>
    [Params(false, true)]
    public bool WithAnalyzer { get; set; }

    /// <summary>
    /// The text encodings that strings are read in.
    /// </summary>
    public enum TextEncoding
    {
        /// <summary>
        /// UTF-8, see <see cref="Encoding.UTF8"/>.
        /// </summary>
        UTF8,

        /// <summary>
        /// Little-endian UTF-16, see <see cref="Encoding.Unicode"/>.
        /// </summary>
        Unicode
    }
}