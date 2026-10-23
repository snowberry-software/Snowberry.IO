namespace Snowberry.IO.SingleFile.Sources;

/// <summary>
/// Reads memory; reads need no synchronization because they do not share a position.
/// </summary>
/// <param name="memory">The bytes to read.</param>
/// <param name="objectName">The name reported by <see cref="ObjectDisposedException"/> after the source is disposed.</param>
internal sealed class MemoryBundleSource(ReadOnlyMemory<byte> memory, string objectName) : BundleSource(objectName)
{
    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        ThrowIfDisposed();

        if (offset >= memory.Length)
            return 0;

        var available = memory.Span[(int)offset..];
        int count = Math.Min(available.Length, buffer.Length);
        available[..count].CopyTo(buffer);
        return count;
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(Read(offset, buffer.Span));
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
    }

    /// <inheritdoc/>
    public override long Length => memory.Length;
}