using AwesomeAssertions;
using Snowberry.IO.SingleFile.Sources;
using Snowberry.IO.SingleFile.Streams;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for <see cref="RangeReadStream"/>.
/// </summary>
public class RangeReadStreamTests
{
    private static readonly byte[] s_Data = RandomData(10_000, seed: 11);

    [Fact]
    public void Read_AfterDispose_ThrowsObjectDisposedException()
    {
        var stream = new RangeReadStream(new CountingSource(s_Data), 0, s_Data.Length, bufferSize: 1024);
        stream.ReadByte();

        stream.Dispose();
        stream.Dispose();

        FluentActions.Invoking(() => stream.ReadByte()).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void Read_BufferedSourceEndsEarly_ThrowsTruncatedException()
    {
        using var stream = new RangeReadStream(new CountingSource(s_Data), 9000, 2000, position => new InvalidDataException($"ended at {position}"), bufferSize: 512);

        FluentActions.Invoking(() => stream.CopyTo(Stream.Null, 100)).Should().ThrowExactly<InvalidDataException>().WithMessage("ended at 1000");
    }

    [Fact]
    public void Read_RequestAtLeastBufferSize_ReadsSourceDirectly()
    {
        var source = new CountingSource(s_Data);
        using var stream = new RangeReadStream(source, 0, s_Data.Length, bufferSize: 1024);
        byte[] target = new byte[5000];

        stream.ReadExactly(target);

        target.Should().Equal(s_Data.AsSpan(0, 5000).ToArray());
        source.Reads.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_SmallReadsWithBuffer_ReadsSourceOncePerBuffer(bool useAsync)
    {
        var source = new CountingSource(s_Data);
        using var stream = new RangeReadStream(source, 100, 9000, bufferSize: 4096);
        byte[] chunk = new byte[100];
        using var copy = new MemoryStream();
        int read;

        while ((read = useAsync ? await stream.ReadAsync(chunk, TestContext.Current.CancellationToken) : stream.Read(chunk)) > 0)
            copy.Write(chunk, 0, read);

        copy.ToArray().Should().Equal(s_Data.AsSpan(100, 9000).ToArray());
        source.Reads.Should().Be(3); // 4096 + 4096 + 808 bytes
    }

    [Fact]
    public void Seek_WithBuffer_ReadsFromNewPosition()
    {
        using var stream = new RangeReadStream(new CountingSource(s_Data), 0, s_Data.Length, bufferSize: 1024);
        byte[] target = new byte[10];

        stream.ReadExactly(target);
        stream.Position = 5;
        stream.ReadExactly(target);
        target.Should().Equal(s_Data.AsSpan(5, 10).ToArray());

        stream.Seek(8000, SeekOrigin.Begin);
        stream.ReadExactly(target);
        target.Should().Equal(s_Data.AsSpan(8000, 10).ToArray());

        stream.Seek(-20, SeekOrigin.End);
        stream.ReadExactly(target);
        target.Should().Equal(s_Data.AsSpan(s_Data.Length - 20, 10).ToArray());
    }

    /// <summary>
    /// A source over memory that counts its reads.
    /// </summary>
    private sealed class CountingSource(byte[] data) : BundleSource("test source")
    {
        private readonly MemoryBundleSource _inner = new(data, "test source");

        public override int Read(long offset, Span<byte> buffer)
        {
            Reads++;
            return _inner.Read(offset, buffer);
        }

        public override ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            Reads++;
            return _inner.ReadAsync(offset, buffer, cancellationToken);
        }

        protected override void DisposeCore()
        {
        }

        public override long Length => data.Length;

        public int Reads { get; private set; }
    }
}