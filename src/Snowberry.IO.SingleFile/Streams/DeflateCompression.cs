using System.IO.Compression;
using System.Security.Cryptography;

#if NET11_0_OR_GREATER
using System.Buffers;
#endif

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// Compresses entry content with Deflate, producing the same bytes as writing it to a <see cref="DeflateStream"/> of the same
/// <see cref="CompressionLevel"/> in chunks of <see cref="StreamCopy.c_DefaultBufferSize"/> bytes.
/// </summary>
/// <remarks>When <c>useAsync</c> is <see langword="false"/>, every operation completes synchronously.</remarks>
internal static class DeflateCompression
{
#if NET11_0_OR_GREATER
    /// <summary>
    /// The size of the buffer the encoder writes into before it is written to the destination: the size <see cref="DeflateStream"/> uses,
    /// because the size of the output buffer changes how zlib divides the output into blocks.
    /// </summary>
    private const int c_OutputBufferSize = 8192;
#endif

    /// <summary>
    /// Compresses <paramref name="source"/> to its end into <paramref name="destination"/> as one complete Deflate stream, feeding every byte
    /// read to <paramref name="hash"/> (if any).
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="level">The compression level.</param>
    /// <param name="source">The stream to read the uncompressed content from.</param>
    /// <param name="destination">The stream to write the compressed content to; it is left open.</param>
    /// <param name="hash">The hash that receives every uncompressed byte, or <see langword="null"/> to skip hashing.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The number of uncompressed bytes read.</returns>
    public static async ValueTask<long> CompressAsync(
        bool useAsync,
        CompressionLevel level,
        Stream source,
        Stream destination,
        IncrementalHash? hash,
        CancellationToken cancellationToken)
    {
#if NET11_0_OR_GREATER
        // Every entry gets a new encoder: a reset encoder can choose different matches than a new one, which would make the output depend on
        // the order in which the entries are compressed.
        const int inputSize = StreamCopy.c_DefaultBufferSize;
        using var encoder = new DeflateEncoder(GetQuality(level));
        byte[] input = ArrayPool<byte>.Shared.Rent(inputSize);
        byte[] output = ArrayPool<byte>.Shared.Rent(c_OutputBufferSize);
        long total = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int read = useAsync
                    ? await source.ReadAtLeastAsync(input.AsMemory(0, inputSize), inputSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false)
                    : source.ReadAtLeast(input.AsSpan(0, inputSize), inputSize, throwOnEndOfStream: false);

                hash?.AppendData(input, 0, read);
                total += read;
                await EncodeAsync(useAsync, encoder, input.AsMemory(0, read), final: false, output, destination, cancellationToken).ConfigureAwait(false);

                // A partial chunk means the source has ended. The stream is ended by a call of its own, like DeflateStream does when it is
                // disposed, because zlib can divide the output differently when the last data and the end come in one call.
                if (read < inputSize)
                {
                    await EncodeAsync(useAsync, encoder, ReadOnlyMemory<byte>.Empty, final: true, output, destination, cancellationToken).ConfigureAwait(false);
                    return total;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(output);
            ArrayPool<byte>.Shared.Return(input);
        }
#else
        var deflate = new DeflateStream(destination, level, leaveOpen: true);

        try
        {
            return await StreamCopy.CopyAsync(useAsync, source, deflate, hash, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Disposing writes the final deflate block.
            await StreamCopy.DisposeAsync(useAsync, deflate).ConfigureAwait(false);
        }
#endif
    }

#if NET11_0_OR_GREATER
    /// <summary>
    /// Gets the zlib level <see cref="DeflateStream"/> uses for <paramref name="level"/>; <c>-1</c> is zlib's default level.
    /// </summary>
    private static int GetQuality(CompressionLevel level)
    {
        return level switch
        {
            CompressionLevel.NoCompression => 0,
            CompressionLevel.Fastest => 1,
            CompressionLevel.SmallestSize => 9,
            _ => -1
        };
    }

    /// <summary>
    /// Feeds <paramref name="data"/> to <paramref name="encoder"/> and writes everything it produces to <paramref name="destination"/>; with
    /// <paramref name="final"/>, also the end of the Deflate stream.
    /// </summary>
    private static async ValueTask EncodeAsync(
        bool useAsync,
        DeflateEncoder encoder,
        ReadOnlyMemory<byte> data,
        bool final,
        byte[] output,
        Stream destination,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var status = encoder.Compress(data.Span, output, out int consumed, out int written, final);
            data = data[consumed..];

            if (written > 0)
                await StreamCopy.WriteAsync(useAsync, destination, output.AsMemory(0, written), cancellationToken).ConfigureAwait(false);

            // Done means all of the data was consumed, and with final that the stream was ended; otherwise the output buffer was full.
            if (status == OperationStatus.Done)
                return;
        }
    }
#endif
}
