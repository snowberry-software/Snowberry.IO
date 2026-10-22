namespace Snowberry.IO.SingleFile.Sources;

/// <summary>
/// Reads a seekable stream; reads are serialized because they share the stream position.
/// </summary>
/// <remarks>The gate is intentionally not disposed, so a read racing with disposal fails with <see cref="ObjectDisposedException"/> from the stream instead of from the gate.</remarks>
internal sealed class StreamBundleSource : BundleSource
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _leaveOpen;
    private readonly Stream _stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamBundleSource"/> class.
    /// </summary>
    /// <param name="stream">The seekable stream to read.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the source is disposed; <see langword="false"/> to dispose it.</param>
    /// <param name="objectName">The name reported by <see cref="ObjectDisposedException"/> after the source is disposed.</param>
    public StreamBundleSource(Stream stream, bool leaveOpen, string objectName) : base(objectName)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        Length = stream.Length;
    }

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        ThrowIfDisposed();
        _gate.Wait();

        try
        {
            _stream.Position = offset;
            return _stream.Read(buffer);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _stream.Position = offset;
            return await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }

    /// <inheritdoc/>
    protected override ValueTask DisposeCoreAsync()
    {
        return _leaveOpen ? ValueTask.CompletedTask : _stream.DisposeAsync();
    }

    /// <inheritdoc/>
    public override long Length { get; }
}