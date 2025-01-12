using System.Runtime.CompilerServices;
using System.Text;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Reader;

/// <summary>
/// Base implementation of <see cref="IEndianReader"/>.
/// </summary>
public abstract partial class BaseEndianReader : IEndianReader
{
    /// <summary>
    /// The largest data type is the <see cref="Sha1"/> type.
    /// </summary>
    public const int MinBufferSize = 20;

    protected const int MaxCharBytesSize = 128;

    protected byte[] _buffer;

    protected long _viewOffset;
    protected bool _isRegionViewEnabled;

    protected readonly Decoder _decoder;
    protected readonly Encoding _encoding;
    protected char[]? _charBuffer;
    protected int _maxCharsSize;

    protected readonly bool _2BytesPerChar;

    protected StringBuilder _stringBuilder = new();

    /// <summary>
    /// Creates a new reader instance.
    /// </summary>
    /// <remarks>
    /// The <paramref name="bufferSize"/> must be at least <see cref="IEndianReader.MinBufferSize"/>, the size will automatically be adjusted if that's not the case.<para/>
    /// The <see cref="Encoding.Default"/> will be used for the <see cref="Encoding"/>.
    /// </remarks>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The size of the internal buffer.</param>
    protected BaseEndianReader(Analyzer? analyzer = null, int bufferSize = 0) : this(analyzer, bufferSize, Encoding.Default)
    {
    }

    /// <summary>
    /// <inheritdoc cref="BaseEndianReader(Analyzer?, int)"/>
    /// </summary>
    /// <remarks>
    /// The <paramref name="bufferSize"/> must be at least 20, if that's not the case it will automatically adjust the size to it.
    /// </remarks>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The size of the internal buffer.</param>
    /// <param name="encoding">The encoding to use.</param>
    protected BaseEndianReader(Analyzer? analyzer, int bufferSize, Encoding encoding)
    {
        _ = encoding ?? throw new ArgumentNullException(nameof(encoding));

        if (bufferSize < MinBufferSize)
            bufferSize = MinBufferSize;

        _buffer = new byte[bufferSize];
        Analyzer = analyzer;

        _encoding = encoding;
        _decoder = encoding.GetDecoder();
        _maxCharsSize = encoding.GetMaxCharCount(MaxCharBytesSize);

        // For Encodings that always use 2 bytes per char (or more),
        // special case them here to make Read() & Peek() faster.
        _2BytesPerChar = encoding is UnicodeEncoding;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ThrowEndOfStreamException()
    {
        throw new EndOfStreamException();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void ThrowIfDisposed()
    {
        if (Disposed)
            throw new ObjectDisposedException(nameof(BaseEndianReader));
    }

    /// <summary>
    /// Reads a specified number of bytes from the current stream into a buffer.
    /// </summary>
    /// <param name="inBuffer">The buffer to store the read bytes.</param>
    /// <param name="offset">The zero-based byte offset in the buffer at which to begin storing the data read from the stream.</param>
    /// <param name="byteCount">The maximum number of bytes to read from the current stream.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero if the end of the stream is reached.
    /// </returns>
    /// <remarks>
    /// This method is intended to be implemented by derived classes to provide the actual logic for reading bytes.
    /// </remarks>
    protected abstract int InternalReadBytes(byte[] inBuffer, int offset, int byteCount);

    /// <inheritdoc/>
    public abstract void CopyTo(Stream destination);

    /// <inheritdoc/>
    public abstract void CopyTo(Stream destination, int length, int bufferSize = 0x14000);

    /// <inheritdoc/>
    public void EnsureBufferSize(int bufferSize)
    {
        ThrowIfDisposed();

        if (Buffer == null || Buffer.Length < bufferSize)
            Buffer = new byte[bufferSize];
    }

    /// <inheritdoc/>
    public virtual void EnableRegionView(RegionRange region)
    {
        ThrowIfDisposed();

        RegionView = region;

        _isRegionViewEnabled = true;
        Position = 0;
    }

    /// <inheritdoc/>
    public virtual void DisableRegionView()
    {
        ThrowIfDisposed();

        _isRegionViewEnabled = false;
        _viewOffset = 0;
    }

    /// <summary>
    /// Returns either the position of the current view offset or the given <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The position that will be used if <see cref="IsRegionViewEnabled"/> is disabled.</param>
    /// <returns>The current position to use.</returns>
    protected long GetViewOrPosition(long position)
    {
        if (IsRegionViewEnabled)
            return _viewOffset;

        return position;
    }

    /// <summary>
    /// Updates the <paramref name="position"/> value..
    /// </summary>
    /// <remarks>
    /// This will automatically adjust <paramref name="position"/> depending on <see cref="IsRegionViewEnabled"/>.
    /// </remarks>
    /// <param name="position">The position to update.</param>
    /// <param name="newPosition">The new position.</param>
    protected void SetPosition(ref long position, long newPosition)
    {
        if (IsRegionViewEnabled)
        {
            _viewOffset = newPosition;
            position = RegionView.StartPosition + newPosition;
            return;
        }

        position = newPosition;
    }

    /// <inheritdoc/>
    public abstract void Dispose();

    /// <inheritdoc/>
    public virtual bool CanReadData => Length > 0;

    /// <inheritdoc/>
    public abstract long Length { get; }

    /// <inheritdoc/>
    public abstract long Position { get; set; }

    /// <inheritdoc/>
    public virtual byte[] Buffer
    {
        get => _buffer;
        set => _buffer = value;
    }

    /// <inheritdoc/>
    public virtual bool IsRegionViewEnabled => _isRegionViewEnabled;

    /// <inheritdoc/>
    public virtual RegionRange RegionView { get; protected set; }

    /// <inheritdoc/>
    public abstract long ActualPosition { get; }

    /// <inheritdoc/>
    public abstract long ActualLength { get; }

    /// <inheritdoc/>
    public bool Disposed { get; protected set; }

    /// <inheritdoc/>
    public Encoding Encoding => _encoding;

    /// <inheritdoc/>
    public Analyzer? Analyzer { get; set; }
}
