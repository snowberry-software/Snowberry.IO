using System.Security.Cryptography;
using AwesomeAssertions;
using Snowberry.IO.SingleFile.Streams;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for <see cref="PageHashingStream"/>, which hashes the pages of a macOS executable as it is written.
/// </summary>
public class PageHashingStreamTests
{
    [Fact]
    public void GetPageHashes_BeforeLimitIsWritten_ThrowsInvalidOperationException()
    {
        using var stream = new PageHashingStream(new MemoryStream(), 100, 16);
        stream.Write(new byte[99]);

        FluentActions.Invoking(() => stream.GetPageHashes().ToArray()).Should().ThrowExactly<InvalidOperationException>();
    }

    [Theory]
    [InlineData(4096, 100_000, false)]
    [InlineData(4096, 4 * 4096, false)]
    [InlineData(16384, 100_000, true)]
    [InlineData(16384, 16384 + 1, true)]
    [InlineData(4096, 1, false)]
    public async Task Write_RandomChunks_HashesEveryPageAndForwardsAllBytes(int pageSize, int limit, bool useAsync)
    {
        byte[] data = RandomData(limit + 5000, seed: limit);
        var destination = new MemoryStream();
        var random = new Random(pageSize + limit);

        using (var stream = new PageHashingStream(destination, limit, pageSize))
        {
            int position = 0;

            // Chunks of 1 byte to more than two pages split pages in every possible way, including across the limit.
            while (position < data.Length)
            {
                int chunk = Math.Min(random.Next(1, (2 * pageSize) + 100), data.Length - position);

                if (useAsync)
                    await stream.WriteAsync(data.AsMemory(position, chunk), TestContext.Current.CancellationToken);
                else
                    stream.Write(data, position, chunk);

                position += chunk;
            }

            stream.GetPageHashes().ToArray().Should().Equal(ExpectedHashes(data, limit, pageSize));
        }

        destination.ToArray().Should().Equal(data);
    }

    private static byte[] ExpectedHashes(byte[] data, int limit, int pageSize)
    {
        using var hashes = new MemoryStream();

        for (int start = 0; start < limit; start += pageSize)
            hashes.Write(SHA256.HashData(data.AsSpan(start, Math.Min(pageSize, limit - start))));

        return hashes.ToArray();
    }
}
