using System.Runtime.CompilerServices;
using System.Text;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Reader.Streams;

namespace Snowberry.IO.Reader;

/// <summary>
/// <see cref="BinaryReader"/> extended with endian-aware reads, region views, an <see cref="Common.Reader.Analyzer"/> and
/// further reads such as null-terminated strings; the base of <see cref="EndianStreamReader"/> and <see cref="Win32ProcessReader"/>.
/// </summary>
/// <remarks>
/// <para>Every read, including the ones inherited from <see cref="BinaryReader"/> such as <see cref="BinaryReader.ReadInt32"/>
/// or <see cref="BinaryReader.ReadString"/>, honors the region view and is reported to <see cref="Analyzer"/>.</para>
/// <para>The parameterless reads such as <see cref="BinaryReader.ReadInt32"/> are little-endian, like in <see cref="BinaryReader"/>;
/// the overloads that take an <see cref="EndianType"/>, such as <see cref="ReadInt32(EndianType)"/>, read the given byte order.</para>
/// <para>A fixed-size read that reaches the end of the data throws <see cref="EndOfStreamException"/> and leaves <see cref="Position"/>
/// after the bytes that were available. Use the <c>TryRead*</c> methods to restore the position instead.</para>
/// <para>This type is not thread-safe.</para>
/// </remarks>
public abstract partial class BaseEndianReader : BinaryReader, IEndianReader
{
    /// <summary>
    /// The smallest size of <see cref="Buffer"/>, which fits the largest value the <c>*At</c> reader extensions read (a <see cref="Sha1"/>).
    /// </summary>
    public const int c_MinBufferSize = 20;

    /// <summary>
    /// Scratch space for fixed-size reads, large enough for the largest one (<see cref="Sha1"/>).
    /// </summary>
    private readonly byte[] _scratch = new byte[Sha1.c_StructSize];

    /// <summary>
    /// The buffer behind <see cref="Buffer"/>.
    /// </summary>
    private byte[] _buffer;

    /// <inheritdoc/>
    public abstract void CopyTo(Stream destination);

    /// <inheritdoc/>
    public abstract void CopyTo(Stream destination, int length, int bufferSize = 0x14000);

    /// <inheritdoc/>
    public void DisableRegionView()
    {
        ThrowIfDisposed();

        _readerStream.DisableRegionView();
    }

