namespace Snowberry.IO.SingleFile;

public class StreamView : Stream
{
    private readonly bool _leaveOpen;
    private readonly Stream _baseStream;
    private readonly long _offset;
    private readonly long _length;
    private long _currentPosition;

    public StreamView(Stream baseStream, long offset, long length, bool leaveOpen)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        if (!baseStream.CanRead)
            throw new ArgumentException("Stream must be readable", nameof(baseStream));
        if (offset < 0 || length < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "offset/length must be non-negative");
        if (offset + length > baseStream.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "offset + length exceeds stream length");

        _baseStream.Seek(offset, SeekOrigin.Begin);
        _offset = offset;
        _length = length;
        _currentPosition = 0;
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_currentPosition >= _length)
            return 0;
        if (_currentPosition + count > _length)
            count = (int)(_length - _currentPosition);

        int read = _baseStream.Read(buffer, offset, count);
        _currentPosition += read;
        return read;
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        if (_currentPosition >= _length)
            return 0;

        int count = (int)Math.Min(buffer.Length, _length - _currentPosition);
        int read = _baseStream.Read(buffer[..count]);
        _currentPosition += read;
        return read;
    }

    /// <inheritdoc/>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_currentPosition >= _length)
            return 0;

        count = (int)Math.Min(count, _length - _currentPosition);
        int read = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
        _currentPosition += read;
        return read;
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        if (_currentPosition >= _length)
            return -1;

        int result = _baseStream.ReadByte();
        if (result != -1)
            _currentPosition++;

        return result;
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_currentPosition >= _length)
            return 0;

        int count = (int)Math.Min(buffer.Length, _length - _currentPosition);
        int read = await _baseStream.ReadAsync(buffer[..count], cancellationToken);
        _currentPosition += read;
        return read;
    }

    /// <inheritdoc/>
    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        count = _currentPosition >= _length ? 0 : (int)Math.Min(count, _length - _currentPosition);
        return _baseStream.BeginRead(buffer, offset, count, callback, state);
    }

    /// <inheritdoc/>
    public override int EndRead(IAsyncResult asyncResult)
    {
        int read = _baseStream.EndRead(asyncResult);
        _currentPosition += read;
        return read;
    }

    /// <inheritdoc/>
    public override void CopyTo(Stream destination, int bufferSize)
    {
        byte[] buffer = new byte[bufferSize];
        int read;
        while ((read = Read(buffer, 0, buffer.Length)) > 0)
            destination.Write(buffer, 0, read);
    }

    /// <inheritdoc/>
    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[bufferSize];
        int read;
        while ((read = await ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            await destination.WriteAsync(buffer, 0, read, cancellationToken);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => _offset + offset,
            SeekOrigin.Current => _baseStream.Position + offset,
            SeekOrigin.End => _offset + _length + offset,
            _ => throw new ArgumentException("Invalid SeekOrigin", nameof(origin))
        };

        if (newPosition < _offset || newPosition > _offset + _length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _baseStream.Seek(newPosition, SeekOrigin.Begin);
        _currentPosition = newPosition - _offset;
        return _currentPosition;
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _baseStream.FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        _baseStream.Flush();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_leaveOpen)
            _baseStream.Dispose();

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override bool CanRead => _baseStream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => _baseStream.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _currentPosition;
        set
        {
            if (value < 0 || value > _length)
                throw new ArgumentOutOfRangeException(nameof(value));
            _currentPosition = value;
            _baseStream.Position = _offset + value;
        }
    }

    /// <inheritdoc/>
    public override bool CanTimeout => _baseStream.CanTimeout;

    /// <inheritdoc/>
    public override int ReadTimeout
    {
        get => _baseStream.ReadTimeout;
        set => _baseStream.ReadTimeout = value;
    }

    /// <inheritdoc/>
    public override int WriteTimeout
    {
        get => _baseStream.WriteTimeout;
        set => _baseStream.WriteTimeout = value;
    }
}