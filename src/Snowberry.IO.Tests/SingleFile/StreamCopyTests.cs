using System.Security.Cryptography;
using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.SingleFile.Streams;
using Snowberry.IO.Tests.Infrastructure;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for <see cref="StreamCopy"/>.
/// </summary>
public class StreamCopyTests
{
    private const int c_ChunkSize = 81920;

    [Fact]
    public async Task CopyAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        await FluentActions.Awaiting(() => StreamCopy.CopyAsync(true, new MemoryStream(new byte[10]), Stream.Null, null, new CancellationToken(canceled: true)).AsTask())
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyAsync_CompressedEntry_WritesFullChunks(bool useAsync)
    {
        byte[] data = RandomData(300_000, seed: 8);
        using var bundle = OpenBytes(Build(6, [new("data.bin", data, BundleFileType.Unknown, Compress: true)]));
        var destination = new WriteRecordingStream();
        await using var source = bundle.GetEntry("data.bin").Open();

        await StreamCopy.CopyAsync(useAsync, source, destination, null, TestContext.Current.CancellationToken);

        destination.WriteSizes.Should().Equal(c_ChunkSize, c_ChunkSize, c_ChunkSize, 300_000 - (3 * c_ChunkSize));
        destination.ToArray().Should().Equal(data);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyAsync_SourceReturnsSmallReads_WritesFullChunks(bool useAsync)
    {
        byte[] data = RandomData((3 * c_ChunkSize) + 123, seed: 4);
        var destination = new WriteRecordingStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long copied = await StreamCopy.CopyAsync(useAsync, new ChunkedReadStream(data, maxBytesPerRead: 1000), destination, hash, TestContext.Current.CancellationToken);

        copied.Should().Be(data.Length);
        destination.WriteSizes.Should().Equal(c_ChunkSize, c_ChunkSize, c_ChunkSize, 123);
        destination.ToArray().Should().Equal(data);
        hash.GetHashAndReset().Should().Equal(SHA256.HashData(data));
    }
}