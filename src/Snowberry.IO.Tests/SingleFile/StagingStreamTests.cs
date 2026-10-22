using System.Security.Cryptography;
using AwesomeAssertions;
using Snowberry.IO.SingleFile.Streams;
using Snowberry.IO.Tests.Infrastructure;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for <see cref="StagingStream"/>.
/// </summary>
public class StagingStreamTests
{
    private const int c_BufferSize = 1000;

    [Fact]
    public void Advance_BeyondFreeSpace_ThrowsArgumentOutOfRangeException()
    {
        using var staging = new StagingStream(Stream.Null, c_BufferSize);
        staging.Advance(600);

        staging.Free.Length.Should().Be(400);
        FluentActions.Invoking(() => staging.Advance(401)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyFromAsync_SourceWithSmallReads_FillsBufferAndHashes(bool useAsync)
    {
        byte[] data = RandomData(2500, seed: 2);
        var destination = new WriteRecordingStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        using (var staging = new StagingStream(destination, c_BufferSize))
        {
            await staging.WriteZerosAsync(useAsync, 300, TestContext.Current.CancellationToken);
            long copied = await staging.CopyFromAsync(useAsync, new ChunkedReadStream(data, maxBytesPerRead: 64), hash, TestContext.Current.CancellationToken);
            await staging.FlushBufferAsync(useAsync, TestContext.Current.CancellationToken);

            copied.Should().Be(data.Length);
        }

        destination.WriteSizes.Should().Equal(1000, 1000, 800);
        destination.ToArray().Should().Equal([.. new byte[300], .. data]);
        hash.GetHashAndReset().Should().Equal(SHA256.HashData(data));
    }

    [Fact]
    public void Dispose_ThenWrite_ThrowsObjectDisposedException()
    {
        var staging = new StagingStream(Stream.Null, c_BufferSize);
        staging.Dispose();
        staging.Dispose();

        staging.CanWrite.Should().BeFalse();
        FluentActions.Invoking(() => staging.Write(new byte[1])).Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_ManySmallWrites_WritesFullBuffersToDestination(bool useAsync)
    {
        byte[] data = RandomData(3500, seed: 1);
        var destination = new WriteRecordingStream();

        using (var staging = new StagingStream(destination, c_BufferSize))
        {
            foreach (byte[] chunk in data.Chunk(170))
            {
                if (useAsync)
                    await staging.WriteAsync(chunk, TestContext.Current.CancellationToken);
                else
                    staging.Write(chunk);
            }

            staging.Length.Should().Be(data.Length);
            await staging.FlushBufferAsync(useAsync, TestContext.Current.CancellationToken);
        }

        destination.WriteSizes.Should().Equal(1000, 1000, 1000, 500);
        destination.ToArray().Should().Equal(data);
    }
}