    /// <inheritdoc/>
    public void EnableRegionView(Range range)
    {
        ThrowIfDisposed();

        long actualLength = ActualLength;
        long start = range.Start.IsFromEnd ? actualLength - range.Start.Value : range.Start.Value;
        long end = range.End.IsFromEnd ? actualLength - range.End.Value : range.End.Value;

        if (end < start)
            throw new ArgumentOutOfRangeException(nameof(range), "The end of the range must not be before its start.");

        EnableRegionView(new RegionRange { StartPosition = start, Size = end - start });
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="RegionRange.StartPosition"/> or <see cref="RegionRange.Size"/> of <paramref name="region"/> is negative,
    /// or when the region does not fit within <see cref="ActualLength"/>.
    /// </exception>
    public virtual void EnableRegionView(RegionRange region)
    {
        ThrowIfDisposed();

        if (region.StartPosition < 0)
            throw new ArgumentOutOfRangeException(nameof(region), "The start position of the region cannot be negative.");

        if (region.Size < 0)
            throw new ArgumentOutOfRangeException(nameof(region), "The size of the region cannot be negative.");

        if (region.Size > ActualLength - region.StartPosition)
            throw new ArgumentOutOfRangeException(nameof(region), "The region exceeds the actual length of the reader.");

        _readerStream.EnableRegionView(region);
    }

    /// <inheritdoc/>
    public void EnsureBufferSize(int bufferSize)
    {
        ThrowIfDisposed();

        if (bufferSize < 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size cannot be negative.");

        bufferSize = Math.Max(c_MinBufferSize, bufferSize);

        if (_buffer.Length < bufferSize)
            _buffer = new byte[bufferSize];
    }

    /// <summary>
    /// Takes the current <see cref="ReaderStream.DirectSource"/> and <see cref="Analyzer"/> of the reader stream; called by the reader
    /// stream whenever its <see cref="Analyzer"/> or region view changes.
    /// </summary>
    internal void RefreshDirectSource()
    {
        _direct = _readerStream.DirectSource;
        _analyzer = _readerStream.Analyzer;
    }

    /// <summary>
    /// Releases the resources of the reader; the stream that the reader reads from is released by the derived types.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (Disposed)
            return;

        Disposed = true;

        if (disposing)
            _readerStream.Dispose();

        base.Dispose(disposing);
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if <see cref="Disposed"/> is <see langword="true"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when <see cref="Disposed"/> is <see langword="true"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ThrowIfDisposed()
    {
        if (Disposed)
            ThrowObjectDisposedException();
    }

    /// <summary>
    /// Throws the <see cref="ObjectDisposedException"/> of <see cref="ThrowIfDisposed"/>.
    /// </summary>
    /// <remarks>Kept out of <see cref="ThrowIfDisposed"/>, so that the check inlines into every read as a single test.</remarks>
    /// <exception cref="ObjectDisposedException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowObjectDisposedException()
    {
        throw new ObjectDisposedException(GetType().FullName);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public long ActualLength
    {
        get
        {
            ThrowIfDisposed();
            return _readerStream.ActualLength;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public long ActualPosition
    {
        get
        {
            ThrowIfDisposed();
            return _readerStream.ActualPosition;
        }
    }

    /// <inheritdoc/>
    public Analyzer? Analyzer
    {
        get => _analyzer;
        set => _readerStream.Analyzer = value;
    }

    /// <inheritdoc/>
    public Memory<byte> Buffer => _buffer;

    /// <inheritdoc/>
    public virtual bool CanReadData => Length > 0;

    /// <inheritdoc/>
    public bool Disposed { get; private set; }

    /// <inheritdoc/>
    public Encoding Encoding => _encoding;

    /// <inheritdoc/>
    public bool IsRegionViewEnabled => _readerStream.IsRegionViewEnabled;

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public long Length
    {
        get
        {
            ThrowIfDisposed();
            return _readerStream.Length;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative while a region view is enabled.</exception>
    public long Position
    {
        get
        {
            ThrowIfDisposed();
            return _readerStream.Position;
        }

        set
        {
            ThrowIfDisposed();
            _readerStream.Position = value;
        }
    }

    /// <inheritdoc/>
    public RegionRange RegionView => _readerStream.RegionView;

    /// <summary>
    /// The maximum number of bytes read into a stack-allocated buffer at once when decoding characters.
    /// </summary>
    private protected const int c_MaxCharBytesSize = 128;

    /// <summary>
    /// The stream that the <see cref="BinaryReader"/> base reads from; it applies the region view and the analyzer.
    /// </summary>
    private protected readonly ReaderStream _readerStream;

    /// <summary>
    /// The shared decoder used for chunked, stateful character decoding of the Snowberry string reads.
    /// </summary>
    private protected readonly Decoder _decoder;

    /// <summary>
    /// The <see cref="System.Text.Encoding"/> used to decode characters and strings, shared with the <see cref="BinaryReader"/> base.
    /// </summary>
    private protected readonly Encoding _encoding;

    /// <summary>
    /// The maximum number of characters that can be produced by decoding <see cref="c_MaxCharBytesSize"/> bytes with <see cref="_encoding"/>.
    /// </summary>
    private protected readonly int _maxCharsSize;

    /// <summary>
    /// Indicates whether <see cref="_encoding"/> always uses two bytes per character (for example <see cref="UnicodeEncoding"/>).
    /// </summary>
    private protected readonly bool _2BytesPerChar;

    /// <summary>
    /// Indicates whether <see cref="_encoding"/> always uses four bytes per character (for example <see cref="UTF32Encoding"/>).
    /// </summary>
    private protected readonly bool _4bytesPerChar;

    /// <summary>
    /// The <see cref="StringBuilder"/> that the string-reading methods decode characters into.
    /// </summary>
    private protected readonly StringBuilder _stringBuilder = new();

    /// <summary>
    /// The <see cref="ReaderStream.DirectSource"/> of <see cref="_readerStream"/>, which the fixed-size reads use instead of
    /// <see cref="_readerStream"/> while it is not <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A field of the reader rather than of <see cref="_readerStream"/>, because on .NET Framework every <see cref="Stream"/> is a
    /// <see cref="MarshalByRefObject"/>, whose fields cannot be read from another type without a call.
    /// </remarks>
    private protected Stream? _direct;

    /// <summary>
    /// The <see cref="Analyzer"/> of <see cref="_readerStream"/>, kept by the reader for the same reason as <see cref="_direct"/>.
    /// </summary>
    private protected Analyzer? _analyzer;

    /// <summary>
    /// Creates a reader whose <see cref="BinaryReader"/> base reads from <paramref name="stream"/>.
    /// </summary>
    /// <param name="stream">The stream that applies the region view and the analyzer.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The size of <see cref="Buffer"/>; raised to <see cref="c_MinBufferSize"/> if smaller.</param>
    /// <param name="encoding">The encoding used by both the <see cref="BinaryReader"/> base and the Snowberry string reads.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="encoding"/> is <see langword="null"/>.</exception>
    private protected BaseEndianReader(ReaderStream stream, Analyzer? analyzer, int bufferSize, Encoding encoding)
        : base(stream, encoding ?? throw new ArgumentNullException(nameof(encoding)), leaveOpen: true)
    {
        _readerStream = stream;
        stream.Owner = this;
        stream.Analyzer = analyzer;

        _buffer = new byte[Math.Max(bufferSize, c_MinBufferSize)];

        _encoding = encoding;
        _decoder = encoding.GetDecoder();
        _maxCharsSize = encoding.GetMaxCharCount(c_MaxCharBytesSize);

        // For Encodings that always use 2 bytes per char (or more),
        // special case them here to make Read() & Peek() faster.
        _2BytesPerChar = encoding is UnicodeEncoding;
        _4bytesPerChar = encoding is UTF32Encoding;
    }

    /// <summary>
    /// Throws an <see cref="EndOfStreamException"/> with the message that <see cref="BinaryReader"/> uses.
    /// </summary>
    /// <exception cref="EndOfStreamException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private protected static void ThrowEndOfStreamException()
    {
        throw new EndOfStreamException("Unable to read beyond the end of the stream.");
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Position"/> can be set, which the <c>TryRead*</c> methods need to restore it after a failed read.
    /// </summary>
    private protected bool CanSeek => _readerStream.CanSeek;

    /// <summary>
    /// Gets the size, in bytes, of a single code unit of <see cref="Encoding"/>: 4 for UTF-32, 2 for UTF-16 and 1 otherwise.
    /// </summary>
    private protected int CodeUnitSize => _4bytesPerChar ? 4 : (_2BytesPerChar ? 2 : 1);
}