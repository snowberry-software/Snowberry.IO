using System.Security.Cryptography;
using AwesomeAssertions;
using Snowberry.IO.SingleFile.Streams;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for <see cref="SpillBuffer"/> and <see cref="SpillFilePool"/>.
/// </summary>
public class SpillBufferTests
{
    private const int c_SegmentLength = 16 * 1024;

    private static byte[] ContentOf(SpillBuffer buffer)
    {
        using var output = new MemoryStream();
        buffer.WriteToAsync(useAsync: false, output, CancellationToken.None).AsTask().GetAwaiter().GetResult().Should().Be(buffer.Length);
        return output.ToArray();
    }

    private static async Task<byte[]> ContentOfAsync(bool useAsync, SpillBuffer buffer)
    {
        using var output = new MemoryStream();
        (await buffer.WriteToAsync(useAsync, output, TestContext.Current.CancellationToken)).Should().Be(buffer.Length);
        return output.ToArray();
    }

    private static string CreateDirectory()
    {
        string directory = NewTempDirectory();
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WriteAsync(bool useAsync, SpillBuffer buffer, byte[] data)
    {
        foreach (byte[] chunk in data.Chunk(8 * 1024))
        {
            if (useAsync)
                await buffer.WriteAsync(chunk, TestContext.Current.CancellationToken);
            else
                buffer.Write(chunk);
        }
    }

    [Fact]
    public void Complete_SpilledBuffers_ShareOneFile()
    {
        var budget = new MemoryBudget(0);
        byte[] first = RandomData(5000, seed: 1);
        byte[] second = RandomData(7000, seed: 2);
        using var files = new SpillFilePool(useAsync: false);
        using var a = new SpillBuffer(budget, files);
        using var b = new SpillBuffer(budget, files);

        a.Write(first);
        a.Complete();
        b.Write(second);
        b.Complete();

        files.FileCount.Should().Be(1);
        ContentOf(a).Should().Equal(first);
        ContentOf(b).Should().Equal(second);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(70_000, false)]
    [InlineData(262_144, true)]
    [InlineData(1_032_192, false)]
    [InlineData(3_000_001, true)]
    public async Task CopyFromAsync_BudgetOfFootprintForExpectedLength_KeepsDataInMemory(int length, bool useAsync)
    {
        long footprint = SpillBuffer.GetMemoryFootprint(length);
        byte[] data = RandomData(length, seed: 6);
        using var files = new SpillFilePool(useAsync);
        using var buffer = new SpillBuffer(new MemoryBudget(footprint), files, expectedLength: length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long copied = await buffer.CopyFromAsync(useAsync, new ChunkedReadStream(data, maxBytesPerRead: 5000), hash, TestContext.Current.CancellationToken);

        copied.Should().Be(length);
        buffer.IsSpilled.Should().BeFalse();
        buffer.ReservedMemory.Should().Be(footprint);
        hash.GetHashAndReset().Should().Equal(SHA256.HashData(data));
        ContentOf(buffer).Should().Equal(data);
    }

    [Fact]
    public async Task CopyFromAsync_EmptySource_RentsNoSegment()
    {
        using var files = new SpillFilePool(useAsync: true);
        using var buffer = new SpillBuffer(new MemoryBudget(long.MaxValue), files, expectedLength: 0);

        (await buffer.CopyFromAsync(useAsync: true, new MemoryStream(), null, TestContext.Current.CancellationToken)).Should().Be(0);

        buffer.ReservedMemory.Should().Be(0);
        ContentOf(buffer).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyFromAsync_SourceLongerThanExpected_KeepsAllData(bool useAsync)
    {
        byte[] data = RandomData(100_000, seed: 7);
        using var files = new SpillFilePool(useAsync);
        using var buffer = new SpillBuffer(new MemoryBudget(SpillBuffer.GetMemoryFootprint(64 * 1024)), files, expectedLength: 64 * 1024);

        long copied = await buffer.CopyFromAsync(useAsync, new MemoryStream(data), null, TestContext.Current.CancellationToken);

        // The budget only covers the expected length, so the rest moves the data to a file.
        copied.Should().Be(data.Length);
        buffer.IsSpilled.Should().BeTrue();
        ContentOf(buffer).Should().Equal(data);
    }

    [Fact]
    public void CreateTempFile_OnUnix_IsOnlyAccessibleByOwner()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes only.");

        using var file = SpillFilePool.CreateTempFile(useAsync: false);

#pragma warning disable CA1416 // Guarded by the skip above.
        File.GetUnixFileMode(file.SafeFileHandle).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
#pragma warning restore CA1416
    }

    [Fact]
    public void Dispose_InMemory_ReturnsBudget()
    {
        var budget = new MemoryBudget(1024 * 1024);
        using var files = new SpillFilePool(useAsync: false);

        var buffer = new SpillBuffer(budget, files);
        buffer.Write(new byte[200_000]);
        buffer.Dispose();

        buffer.ReservedMemory.Should().Be(0);
        budget.TryReserve(1024 * 1024).Should().BeTrue();
        FluentActions.Invoking(() => buffer.Write(new byte[1])).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_SpilledBufferThatIsNotComplete_ReleasesItsFileRegion()
    {
        var budget = new MemoryBudget(0);
        byte[] kept = RandomData(10_000, seed: 9);
        using var files = new SpillFilePool(useAsync: false);

        using (var discarded = new SpillBuffer(budget, files))
            discarded.Write(new byte[50_000]);

        using var buffer = new SpillBuffer(budget, files);
        buffer.Write(kept);

        files.FileCount.Should().Be(1);
        files.Length.Should().Be(kept.Length);
        ContentOf(buffer).Should().Equal(kept);
    }

    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, 16 * 1024L)]
    [InlineData(16 * 1024L, 16 * 1024L)]
    [InlineData((16 * 1024L) + 1, 32 * 1024L)]
    [InlineData(256 * 1024L, 256 * 1024L)]
    [InlineData(300 * 1024L, 304 * 1024L)] // 256 + 32 + 16 KiB
    [InlineData((5 * 1024 * 1024L) + 1, (5 * 1024 * 1024L) + (16 * 1024))]
    [InlineData(long.MaxValue - 10, long.MaxValue)]
    public void GetMemoryFootprint_Length_ReturnsSizeOfSegmentsForThatExpectedLength(long length, long expected)
    {
        SpillBuffer.GetMemoryFootprint(length).Should().Be(expected);
    }

    [Fact]
    public async Task WriteToAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        using var files = new SpillFilePool(useAsync: true);
        using var buffer = new SpillBuffer(new MemoryBudget(long.MaxValue), files);
        buffer.Write(new byte[100]);

        await FluentActions.Awaiting(() => buffer.WriteToAsync(useAsync: true, Stream.Null, new CancellationToken(canceled: true)).AsTask())
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_BeyondBudget_SpillsReleasesMemoryAndKeepsData(bool useAsync)
    {
        const int c_Limit = 100 * 1024;
        var budget = new MemoryBudget(c_Limit);
        byte[] data = RandomData(300 * 1024, seed: 5);
        string directory = CreateDirectory();

        try
        {
            using (var files = new SpillFilePool(useAsync, directory))
            {
                using var buffer = new SpillBuffer(budget, files);
                await WriteAsync(useAsync, buffer, data);

                buffer.IsSpilled.Should().BeTrue();
                buffer.ReservedMemory.Should().Be(0);
                budget.TryReserve(c_Limit).Should().BeTrue();
                budget.Release(c_Limit);

                (await ContentOfAsync(useAsync, buffer)).Should().Equal(data);
                Directory.GetFiles(directory).Should().HaveCount(1);
            }

            // Disposing the pool closes and thereby deletes its files.
            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Write_InMemory_ChargesBudgetPerSegment()
    {
        const long c_Limit = 6 * 1024 * 1024;
        var budget = new MemoryBudget(c_Limit);
        byte[] data = RandomData((5 * 1024 * 1024) + 1000, seed: 3);

        using (var files = new SpillFilePool(useAsync: false))
        using (var buffer = new SpillBuffer(budget, files))
        {
            foreach (byte[] chunk in data.Chunk(64 * 1024))
                buffer.Write(chunk);

            buffer.IsSpilled.Should().BeFalse();
            buffer.Length.Should().Be(data.Length);

            // Segments double up to 1 MiB, so at most the last segment is partly unused.
            buffer.ReservedMemory.Should().BeInRange(data.Length, data.Length + (1024 * 1024));
            budget.TryReserve(c_Limit - buffer.ReservedMemory + 1).Should().BeFalse();

            ContentOf(buffer).Should().Equal(data);
            files.FileCount.Should().Be(0);
        }

        budget.TryReserve(c_Limit).Should().BeTrue();
    }

    [Fact]
    public void Write_SmallData_ReservesOneSmallSegment()
    {
        var budget = new MemoryBudget(long.MaxValue);
        using var files = new SpillFilePool(useAsync: false);
        using var buffer = new SpillBuffer(budget, files);

        buffer.Write(new byte[100]);

        buffer.ReservedMemory.Should().Be(c_SegmentLength);
    }

    [Fact]
    public void Write_SpilledBuffersThatAreNotComplete_UseSeparateFiles()
    {
        var budget = new MemoryBudget(0);
        using var files = new SpillFilePool(useAsync: false);
        using var a = new SpillBuffer(budget, files);
        using var b = new SpillBuffer(budget, files);
        using var c = new SpillBuffer(budget, files);

        a.Write(RandomData(100, seed: 1));
        b.Write(RandomData(200, seed: 2));
        a.Complete();
        b.Complete();
        c.Write(RandomData(300, seed: 3));

        files.FileCount.Should().Be(2);
        ContentOf(a).Should().Equal(RandomData(100, seed: 1));
        ContentOf(b).Should().Equal(RandomData(200, seed: 2));
        ContentOf(c).Should().Equal(RandomData(300, seed: 3));
    }

    [Fact]
    public void Write_ZeroBudget_SpillsAndKeepsData()
    {
        using var files = new SpillFilePool(useAsync: false);
        using var buffer = new SpillBuffer(new MemoryBudget(0), files);
        byte[] data = [1, 2, 3, 4];

        buffer.Write(data);

        buffer.IsSpilled.Should().BeTrue();
        ContentOf(buffer).Should().Equal(data);
    }
}