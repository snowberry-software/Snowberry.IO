#if NET11_0_OR_GREATER
using System.Buffers;
using System.IO.Compression;

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// A read-only stream that decompresses the Deflate stream read from <paramref name="compressed"/>, which it disposes when it is disposed.
/// </summary>
/// <remarks>
/// It reads like <see cref="DeflateStream"/> in <see cref="CompressionMode.Decompress"/> mode: reading ends at the end of the Deflate stream
/// or when the compressed data ends early, and invalid data throws <see cref="InvalidDataException"/>. The compressed data is read in
/// large chunks, and the native decoders are kept in a small shared pool, so opening an entry does not create a decoder each time.
/// </remarks>
/// <param name="compressed">The stream to read the compressed data from.</param>
internal sealed class DeflateDecoderStream(Stream compressed) : Stream
{
    /// <summary>
    /// The size of the chunks the compressed data is read in.
    /// </summary>
    public const int c_InputBufferSize = 64 * 1024;

    private static readonly int s_MaxPooledDecoders = Environment.ProcessorCount;
    private static readonly Lock s_PoolLock = new();
    private static readonly Stack<DeflateDecoder> s_Pool = [];

    private DeflateDecoder? _decoder = RentDecoder();
    private byte[]? _input = ArrayPool<byte>.Shared.Rent(c_InputBufferSize);
    private int _inputStart;
    private int _inputEnd;
    private bool _sourceEnded;
    private bool _finished;

    /// <inheritdoc/>
    public override bool CanRead => _decoder != null;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        Span<byte> value = stackalloc byte[1];
        return Read(value) == 0 ? -1 : value[0];
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        var decoder = _decoder;
        ObjectDisposedException.ThrowIf(decoder == null, this);

        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            if (_inputStart == _inputEnd && !_sourceEnded && !_finished)
                Refill(compressed.Read(_input!));

            if (TryDecode(decoder, buffer, out int written))
                return written;
        }
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var decoder = _decoder;
        ObjectDisposedException.ThrowIf(decoder == null, this);

        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            if (_inputStart == _inputEnd && !_sourceEnded && !_finished)
                Refill(await compressed.ReadAsync(_input!, cancellationToken).ConfigureAwait(false));

            if (TryDecode(decoder, buffer.Span, out int written))
                return written;
        }
    }

    /// <inheritdoc/>
    public override void Flush()
    {
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
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        if (_decoder != null)
        {
            Release();
            await compressed.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && _decoder != null)
        {
            Release();
            compressed.Dispose();
        }

        base.Dispose(disposing);
    }

    private static DeflateDecoder RentDecoder()
    {
        lock (s_PoolLock)
        {
            if (s_Pool.TryPop(out var decoder))
                return decoder;
        }

        return new DeflateDecoder();
    }

    private static void ReturnDecoder(DeflateDecoder decoder)
    {
        decoder.Reset();

        lock (s_PoolLock)
        {
            if (s_Pool.Count < s_MaxPooledDecoders)
            {
                s_Pool.Push(decoder);
                return;
            }
        }

        decoder.Dispose();
    }

    private void Refill(int read)
    {
        _inputStart = 0;
        _inputEnd = read;
        _sourceEnded = read == 0;
    }

    /// <summary>
    /// Decodes the buffered input into <paramref name="buffer"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the read is complete with <paramref name="written"/> bytes; <see langword="false"/> if more input must
    /// be read first.</returns>
    private bool TryDecode(DeflateDecoder decoder, Span<byte> buffer, out int written)
    {
        if (_finished)
        {
            written = 0;
            return true;
        }

        var status = decoder.Decompress(_input!.AsSpan(_inputStart, _inputEnd - _inputStart), buffer, out int consumed, out written);
        _inputStart += consumed;

        switch (status)
        {
            case OperationStatus.Done:
                _finished = true;
                return true;

            case OperationStatus.InvalidData:
                throw new InvalidDataException("The compressed data is not valid Deflate data.");

            case OperationStatus.DestinationTooSmall:
                return true;

            default:
                // More input is needed: return what was decoded so far, or the end of the stream when the compressed data has ended.
                return written > 0 || (_sourceEnded && _inputStart == _inputEnd);
        }
    }

    private void Release()
    {
        var decoder = _decoder!;
        _decoder = null;
        ReturnDecoder(decoder);

        ArrayPool<byte>.Shared.Return(_input!);
        _input = null;
    }
}
#endif