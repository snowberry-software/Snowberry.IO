using System.Runtime.CompilerServices;
using Snowberry.IO.Common.Reader;

namespace Snowberry.IO.Reader.Streams;

/// <summary>
/// The stream that a <see cref="BaseEndianReader"/> hands to its <see cref="BinaryReader"/> base, so that every inherited
/// <see cref="BinaryReader"/> read goes through the region view and the <see cref="Analyzer"/>.
/// </summary>
/// <remarks>
/// <para><see cref="Position"/> and <see cref="Length"/> are relative to the region view while one is enabled, reads are clamped
/// to its end, and every read is reported to <see cref="Analyzer"/>.</para>
/// <para>Derived types implement the reads themselves (instead of a shared virtual core) so a <see cref="BinaryReader"/> read
/// costs a single virtual call into this stream.</para>
/// </remarks>
internal abstract class ReaderStream : Stream
{
    /// <summary>
    /// The analyzer that every read is reported to, or <see langword="null"/>.
    /// </summary>
    protected Analyzer? _analyzer;

    /// <summary>
    /// Indicates whether this stream is disposed.
    /// </summary>
    protected bool _disposed;

    /// <summary>
    /// Indicates whether a region view is enabled.
    /// </summary>
    protected bool _isRegionViewEnabled;

    /// <summary>
    /// The active region view, valid while <see cref="_isRegionViewEnabled"/> is <see langword="true"/>.
    /// </summary>
    protected RegionRange _regionView;

    /// <summary>
    /// The position within the region view, valid while <see cref="_isRegionViewEnabled"/> is <see langword="true"/>.
    /// </summary>
    protected long _viewOffset;

    private BaseEndianReader? _owner;

    /// <summary>
    /// Throws an <see cref="EndOfStreamException"/> with the message that <see cref="BinaryReader"/> uses.
    /// </summary>
    /// <exception cref="EndOfStreamException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowEndOfStream()
    {
        throw new EndOfStreamException("Unable to read beyond the end of the stream.");
    }

    /// <summary>
    /// Throws the <see cref="ArgumentOutOfRangeException"/> of <see cref="Position"/> for a negative position inside a region view.
    /// </summary>
    /// <param name="value">The negative position.</param>
    /// <exception cref="ArgumentOutOfRangeException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNegativeViewPosition(long value)
    {
        throw new ArgumentOutOfRangeException(nameof(value), value, "The position cannot be negative while a region view is enabled.");
    }

    /// <summary>
    /// Throws the <see cref="InvalidOperationException"/> of <see cref="Owner"/> for a stream without an owning reader.
    /// </summary>
    /// <returns>Never returns.</returns>
    /// <exception cref="InvalidOperationException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BaseEndianReader ThrowNoOwner()
    {
        throw new InvalidOperationException("The stream has no owning reader.");
    }

    /// <inheritdoc/>
    public sealed override void Flush()
    {
    }

