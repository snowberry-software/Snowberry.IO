using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Snowberry.IO.SingleFile.Streams;
using Xunit;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for <see cref="DeflateCompression"/>.
/// </summary>
public class DeflateCompressionTests
{
    public static TheoryData<CompressionLevel, int, bool> LevelsAndLengths()
    {
        var data = new TheoryData<CompressionLevel, int, bool>();

        foreach (var level in new[] { CompressionLevel.Optimal, CompressionLevel.Fastest, CompressionLevel.NoCompression, CompressionLevel.SmallestSize })
        {
            foreach (int length in new[] { 0, 1, 700, StreamCopy.c_DefaultBufferSize, StreamCopy.c_DefaultBufferSize + 1, 1_000_000 })
            {
                data.Add(level, length, false);
                data.Add(level, length, true);
            }
        }

        return data;
    }

    internal static byte[] TextData(int length, int seed)
    {
        string[] words = ["bundle", "entry", "host", "manifest", "offset", "stream", "deflate", "assembly"];
        var random = new Random(seed);
        var builder = new StringBuilder(length + 16);

        while (builder.Length < length)
            builder.Append(words[random.Next(words.Length)]).Append(random.Next(8) == 0 ? '\n' : ' ');

        return Encoding.ASCII.GetBytes(builder.ToString(0, length));
    }

    private static byte[] CompressWithDeflateStream(byte[] content, CompressionLevel level)
    {
        using var output = new MemoryStream();

        // Written in the chunks DeflateCompression reads, since zlib's output depends on how the input is divided.
        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
        {
            foreach (byte[] chunk in content.Chunk(StreamCopy.c_DefaultBufferSize))
                deflate.Write(chunk, 0, chunk.Length);
        }

        return output.ToArray();
    }

    [Theory]
    [MemberData(nameof(LevelsAndLengths))]
    public async Task CompressAsync_AnyLevelAndLength_WritesTheBytesOfDeflateStream(CompressionLevel level, int length, bool useAsync)
    {
        byte[] content = TextData(length, seed: length);
        using var output = new MemoryStream();

        long read = await DeflateCompression.CompressAsync(useAsync, level, new MemoryStream(content), output, hash: null, TestContext.Current.CancellationToken);

        read.Should().Be(length);
        output.ToArray().Should().Equal(CompressWithDeflateStream(content, level));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompressAsync_WithHash_HashesTheUncompressedContent(bool useAsync)
    {
        byte[] content = TextData(200_000, seed: 1);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await DeflateCompression.CompressAsync(useAsync, CompressionLevel.SmallestSize, new MemoryStream(content), Stream.Null, hash, TestContext.Current.CancellationToken);

        hash.GetHashAndReset().Should().Equal(SHA256.HashData(content));
    }

    [Fact]
    public async Task CompressAsync_ManyEntriesInParallel_WritesTheSameBytesAsOneAtATime()
    {
        byte[][] contents = [.. Enumerable.Range(0, 32).Select(i => TextData(1000 + (i * 1700), seed: i))];
        byte[][] expected = [.. contents.Select(content => CompressWithDeflateStream(content, CompressionLevel.SmallestSize))];
        byte[][] actual = new byte[contents.Length][];

        await Parallel.ForAsync(0, contents.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (i, token) =>
        {
            using var output = new MemoryStream();
            await DeflateCompression.CompressAsync(true, CompressionLevel.SmallestSize, new MemoryStream(contents[i]), output, hash: null, token);
            actual[i] = output.ToArray();
        });

        actual.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task CompressAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var compress = () => DeflateCompression.CompressAsync(true, CompressionLevel.Optimal, new MemoryStream(TextData(1000, seed: 1)), Stream.Null, hash: null, cancellation.Token).AsTask();

        await compress.Should().ThrowAsync<OperationCanceledException>();
    }
}