using System.Buffers;
using System.Text;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Reader.Streams;
using Snowberry.IO.Utils;

namespace Snowberry.IO.Reader;

/// <summary>
/// A <see cref="BinaryReader"/> over a stream that also reads different endian types, region views and null-terminated strings.
/// </summary>
/// <remarks>
/// The stream is disposed with the reader unless <see cref="KeepStreamOpen"/> is <see langword="true"/>.
/// </remarks>
public class EndianStreamReader : BaseEndianReader
{
    private readonly Stream? _stream;

    /// <summary>
    /// Creates a new reader instance that decodes text as UTF-8 without a byte order mark.
    /// </summary>
    /// <param name="stream">The source stream, or <see langword="null"/> to create a reader without data.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The size of <see cref="BaseEndianReader.Buffer"/>; raised to <see cref="BaseEndianReader.c_MinBufferSize"/> if smaller.</param>
    public EndianStreamReader(Stream? stream, Analyzer? analyzer = null, int bufferSize = 0) : this(stream, analyzer, bufferSize, EncodingDefaults.s_Utf8)
    {
    }

    /// <summary>
    /// Creates a new reader instance.
    /// </summary>
    /// <param name="stream">The source stream, or <see langword="null"/> to create a reader without data.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The size of <see cref="BaseEndianReader.Buffer"/>; raised to <see cref="BaseEndianReader.c_MinBufferSize"/> if smaller.</param>
    /// <param name="encoding">The encoding used by every character and string read.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="encoding"/> is <see langword="null"/>.</exception>
    public EndianStreamReader(Stream? stream, Analyzer? analyzer, int bufferSize, Encoding encoding) : base(new WrappedReaderStream(stream), analyzer, bufferSize, encoding)
    {
        _stream = stream;
        analyzer?.Initialize(this);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="Stream"/> is <see langword="null"/>.</exception>
    public override void CopyTo(Stream destination)
    {
        ThrowIfDisposed();

        _ = destination ?? throw new ArgumentNullException(nameof(destination));

        if (Stream is null)
            throw new InvalidOperationException($"The {nameof(Stream)} is null.");

        long remaining = Length - Position;

        if (remaining <= 0)
            return;

#if !NETSTANDARD2_0
        // A MemoryStream writes its remaining bytes straight from its buffer. It is the only stream for which this is guaranteed to
        // copy the same bytes as the loop below, because its length is exact and only changes when it is written to.
        if (_direct is { } direct && direct.GetType() == typeof(MemoryStream))
        {
            direct.CopyTo(destination, 0x14000);
            return;
        }
#endif

        CopyToCore(destination, remaining, 0x14000);
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is negative, or when <paramref name="bufferSize"/> is not greater than zero.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="Stream"/> is <see langword="null"/>.</exception>
    public override void CopyTo(Stream destination, int length, int bufferSize = 0x14000)
    {
        ThrowIfDisposed();

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be greater than zero.");

        _ = destination ?? throw new ArgumentNullException(nameof(destination));

        if (Stream is null)
            throw new InvalidOperationException($"The {nameof(Stream)} is null.");

        long remaining = Length - Position;
        long toCopy = Math.Min(length, remaining);

        if (toCopy <= 0)
            return;

        CopyToCore(destination, toCopy, bufferSize);
    }

    /// <summary>
    /// Releases the resources of the reader and disposes <see cref="Stream"/> unless <see cref="KeepStreamOpen"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (!Disposed && disposing && !KeepStreamOpen)
            _stream?.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>
    /// Copies <paramref name="length"/> bytes to <paramref name="destination"/>, routed through <see cref="BinaryReader.Read(byte[], int, int)"/>
    /// so that an active region view and the <see cref="BaseEndianReader.Analyzer"/> are honored.
    /// </summary>
    /// <param name="destination">The stream to copy to.</param>
    /// <param name="length">The number of bytes to copy. Assumed to already be clamped to the available data.</param>
    /// <param name="bufferSize">The size, in bytes, of the buffer used to stage the copy.</param>
    private void CopyToCore(Stream destination, long length, int bufferSize)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            while (length > 0)
            {
                int toRead = (int)Math.Min(bufferSize, length);
                int read = Read(buffer, 0, toRead);

                if (read == 0)
                    break;

                destination.Write(buffer, 0, read);
                length -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Gets a read-only stream over the reader's view of <see cref="Stream"/>.
    /// </summary>
    /// <remarks>
    /// This is the stream that the inherited <see cref="BinaryReader"/> reads use. It shares <see cref="BaseEndianReader.Position"/>,
    /// <see cref="BaseEndianReader.Length"/>, the region view and the <see cref="BaseEndianReader.Analyzer"/> with this reader, so
    /// seeking or reading it moves the reader and stays inside the region view. It is seekable when <see cref="Stream"/> is.
    /// Use <see cref="Stream"/> for the stream passed to the constructor.
    /// </remarks>
    public override Stream BaseStream => base.BaseStream;

    /// <inheritdoc/>
    public override bool CanReadData
    {
        get
        {
            if (Stream == null || Disposed)
                return false;

            // NOTE(VNC): A non-seekable stream without an active region view cannot report its Length
            // (and some non-seekable streams cannot report Position either), so there is no reliable way
            // to compute "remaining bytes" up front. Optimistically assume data may still be available;
            // the actual read call naturally returns 0 once the stream is truly exhausted.
            if (!IsRegionViewEnabled && !Stream.CanSeek)
                return true;

            return Length > Position;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Stream"/> is left open when the reader is disposed.
    /// </summary>
    public virtual bool KeepStreamOpen { get; set; }

    /// <summary>
    /// Gets the stream passed to the constructor, or <see langword="null"/> when the reader was created without one.
    /// </summary>
    public virtual Stream? Stream => _stream;
}