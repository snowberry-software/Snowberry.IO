#if NET11_0_OR_GREATER
using System.IO.Compression;
using AwesomeAssertions;
using Snowberry.IO.SingleFile.Streams;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for <see cref="DeflateDecoderStream"/>.
/// </summary>
public class DeflateDecoderStreamTests
{
    // A fixed Huffman block whose first symbol copies from before the start of the output.
    private static readonly byte[] s_InvalidData = [0x03, 0x02, 0x00];

    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();

        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(content, 0, content.Length);

        return output.ToArray();
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, bool useAsync, int chunkSize)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[chunkSize];

        while (true)
        {
            int read = useAsync
                ? await stream.ReadAsync(buffer.AsMemory(), TestContext.Current.CancellationToken)
                : stream.Read(buffer, 0, buffer.Length);

            if (read == 0)
                return output.ToArray();

            output.Write(buffer, 0, read);
        }
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(100_000, 7, false)]
    [InlineData(100_000, 7, true)]
    [InlineData(1_000_000, 81920, false)]
    [InlineData(1_000_000, 81920, true)]
    public async Task Read_CompressedContent_ReturnsTheContent(int length, int chunkSize, bool useAsync)
    {
        byte[] content = DeflateCompressionTests.TextData(length, seed: length);
        using var stream = new DeflateDecoderStream(new MemoryStream(Compress(content)));

        (await ReadAllAsync(stream, useAsync, chunkSize)).Should().Equal(content);
    }

    [Fact]
    public void ReadByte_CompressedContent_ReturnsEveryByteThenMinusOne()
    {
        byte[] content = DeflateCompressionTests.TextData(500, seed: 1);
        using var stream = new DeflateDecoderStream(new MemoryStream(Compress(content)));

        foreach (byte value in content)
            stream.ReadByte().Should().Be(value);

        stream.ReadByte().Should().Be(-1);
    }

    [Fact]
    public async Task Read_ChunkedSource_ReturnsTheContent()
    {
        byte[] content = DeflateCompressionTests.TextData(300_000, seed: 2);
        using var stream = new DeflateDecoderStream(new ChunkedReadStream(Compress(content), 3));

        (await ReadAllAsync(stream, useAsync: false, 4096)).Should().Equal(content);
    }

    [Fact]
    public async Task Read_DataAfterTheEndOfTheDeflateStream_IsIgnored()
    {
        byte[] content = DeflateCompressionTests.TextData(10_000, seed: 3);
        using var stream = new DeflateDecoderStream(new MemoryStream([.. Compress(content), 1, 2, 3]));

        (await ReadAllAsync(stream, useAsync: false, 4096)).Should().Equal(content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_TruncatedData_EndsLikeDeflateStream(bool useAsync)
    {
        byte[] compressed = Compress(DeflateCompressionTests.TextData(100_000, seed: 4));
        byte[] truncated = compressed[..(compressed.Length / 2)];

        using var expectedStream = new DeflateStream(new MemoryStream(truncated), CompressionMode.Decompress);
        using var stream = new DeflateDecoderStream(new MemoryStream(truncated));

        (await ReadAllAsync(stream, useAsync, 4096)).Should().Equal(await ReadAllAsync(expectedStream, useAsync, 4096));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_InvalidData_ThrowsInvalidDataException(bool useAsync)
    {
        using var stream = new DeflateDecoderStream(new MemoryStream(s_InvalidData));

        var read = () => ReadAllAsync(stream, useAsync, 4096);

        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Read_AfterAStreamWithInvalidDataWasDisposed_ReturnsTheContent()
    {
        // The decoders are pooled, so the decoder that saw invalid data may be the next stream's.
        for (int i = 0; i < Environment.ProcessorCount * 2; i++)
        {
            using var invalid = new DeflateDecoderStream(new MemoryStream(s_InvalidData));
            FluentActions.Invoking(() => invalid.ReadByte()).Should().Throw<InvalidDataException>();
        }

        byte[] content = DeflateCompressionTests.TextData(50_000, seed: 5);
        using var stream = new DeflateDecoderStream(new MemoryStream(Compress(content)));

        (await ReadAllAsync(stream, useAsync: false, 4096)).Should().Equal(content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_DisposesTheCompressedStreamAndRejectsReads(bool useAsync)
    {
        var compressed = new MemoryStream(Compress(DeflateCompressionTests.TextData(1000, seed: 6)));
        var stream = new DeflateDecoderStream(compressed);

        if (useAsync)
            await stream.DisposeAsync();
        else
            stream.Dispose();

        compressed.CanRead.Should().BeFalse();
        stream.CanRead.Should().BeFalse();
        FluentActions.Invoking(() => stream.ReadByte()).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => stream.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void UnsupportedMembers_Throw()
    {
        using var stream = new DeflateDecoderStream(new MemoryStream(Compress([1, 2, 3])));

        stream.CanSeek.Should().BeFalse();
        stream.CanWrite.Should().BeFalse();
        FluentActions.Invoking(() => stream.Length).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.Position).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.Seek(0, SeekOrigin.Begin)).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => stream.Write([1], 0, 1)).Should().Throw<NotSupportedException>();
    }
}
#endif
