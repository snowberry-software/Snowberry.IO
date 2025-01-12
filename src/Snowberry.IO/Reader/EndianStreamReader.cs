using System.Text;
using Snowberry.IO.Common.Reader;

namespace Snowberry.IO.Reader;

/// <summary>
/// Supports reading different endian types from streams.
/// </summary>
public partial class EndianStreamReader : BaseEndianReader
{
    protected Stream? _stream;

    /// <summary>
    /// Creates a new reader instance.
    /// </summary>
    /// <param name="stream">The source stream.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The internal buffer size.</param>
    public EndianStreamReader(Stream? stream, Analyzer? analyzer = null, int bufferSize = 0) : base(analyzer, bufferSize)
    {
        _stream = stream;
        analyzer?.Initialize(this);
    }

    /// <summary>
    /// Creates a new reader instance.
    /// </summary>
    /// <param name="stream">The source stream.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The internal buffer size.</param>
    /// <param name="encoding">The encoding to use.</param>
    public EndianStreamReader(Stream? stream, Analyzer? analyzer, int bufferSize, Encoding encoding) : base(analyzer, bufferSize, encoding)
    {
        _stream = stream;
        analyzer?.Initialize(this);
    }

    /// <inheritdoc/>
    public override void CopyTo(Stream destination)
    {
        _ = destination ?? throw new ArgumentNullException(nameof(destination));
        _ = Stream ?? throw new NullReferenceException(nameof(Stream));

        Stream.CopyTo(destination);
    }

    /// <inheritdoc/>
    public override void CopyTo(Stream destination, int length, int bufferSize = 0x14000)
    {
        _ = destination ?? throw new ArgumentNullException(nameof(destination));
        _ = Stream ?? throw new NullReferenceException(nameof(Stream));

        byte[] buffer = new byte[bufferSize];
        int read;
        while (length > 0 && (read = Stream.Read(buffer, 0, Math.Min(bufferSize, length))) > 0)
        {
            if (IsRegionViewEnabled)
                _viewOffset += read;

            Analyzer?.AnalyzeReadBytes(this, buffer.AsSpan(), read);

            destination.Write(buffer, 0, read);
            length -= read;
        }
    }

    /// <inheritdoc/>
    protected override int InternalReadBytes(byte[] inBuffer, int offset, int byteCount)
    {
        return InternalReadBytes(inBuffer.AsSpan()[offset..byteCount]);
    }

    /// <summary>
    /// Close and dispose the <see cref="Stream" />.
    /// </summary>
    public override void Dispose()
    {
        GC.SuppressFinalize(this);

        if (Stream == null)
            return;

        if (KeepStreamOpen)
            return;

        Stream.Dispose();
        Stream.Close();
        Disposed = true;
    }

    /// <inheritdoc />
    public override long Length
    {
        get
        {
            if (IsRegionViewEnabled)
                return RegionView.Size;

            return Stream?.Length ?? 0;
        }
    }

    /// <inheritdoc />
    public override long Position
    {
        get
        {
            ThrowIfDisposed();

            _ = Stream ?? throw new NullReferenceException(nameof(Stream));

            return GetViewOrPosition(Stream.Position);
        }

        set
        {
            ThrowIfDisposed();

            _ = Stream ?? throw new NullReferenceException(nameof(Stream));

            long position = Stream.Position;
            SetPosition(ref position, value);
            Stream.Position = position;
        }
    }

    /// <inheritdoc/>
    public override bool CanReadData
    {
        get
        {
            if (Stream == null || Disposed)
                return false;

            return Length > Position;
        }
    }

    /// <summary>
    /// Do not dispose the current stream at the end.
    /// </summary>
    public virtual bool KeepStreamOpen { get; set; }

    /// <summary>
    /// Returns the stream.
    /// </summary>
    public virtual Stream? Stream => _stream;

    /// <inheritdoc/>
    public override long ActualPosition
    {
        get
        {
            ThrowIfDisposed();

            _ = Stream ?? throw new NullReferenceException(nameof(Stream));
            return Stream.Position;
        }
    }

    /// <inheritdoc/>
    public override long ActualLength
    {
        get
        {
            ThrowIfDisposed();

            _ = Stream ?? throw new NullReferenceException(nameof(Stream));
            return Stream.Length;
        }
    }
}