    /// <inheritdoc/>
    public sealed override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown seek origin.")
        };

        Position = target;
        return Position;
    }

    /// <inheritdoc/>
    public sealed override void SetLength(long value)
    {
        throw new NotSupportedException("The stream is read-only.");
    }

    /// <inheritdoc/>
    public sealed override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("The stream is read-only.");
    }

    /// <summary>
    /// Disables the region view; <see cref="Position"/> becomes the position in the underlying data source again.
    /// </summary>
    internal void DisableRegionView()
    {
        _isRegionViewEnabled = false;
        _viewOffset = 0;
        _owner?.RefreshDirectSource();
    }

    /// <summary>
    /// Enables <paramref name="region"/> and moves to its start.
    /// </summary>
    /// <param name="region">The already validated region.</param>
    internal void EnableRegionView(RegionRange region)
    {
        _regionView = region;
        _isRegionViewEnabled = true;
        _owner?.RefreshDirectSource();
        Position = 0;
    }

    /// <summary>
    /// Fills the first <paramref name="count"/> bytes of <paramref name="buffer"/>, of which the first <paramref name="filled"/> are
    /// already read, honoring the region view and reporting every read to <see cref="Analyzer"/>.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="filled">The number of bytes at the start of <paramref name="buffer"/> that are already read.</param>
    /// <param name="count">The number of bytes to read in total.</param>
    /// <exception cref="EndOfStreamException">The end of the data or region is reached before <paramref name="count"/> bytes are read.</exception>
    internal void FillReported(byte[] buffer, int filled, int count)
    {
        int totalRead = filled;

        while (totalRead < count)
        {
            int read = Read(buffer, totalRead, count - totalRead);

            if (read == 0)
                ThrowEndOfStream();

            totalRead += read;
        }
    }

    /// <summary>
    /// Reads bytes into <paramref name="buffer"/>, honoring the region view, and reports them to <see cref="Analyzer"/>.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <returns>The number of bytes read, or zero at the end of the data or region.</returns>
    internal int ReadReported(Span<byte> buffer)
    {
        int read = ReadUnreported(buffer);

        if (_analyzer != null)
            ReportRead(buffer, read);

        return read;
    }

    /// <summary>
    /// Reads bytes into <paramref name="buffer"/>, honoring the region view, without reporting them to <see cref="Analyzer"/>.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <returns>The number of bytes read, or zero at the end of the data or region.</returns>
    internal abstract int ReadUnreported(Span<byte> buffer);

    /// <summary>
    /// Reads bytes into a range of <paramref name="buffer"/>, honoring the region view, without reporting them to <see cref="Analyzer"/>.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to start storing the data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read, or zero at the end of the data or region.</returns>
    internal virtual int ReadUnreported(byte[] buffer, int offset, int count)
    {
        return ReadUnreported(buffer.AsSpan(offset, count));
    }

    /// <summary>
    /// Returns how many of <paramref name="count"/> requested bytes the region view allows.
    /// </summary>
    /// <param name="count">The number of requested bytes.</param>
    /// <returns><paramref name="count"/> without a region view; otherwise, at most the bytes left in the region.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected int ClampToRegion(int count)
    {
        if (!_isRegionViewEnabled)
            return count;

        long remaining = _regionView.Size - _viewOffset;

        if (remaining <= 0)
            return 0;

        return count > remaining ? (int)remaining : count;
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    /// <summary>
    /// Advances the region view offset by <paramref name="read"/> bytes and reports the read to <see cref="Analyzer"/>.
    /// </summary>
    /// <param name="buffer">The buffer that was requested to be filled.</param>
    /// <param name="read">The number of bytes read into <paramref name="buffer"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void OnRead(Span<byte> buffer, int read)
    {
        if (_isRegionViewEnabled)
            _viewOffset += read;

        if (_analyzer != null)
            ReportRead(buffer, read);
    }

    /// <summary>
    /// Advances the region view offset by <paramref name="read"/> bytes and reports the read to <see cref="Analyzer"/>.
    /// </summary>
    /// <remarks>The span for <see cref="Analyzer"/> is only created when one is set, which matters for portable spans on .NET Framework.</remarks>
    /// <param name="buffer">The buffer that was requested to be filled.</param>
    /// <param name="offset">The offset in <paramref name="buffer"/> where the read started.</param>
    /// <param name="count">The number of bytes that were requested.</param>
    /// <param name="read">The number of bytes read into <paramref name="buffer"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void OnRead(byte[] buffer, int offset, int count, int read)
    {
        if (_isRegionViewEnabled)
            _viewOffset += read;

        if (_analyzer != null)
            ReportRead(buffer, offset, count, read);
    }

    /// <summary>
    /// Advances the region view offset after a single-byte read and reports the byte to <see cref="Analyzer"/>.
    /// </summary>
    /// <param name="value">The byte read, or -1 at the end of the data.</param>
    /// <returns>The byte, as modified by <see cref="Analyzer"/>, or -1 at the end of the data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected int OnReadByte(int value)
    {
        if (value < 0)
        {
            if (_analyzer != null)
                ReportEndOfData();

            return -1;
        }

        if (_isRegionViewEnabled)
            _viewOffset++;

        return _analyzer is null ? value : ReportByte(value);
    }

    /// <summary>
    /// Reports a read to <see cref="Analyzer"/>, which must be set.
    /// </summary>
    /// <remarks>Kept out of the read methods, so that a read without an analyzer sets up neither the span nor the call.</remarks>
    /// <param name="buffer">The buffer that was requested to be filled.</param>
    /// <param name="read">The number of bytes read into <paramref name="buffer"/>.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected void ReportRead(Span<byte> buffer, int read)
    {
        _analyzer!.AnalyzeReadBytes(_owner!, buffer, read);
    }

    /// <summary>
    /// Reports a read into a range of <paramref name="buffer"/> to <see cref="Analyzer"/>, which must be set.
    /// </summary>
    /// <remarks>Kept out of the read methods, so that a read without an analyzer sets up neither the span nor the call.</remarks>
    /// <param name="buffer">The buffer that was requested to be filled.</param>
    /// <param name="offset">The offset in <paramref name="buffer"/> where the read started.</param>
    /// <param name="count">The number of bytes that were requested.</param>
    /// <param name="read">The number of bytes read into <paramref name="buffer"/>.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected void ReportRead(byte[] buffer, int offset, int count, int read)
    {
        _analyzer!.AnalyzeReadBytes(_owner!, buffer.AsSpan(offset, count), read);
    }

    /// <summary>
    /// Reports a single byte to <see cref="Analyzer"/>, which must be set.
    /// </summary>
    /// <remarks>Kept out of <see cref="OnReadByte"/>, because a method with <see langword="stackalloc"/> is not inlined into loops.</remarks>
    /// <param name="value">The byte that was read.</param>
    /// <returns>The byte, as modified by <see cref="Analyzer"/>.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ReportByte(int value)
    {
        Span<byte> single = stackalloc byte[1];
        single[0] = (byte)value;
        _analyzer!.AnalyzeReadBytes(_owner!, single, 1);
        return single[0];
    }

    /// <summary>
    /// Reports a single-byte read that found no more data to <see cref="Analyzer"/>, which must be set.
    /// </summary>
    /// <remarks>Kept out of <see cref="OnReadByte"/>, so that the empty span is only set up when an analyzer is set.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReportEndOfData()
    {
        _analyzer!.AnalyzeReadBytes(_owner!, Span<byte>.Empty, 0);
    }

    /// <inheritdoc/>
    public override bool CanRead => !_disposed;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public sealed override long Length => _isRegionViewEnabled ? _regionView.Size : UnviewedLength;

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative while a region view is enabled.</exception>
    public sealed override long Position
    {
        get => _isRegionViewEnabled ? _viewOffset : SourcePosition;
        set
        {
            if (!_isRegionViewEnabled)
            {
                SourcePosition = value;
                return;
            }

            if (value < 0)
                ThrowNegativeViewPosition(value);

            SourcePosition = _regionView.StartPosition + value;
            _viewOffset = value;
        }
    }

    /// <summary>
    /// Gets the length of the underlying data source, ignoring the region view.
    /// </summary>
    internal abstract long ActualLength { get; }

    /// <summary>
    /// Gets the position in the underlying data source, ignoring the region view.
    /// </summary>
    internal long ActualPosition => SourcePosition;

    /// <summary>
    /// Gets or sets the analyzer that every read is reported to.
    /// </summary>
    internal Analyzer? Analyzer
    {
        get => _analyzer;
        set
        {
            _analyzer = value;
            _owner?.RefreshDirectSource();
        }
    }

    /// <summary>
    /// Gets the stream that the owning reader can read from instead of this stream, or <see langword="null"/> when every read has
    /// to go through this stream.
    /// </summary>
    /// <remarks>
    /// A stream is only returned while reading it is the same as reading this stream, that is while no <see cref="Analyzer"/> and
    /// no region view is active. The owner keeps the value until <see cref="Analyzer"/> or the region view changes.
    /// </remarks>
    internal virtual Stream? DirectSource => null;

    /// <summary>
    /// Gets a value indicating whether a region view is enabled.
    /// </summary>
    internal bool IsRegionViewEnabled => _isRegionViewEnabled;

    /// <summary>
    /// Gets or sets the reader that owns this stream; it is passed to <see cref="Analyzer.AnalyzeReadBytes"/>.
    /// </summary>
    internal BaseEndianReader Owner
    {
        get => _owner ?? ThrowNoOwner();
        set => _owner = value;
    }

    /// <summary>
    /// Gets the active region view.
    /// </summary>
    internal RegionRange RegionView => _regionView;

    /// <summary>
    /// Gets or sets the position in the underlying data source, ignoring the region view.
    /// </summary>
    protected abstract long SourcePosition { get; set; }

    /// <summary>
    /// Gets the value <see cref="Length"/> reports while no region view is enabled.
    /// </summary>
    protected abstract long UnviewedLength { get; }
}