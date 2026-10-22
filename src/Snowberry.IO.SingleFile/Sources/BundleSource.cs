namespace Snowberry.IO.SingleFile.Sources;

/// <summary>
/// Positional, read-only access to the bytes of a bundle or of entry content.
/// </summary>
/// <param name="objectName">The name reported by <see cref="ObjectDisposedException"/> after the source is disposed.</param>
internal abstract class BundleSource(string objectName) : IDisposable, IAsyncDisposable
{
    private int _disposed;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            DisposeCore();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        return Interlocked.Exchange(ref _disposed, 1) == 0 ? DisposeCoreAsync() : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Reads bytes at an absolute position without changing any shared position.
    /// </summary>
    /// <param name="offset">The position of the first byte to read.</param>
    /// <param name="buffer">The buffer that receives the bytes.</param>
    /// <returns>The number of bytes read, or <c>0</c> when <paramref name="offset"/> is at or past the end.</returns>
    public abstract int Read(long offset, Span<byte> buffer);

    /// <summary>
    /// Asynchronously reads bytes at an absolute position without changing any shared position.
    /// </summary>
    /// <param name="offset">The position of the first byte to read.</param>
    /// <param name="buffer">The buffer that receives the bytes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that produces the number of bytes read, or <c>0</c> when <paramref name="offset"/> is at or past the end.</returns>
    public abstract ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>
    /// Reads bytes at an absolute position, asynchronously or synchronously.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="offset">The position of the first byte to read.</param>
    /// <param name="buffer">The buffer that receives the bytes.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that produces the number of bytes read, or <c>0</c> when <paramref name="offset"/> is at or past the end.</returns>
    public ValueTask<int> ReadAsync(bool useAsync, long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        return useAsync ? ReadAsync(offset, buffer, cancellationToken) : new(Read(offset, buffer.Span));
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> with the bytes starting at an absolute position.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="offset">The position of the first byte to read.</param>
    /// <param name="buffer">The buffer to fill completely.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the buffer is full.</returns>
    /// <exception cref="EndOfStreamException">The source ends before the buffer is full.</exception>
    public async ValueTask ReadExactlyAsync(bool useAsync, long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        while (!buffer.IsEmpty)
        {
            int read = await ReadAsync(useAsync, offset, buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
                throw new EndOfStreamException();

            offset += read;
            buffer = buffer[read..];
        }
    }

    /// <summary>
    /// Releases the resources of the source; called once, on the first disposal.
    /// </summary>
    protected abstract void DisposeCore();

    /// <summary>
    /// Asynchronously releases the resources of the source; called once, on the first disposal.
    /// </summary>
    /// <returns>A task that completes when the resources have been released.</returns>
    protected virtual ValueTask DisposeCoreAsync()
    {
        DisposeCore();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Throws if the source has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The source has been disposed.</exception>
    protected void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(objectName);
    }

    /// <summary>
    /// Gets the full path of the underlying file, or <see langword="null"/> when the source is not a file.
    /// </summary>
    public virtual string? FilePath => null;

    /// <summary>
    /// Gets the length of the source in bytes.
    /// </summary>
    public abstract long Length { get; }

    /// <summary>
    /// Gets a value indicating whether the source has been disposed.
    /// </summary>
    protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;
}