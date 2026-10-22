using System.IO.Compression;
using Snowberry.IO.SingleFile.Sources;
using Snowberry.IO.SingleFile.Streams;

namespace Snowberry.IO.SingleFile.Content;

/// <summary>
/// The content of a bundle entry: a byte range of a source, stored either as is or deflate-compressed.
/// </summary>
/// <param name="source">The source that holds the stored bytes.</param>
/// <param name="offset">The offset of the stored bytes in <paramref name="source"/>.</param>
/// <param name="size">The uncompressed size of the content.</param>
/// <param name="compressedSize">The number of stored bytes when they are compressed, or <c>0</c> when they are stored as is.</param>
/// <param name="entryName">The entry path reported by exceptions about invalid data.</param>
/// <param name="ownsSource">Whether disposing the data disposes <paramref name="source"/>.</param>
internal sealed class EntryData(BundleSource source, long offset, long size, long compressedSize, string entryName, bool ownsSource) : IDisposable, IAsyncDisposable
{
    private const int c_CompressedBufferSize = 64 * 1024;
    private const string c_ContentObjectName = "entry content";

    /// <summary>
    /// Creates data that reads a file, which stays open until the data is disposed.
    /// </summary>
    /// <param name="sourceFileName">The path of the file to read.</param>
    /// <param name="entryName">The entry path reported by exceptions about invalid data.</param>
    /// <returns>The data for the entry.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourceFileName"/> is <see langword="null"/> or empty.</exception>
    public static EntryData FromFile(string sourceFileName, string entryName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceFileName);

        var fileSource = FileBundleSource.Open(sourceFileName, useAsync: false);
        return new EntryData(fileSource, 0, fileSource.Length, 0, entryName, ownsSource: true);
    }

    /// <summary>
    /// Creates data that references <paramref name="content"/>.
    /// </summary>
    /// <param name="content">The bytes of the entry.</param>
    /// <param name="entryName">The entry path reported by exceptions about invalid data.</param>
    /// <returns>The data for the entry.</returns>
    public static EntryData FromMemory(ReadOnlyMemory<byte> content, string entryName)
    {
        return new EntryData(new MemoryBundleSource(content, c_ContentObjectName), 0, content.Length, 0, entryName, ownsSource: true);
    }

    /// <summary>
    /// Creates data that reads <paramref name="content"/> from its current position to its end; a non-seekable stream is copied into memory.
    /// </summary>
    /// <param name="content">The stream to read.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="content"/> open when the data is disposed; <see langword="false"/> to dispose it.</param>
    /// <param name="entryName">The entry path reported by exceptions about invalid data.</param>
    /// <returns>The data for the entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="content"/> is not readable.</exception>
    public static EntryData FromStream(Stream content, bool leaveOpen, string entryName)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (!content.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(content));

        if (!content.CanSeek)
        {
            var buffer = new MemoryStream();
            content.CopyTo(buffer);

            if (!leaveOpen)
                content.Dispose();

            return FromMemory(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), entryName);
        }

        long start = content.Position;
        var streamSource = new StreamBundleSource(content, leaveOpen, c_ContentObjectName);
        return new EntryData(streamSource, start, streamSource.Length - start, 0, entryName, ownsSource: true);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (ownsSource)
            source.Dispose();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return ownsSource ? source.DisposeAsync() : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Opens a read-only stream over the uncompressed content.
    /// </summary>
    /// <remarks>Reading throws when the content ends before its declared size: <see cref="InvalidDataException"/> for bundle data and
    /// <see cref="IOException"/> for content added by the caller, which changed after it was added.</remarks>
    /// <returns>A stream over the uncompressed content.</returns>
    public Stream Open()
    {
        if (!IsCompressed)
            return new RangeReadStream(source, offset, size, CreateTruncatedException);

        // DeflateStream reads in small chunks; buffering turns them into fewer, larger positional reads.
        var compressed = new RangeReadStream(source, offset, compressedSize, bufferSize: (int)Math.Min(compressedSize, c_CompressedBufferSize));
        return new ExactLengthStream(new DeflateStream(compressed, CompressionMode.Decompress), size, entryName);
    }

    /// <summary>
    /// Opens a stream over the bytes exactly as they are stored.
    /// </summary>
    /// <returns>A stream over the stored bytes.</returns>
    public Stream OpenStored()
    {
        return new RangeReadStream(source, offset, StoredLength);
    }

    private Exception CreateTruncatedException(long position)
    {
        // Only content added by the caller owns its source; the bundle's own data shares the bundle source.
        return ownsSource
            ? new IOException($"The content of bundle entry '{entryName}' changed length from {size} bytes; it ended after {position} bytes.")
            : new InvalidDataException($"The bundle entry '{entryName}' ended after {position} bytes, but declares {size} bytes.");
    }

    /// <summary>
    /// Gets the number of stored bytes when they are compressed, or <c>0</c> when they are stored as is.
    /// </summary>
    public long CompressedSize => compressedSize;

    /// <summary>
    /// Gets or sets the level and the resulting length of a compression whose output was discarded because it did not beat the compression
    /// threshold, or <see langword="null"/> if the content was not compressed that way.
    /// </summary>
    public (CompressionLevel Level, long Length)? DiscardedCompression { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 hash of the uncompressed content, once it has been computed.
    /// </summary>
    public byte[]? Hash { get; set; }

    /// <summary>
    /// Gets a value indicating whether the stored bytes are deflate-compressed.
    /// </summary>
    public bool IsCompressed => compressedSize > 0;

    /// <summary>
    /// Gets the offset of the stored bytes in the source.
    /// </summary>
    public long Offset => offset;

    /// <summary>
    /// Gets the uncompressed length of the content in bytes.
    /// </summary>
    public long Size => size;

    /// <summary>
    /// Gets the source that holds the stored bytes.
    /// </summary>
    public BundleSource Source => source;

    /// <summary>
    /// Gets the number of bytes the content occupies in its source.
    /// </summary>
    public long StoredLength => IsCompressed ? compressedSize : size;
}