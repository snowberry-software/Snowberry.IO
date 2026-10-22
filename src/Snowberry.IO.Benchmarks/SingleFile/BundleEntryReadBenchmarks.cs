using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Microsoft.Win32.SafeHandles;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// Measures reading the content of a <see cref="BundleEntry"/> of an opened bundle file with <see cref="BundleEntry.ReadAllBytes"/> and
/// <see cref="BundleEntry.Open"/>, against reading the stored bytes with <see cref="RandomAccess"/> and inflating them with
/// <see cref="DeflateStream"/>.
/// </summary>
public class BundleEntryReadBenchmarks
{
    private const int c_ReadBufferSize = 4096;

    private readonly byte[] _readBuffer = new byte[c_ReadBufferSize];
    private SingleFileBundle _bundle = null!;
    private byte[] _content = [];

    private string? _directory;
    private BundleEntry _entry = null!;
    private SafeFileHandle _file = null!;
    private byte[] _stored = [];

    /// <summary>
    /// Closes the bundle and the file and deletes the temporary file.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _file.Dispose();
        _bundle.Dispose();
        BenchmarkData.DeleteDirectory(_directory);
    }

    /// <summary>
    /// Copies the stream of <see cref="BundleEntry.Open"/> with <see cref="Stream.CopyTo(Stream)"/> to a stream that discards the data.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    [Benchmark]
    public long OpenCopyTo()
    {
        using var stream = _entry.Open();
        var destination = new CountingStream();
        stream.CopyTo(destination);
        return destination.Length;
    }

    /// <summary>
    /// Reads the stream of <see cref="BundleEntry.Open"/> in chunks of 4 KiB.
    /// </summary>
    /// <returns>The number of bytes read.</returns>
    [Benchmark]
    public long OpenRead()
    {
        using var stream = _entry.Open();
        long total = 0;
        int read;

        while ((read = stream.Read(_readBuffer, 0, c_ReadBufferSize)) > 0)
            total += read;

        return total;
    }

    /// <summary>
    /// Reads the stored bytes with <see cref="RandomAccess.Read(SafeFileHandle, Span{byte}, long)"/> and, for a compressed entry, inflates
    /// them from memory with <see cref="DeflateStream"/>, into a new array.
    /// </summary>
    /// <returns>The content of the entry.</returns>
    [Benchmark(Baseline = true)]
    public byte[] RandomAccessRead()
    {
        byte[] content = new byte[_entry.Size];
        byte[] stored = Compressed ? _stored : content;
        int total = 0;

        while (total < stored.Length)
        {
            int read = RandomAccess.Read(_file, stored.AsSpan(total), _entry.Offset + total);

            if (read == 0)
                break;

            total += read;
        }

        if (Compressed)
        {
            using var deflate = new DeflateStream(new MemoryStream(stored, writable: false), CompressionMode.Decompress);
            _ = BenchmarkStreams.ReadFully(deflate, content, content.Length);
        }

        return content;
    }

    /// <summary>
    /// Reads the content with <see cref="BundleEntry.ReadAllBytes"/>.
    /// </summary>
    /// <returns>The content of the entry.</returns>
    [Benchmark]
    public byte[] ReadAllBytes()
    {
        return _entry.ReadAllBytes();
    }

    /// <summary>
    /// Writes a bundle with one assembly to a temporary file, opens it and checks that every method reads the content of the assembly.
    /// </summary>
    /// <exception cref="InvalidOperationException">A method does not read the content of the assembly.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkData.CreateTempDirectory(nameof(BundleEntryReadBenchmarks));
        string path = Path.Combine(_directory, SyntheticBundle.c_FileName);
        SyntheticBundle.Write(path, 1, EntrySize, Compressed);
        _content = SyntheticBundle.CreateContent(EntrySize, seed: 0);

        _bundle = SingleFileBundle.Open(path);
        _entry = _bundle.GetEntry(SyntheticBundle.GetAssemblyPath(0));
        _stored = new byte[Compressed ? _entry.CompressedSize : _entry.Size];
        _file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);

        if (_entry.IsCompressed != Compressed)
            throw new InvalidOperationException("The synthetic bundle does not have the expected compressed entry.");

        if (!RandomAccessRead().AsSpan().SequenceEqual(_content) || !ReadAllBytes().AsSpan().SequenceEqual(_content)
            || OpenCopyTo() != EntrySize || OpenRead() != EntrySize)
        {
            throw new InvalidOperationException($"A method did not read the content of the entry ({EntrySize} bytes, compressed: {Compressed}).");
        }
    }

    /// <summary>
    /// Gets or sets whether the read entry is stored compressed.
    /// </summary>
    [Params(false, true)]
    public bool Compressed { get; set; }

    /// <summary>
    /// Gets or sets the uncompressed size of the read entry, in bytes.
    /// </summary>
    [Params(64 * 1024, 4 * 1024 * 1024)]
    public int EntrySize { get; set; }

    /// <summary>
    /// A write-only stream that discards the data and counts the bytes written to it.
    /// </summary>
    private sealed class CountingStream : Stream
    {
        private long _length;

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            _length += count;
        }

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _length += buffer.Length;
        }

        /// <inheritdoc/>
        public override bool CanRead => false;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Length => _length;

        /// <inheritdoc/>
        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }
    }
}