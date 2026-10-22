using System.Buffers;
using System.Security.Cryptography;

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// Buffered copy helpers that share one implementation between the synchronous and asynchronous APIs.
/// </summary>
/// <remarks>When <c>useAsync</c> is <see langword="false"/>, every operation completes synchronously.</remarks>
internal static class StreamCopy
{
    /// <summary>
    /// The default size of the chunks <see cref="CopyAsync"/> reads, the size <see cref="Stream.CopyTo(Stream)"/> uses.
    /// </summary>
    public const int c_DefaultBufferSize = 81920;

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> (if any), feeding every byte to <paramref name="hash"/> (if any).
    /// </summary>
    /// <remarks>
    /// Every chunk is filled before it is written, also from streams that return less per read than requested, such as
    /// <see cref="System.IO.Compression.DeflateStream"/>.
    /// </remarks>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The stream to read from.</param>
    /// <param name="destination">The stream to write to, or <see langword="null"/> to only read.</param>
    /// <param name="hash">The hash that receives every byte read, or <see langword="null"/> to skip hashing.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <param name="bufferSize">The size of the chunks, which are read into a buffer rented from <see cref="ArrayPool{T}.Shared"/>.</param>
    /// <returns>The number of bytes copied.</returns>
    public static async ValueTask<long> CopyAsync(
        bool useAsync,
        Stream source,
        Stream? destination,
        IncrementalHash? hash,
        CancellationToken cancellationToken,
        int bufferSize = c_DefaultBufferSize)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        long total = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = useAsync
                    ? await source.ReadAtLeastAsync(buffer.AsMemory(0, bufferSize), bufferSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false)
                    : source.ReadAtLeast(buffer.AsSpan(0, bufferSize), bufferSize, throwOnEndOfStream: false);

                if (read == 0)
                    return total;

                hash?.AppendData(buffer, 0, read);

                if (destination != null)
                    await WriteAsync(useAsync, destination, buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);

                total += read;

                // A partial chunk means the source has ended.
                if (read < bufferSize)
                    return total;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Disposes <paramref name="stream"/>.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to dispose asynchronously; <see langword="false"/> to complete synchronously.</param>
    /// <param name="stream">The stream to dispose.</param>
    /// <returns>A task that completes when the stream is disposed.</returns>
    public static ValueTask DisposeAsync(bool useAsync, Stream stream)
    {
        if (useAsync)
            return stream.DisposeAsync();

        stream.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Flushes <paramref name="destination"/>.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="destination">The stream to flush.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the stream is flushed.</returns>
    public static ValueTask FlushAsync(bool useAsync, Stream destination, CancellationToken cancellationToken)
    {
        if (useAsync)
            return new ValueTask(destination.FlushAsync(cancellationToken));

        destination.Flush();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Writes <paramref name="data"/> to <paramref name="destination"/>.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="destination">The stream to write to.</param>
    /// <param name="data">The bytes to write.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the data is written.</returns>
    public static ValueTask WriteAsync(bool useAsync, Stream destination, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        if (useAsync)
            return destination.WriteAsync(data, cancellationToken);

        destination.Write(data.Span);
        return ValueTask.CompletedTask;
    }
}