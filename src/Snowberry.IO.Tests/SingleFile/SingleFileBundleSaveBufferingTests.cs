using System.Text;
using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for how saving a <see cref="SingleFileBundle"/> buffers, schedules and cleans up the work done before the bundle is written.
/// </summary>
public class SingleFileBundleSaveBufferingTests
{
    private static readonly Lazy<byte[]> s_SkewedBundle = new(BuildSkewedBundle);

    private static readonly Lazy<byte[]> s_SkewedCompressed = new(() =>
    {
        using var bundle = OpenBytes(s_SkewedBundle.Value);
        return SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Compress, MaxDegreeOfParallelism = 1 });
    });

    public static TheoryData<long, int, bool> BudgetsAndDegrees()
    {
        var data = new TheoryData<long, int, bool>();

        foreach (long budget in new[] { 0L, 16 * 1024L, 256 * 1024L, BundleWriteOptions.Default.MaxCompressionMemory })
        {
            foreach (int degree in new[] { 1, 4, -1 })
            {
                data.Add(budget, degree, false);
                data.Add(budget, degree, true);
            }
        }

        return data;
    }

    public static TheoryData<BundleCompression, bool, bool> FileSaveCases()
    {
        var data = new TheoryData<BundleCompression, bool, bool>();

        foreach (var compression in new[] { BundleCompression.Preserve, BundleCompression.Compress, BundleCompression.NoCompression })
        {
            foreach (bool modify in new[] { false, true })
            {
                data.Add(compression, modify, false);
                data.Add(compression, modify, true);
            }
        }

        return data;
    }

    public static TheoryData<bool, int, bool> SourcesAndDegrees()
    {
        var data = new TheoryData<bool, int, bool>();

        foreach (bool compressed in new[] { false, true })
        {
            foreach (int degree in new[] { 1, 4, -1 })
            {
                data.Add(compressed, degree, false);
                data.Add(compressed, degree, true);
            }
        }

        return data;
    }

    /// <summary>
    /// Builds an uncompressed version 6 bundle with one large entry, many small ones, incompressible ones and an empty one.
    /// </summary>
    private static byte[] BuildSkewedBundle()
    {
        var files = new List<BundleFile>
        {
            new("app.deps.json", TextData(3000, seed: 100), BundleFileType.DepsJson),
            new("small0.dll", TextData(700, seed: 101), BundleFileType.Assembly)
        };

        for (int i = 0; i < 24; i++)
            files.Add(new($"text{i}.txt", TextData(1000 + (i * 1700), seed: i), BundleFileType.Unknown));

        files.Add(new("big.txt", TextData(1_500_000, seed: 200), BundleFileType.Unknown));

        for (int i = 0; i < 3; i++)
            files.Add(new($"random{i}.bin", RandomData(20_000 * (i + 1), seed: 300 + i), BundleFileType.NativeBinary));

        files.Add(new("empty.txt", [], BundleFileType.Unknown));
        files.Add(new("app.runtimeconfig.json", "{\"runtimeOptions\":{\"includedFrameworks\":[]}}"u8.ToArray(), BundleFileType.RuntimeConfigJson));
        return Build(6, files);
    }

    private static Task SaveAsync(SingleFileBundle bundle, Stream output, BundleWriteOptions options, bool useAsync, CancellationToken cancellationToken)
    {
        if (useAsync)
            return bundle.SaveAsync(output, options, cancellationToken);

        bundle.Save(output, options);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates text of random words, which deflate compresses to roughly half its size.
    /// </summary>
    private static byte[] TextData(int length, int seed)
    {
        string[] words = ["bundle", "entry", "host", "manifest", "offset", "stream", "deflate", "assembly", "native", "json", "save", "open"];
        var random = new Random(seed);
        var text = new StringBuilder(length + 16);

        while (text.Length < length)
            text.Append(words[random.Next(words.Length)]).Append(random.Next(1000)).Append(' ');

        return Encoding.ASCII.GetBytes(text.ToString(0, length));
    }

    [Fact]
    public async Task SaveAsyncToFile_CanceledWhileCompressing_ThrowsAndLeavesNoFile()
    {
        string directory = NewTempDirectory();
        Directory.CreateDirectory(directory);
        using var cancellation = new CancellationTokenSource();
        var options = new BundleWriteOptions { Compression = BundleCompression.Compress, MaxCompressionMemory = 0, MaxDegreeOfParallelism = 4 };

        try
        {
            using var bundle = OpenBytes(s_SkewedBundle.Value);
            bundle.GetEntry("text3.txt").Replace(new FailingStream(TextData(200_000, seed: 3), failAt: 100_000, cancellation.Cancel));

            (await FluentActions.Awaiting(() => bundle.SaveAsync(Path.Combine(directory, "app.exe"), options, cancellation.Token))
                .Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);

            Directory.GetFiles(directory).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAsync_CanceledWhileWriting_ThrowsAndLaterSavesWriteSameBytes()
    {
        using var bundle = OpenBytes(s_SkewedCompressed.Value);
        bundle.GetEntry("text2.txt").Replace(TextData(5000, seed: 77));
        using var cancellation = new CancellationTokenSource();
        var options = new BundleWriteOptions { MaxDegreeOfParallelism = 4 };

        (await FluentActions.Awaiting(() => bundle.SaveAsync(new CancelingStream(cancellation), options, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);

        using var output = new MemoryStream();
        await bundle.SaveAsync(output, options, TestContext.Current.CancellationToken);
        output.ToArray().Should().Equal(SaveToBytes(bundle, options with { MaxDegreeOfParallelism = 1 }));
    }

    [Fact]
    public async Task SaveAsync_NoCompressionCanceledWhileWriting_ThrowsAndLaterSavesWriteSameBytes()
    {
        using var bundle = OpenBytes(s_SkewedCompressed.Value);
        using var cancellation = new CancellationTokenSource();
        var options = new BundleWriteOptions { Compression = BundleCompression.NoCompression, MaxDegreeOfParallelism = 4 };

        (await FluentActions.Awaiting(() => bundle.SaveAsync(new CancelingStream(cancellation), options, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);

        using var output = new MemoryStream();
        await bundle.SaveAsync(output, options, TestContext.Current.CancellationToken);
        output.ToArray().Should().Equal(s_SkewedBundle.Value);
    }

    [Theory]
    [MemberData(nameof(FileSaveCases))]
    public async Task SaveToFile_PreallocatedFile_HasSameBytesAsStream(BundleCompression compression, bool modify, bool useAsync)
    {
        string directory = NewTempDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "app.exe");
        var options = new BundleWriteOptions { Compression = compression };

        try
        {
            using var bundle = OpenBytes(s_SkewedCompressed.Value);

            if (modify)
                bundle.GetEntry("text5.txt").Replace(TextData(70_000, seed: 55));

            byte[] expected = SaveToBytes(bundle, options);

            if (useAsync)
                await bundle.SaveAsync(path, options, TestContext.Current.CancellationToken);
            else
                bundle.Save(path, options);

            File.ReadAllBytes(path).Should().Equal(expected);
            Directory.GetFiles(directory).Should().HaveCount(1);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(-1, true)]
    public async Task Save_ChangedContentAndCorruptCompressedEntry_ThrowsInvalidDataException(int degree, bool useAsync)
    {
        // The corrupt entry is copied as stored, so only hashing it for the new identifier decompresses it.
        byte[] corrupt = PatchEntrySize((byte[])s_SkewedCompressed.Value.Clone(), "text7.txt", 1000);
        using var bundle = OpenBytes(corrupt);
        bundle.GetEntry("text2.txt").Replace(TextData(5000, seed: 77));

        await FluentActions.Awaiting(() => SaveAsync(bundle, new MemoryStream(), new BundleWriteOptions { MaxDegreeOfParallelism = degree }, useAsync, TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ChangedContent_HashesStoredEntriesWhileCompressing(bool useAsync)
    {
        // The replaced entry is compressed; its content only becomes readable once the bundle is read for hashing, which would never
        // happen if hashing waited for the compression.
        using var hashing = new ManualResetEventSlim();
        var source = new SignalingStream(s_SkewedCompressed.Value, hashing);
        using var bundle = SingleFileBundle.Open(source);
        source.Armed = true;
        bundle.GetEntry("text2.txt").Replace(new FailingStream(TextData(50_000, seed: 77), failAt: 0, () =>
        {
            if (!hashing.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The stored entries were not hashed while the replaced entry was compressed.");
        }));

        using var output = new MemoryStream();
        await SaveAsync(bundle, output, new BundleWriteOptions { MaxDegreeOfParallelism = 4 }, useAsync, TestContext.Current.CancellationToken);

        using var reopened = OpenBytes(output.ToArray());
        reopened.GetEntry("text2.txt").IsCompressed.Should().BeTrue();
        reopened.GetEntry("text2.txt").ReadAllBytes().Should().Equal(TextData(50_000, seed: 77));
    }

    [Theory]
    [MemberData(nameof(SourcesAndDegrees))]
    public async Task Save_ChangedContent_WritesSameBytesForEveryDegree(bool compressed, int degree, bool useAsync)
    {
        byte[] original = compressed ? s_SkewedCompressed.Value : s_SkewedBundle.Value;
        byte[] expected = SaveChanged(original, new BundleWriteOptions { MaxDegreeOfParallelism = 1 });

        // A new bundle has no cached hashes, so every entry copied as stored is hashed again.
        using var bundle = OpenBytes(original);
        bundle.GetEntry("text2.txt").Replace(TextData(5000, seed: 77));
        using var output = new MemoryStream();
        await SaveAsync(bundle, output, new BundleWriteOptions { MaxDegreeOfParallelism = degree }, useAsync, TestContext.Current.CancellationToken);

        output.ToArray().Should().Equal(expected);

        using var saved = OpenBytes(expected);
        saved.Header.Id.Should().NotBe(bundle.Header.Id);

        static byte[] SaveChanged(byte[] bytes, BundleWriteOptions options)
        {
            using var serial = OpenBytes(bytes);
            serial.GetEntry("text2.txt").Replace(TextData(5000, seed: 77));
            return SaveToBytes(serial, options);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_CompressAgain_SkipsEntriesThatDidNotBeatTheThreshold(bool useAsync)
    {
        const int c_RandomLength = 200_000;
        byte[] bytes = Build(6,
        [
            new("random0.bin", RandomData(c_RandomLength, seed: 1), BundleFileType.NativeBinary),
            new("random1.bin", RandomData(c_RandomLength, seed: 2), BundleFileType.NativeBinary),
            new("text.txt", TextData(50_000, seed: 3), BundleFileType.Unknown)
        ]);
        var source = new CountingReadStream(bytes);
        using var bundle = SingleFileBundle.Open(source);
        var options = new BundleWriteOptions { Compression = BundleCompression.Compress };

        long first = await BytesReadWhileSavingAsync(options);
        byte[] firstOutput = SaveToBytes(bundle, options);
        long second = await BytesReadWhileSavingAsync(options);

        // Both incompressible entries are compressed and copied the first time, and only copied after that.
        (first - second).Should().Be(2 * c_RandomLength);
        SaveToBytes(bundle, options).Should().Equal(firstOutput);

        // The cached result only applies to the same level, and a higher threshold keeps the compressed data.
        (await BytesReadWhileSavingAsync(options with { CompressionLevel = System.IO.Compression.CompressionLevel.Fastest })).Should().Be(first);

        using var reopened = OpenBytes(SaveToBytes(bundle, options with { CompressionThreshold = double.PositiveInfinity }));
        reopened.GetEntry("random0.bin").IsCompressed.Should().BeTrue();
        reopened.GetEntry("random1.bin").ReadAllBytes().Should().Equal(RandomData(c_RandomLength, seed: 2));

        async Task<long> BytesReadWhileSavingAsync(BundleWriteOptions saveOptions)
        {
            long before = source.BytesRead;
            await SaveAsync(bundle, Stream.Null, saveOptions, useAsync, TestContext.Current.CancellationToken);
            return source.BytesRead - before;
        }
    }

    [Fact]
    public void Save_CompressSkewedEntries_RoundTripsContent()
    {
        using var original = OpenBytes(s_SkewedBundle.Value);
        using var reopened = OpenBytes(s_SkewedCompressed.Value);

        reopened.Entries.Select(e => e.RelativePath).Should().Equal(original.Entries.Select(e => e.RelativePath));

        foreach (var entry in original.Entries)
            reopened.GetEntry(entry.RelativePath).ReadAllBytes().Should().Equal(entry.ReadAllBytes(), entry.RelativePath);

        reopened.GetEntry("big.txt").IsCompressed.Should().BeTrue();
        reopened.GetEntry("random0.bin").IsCompressed.Should().BeFalse();
        reopened.GetEntry("empty.txt").IsCompressed.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(BudgetsAndDegrees))]
    public async Task Save_CompressSkewedEntries_WritesSameBytesForEveryBudgetAndDegree(long maxCompressionMemory, int degree, bool useAsync)
    {
        var options = new BundleWriteOptions { Compression = BundleCompression.Compress, MaxCompressionMemory = maxCompressionMemory, MaxDegreeOfParallelism = degree };
        using var bundle = OpenBytes(s_SkewedBundle.Value);
        using var output = new MemoryStream();

        if (useAsync)
            await bundle.SaveAsync(output, options, TestContext.Current.CancellationToken);
        else
            bundle.Save(output, options);

        output.ToArray().Should().Equal(s_SkewedCompressed.Value);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(-1, true)]
    public async Task Save_ContentStreamFailsWhileCompressing_ThrowsAndLaterSavesSucceed(int degree, bool useAsync)
    {
        var options = new BundleWriteOptions { Compression = BundleCompression.Compress, MaxCompressionMemory = 0, MaxDegreeOfParallelism = degree };
        using var bundle = OpenBytes(s_SkewedBundle.Value);
        var entry = bundle.GetEntry("text3.txt");
        entry.Replace(new FailingStream(TextData(200_000, seed: 3), failAt: 100_000, () => throw new IOException("Simulated read failure.")));

        var save = () => SaveAsync(bundle, new MemoryStream(), options, useAsync, TestContext.Current.CancellationToken);
        (await save.Should().ThrowAsync<IOException>()).Which.Message.Should().Be("Simulated read failure.");

        entry.Revert();
        using var output = new MemoryStream();
        await SaveAsync(bundle, output, options, useAsync, TestContext.Current.CancellationToken);
        output.ToArray().Should().Equal(s_SkewedCompressed.Value);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(-1, true)]
    public async Task Save_NoCompressionWithChangedContent_WritesSameBytesAsSerialSave(int degree, bool useAsync)
    {
        // Entries larger than the budget are decompressed by the writer itself, the others ahead of it.
        var options = new BundleWriteOptions { Compression = BundleCompression.NoCompression, MaxCompressionMemory = 100 * 1024, MaxDegreeOfParallelism = degree };
        byte[] expected = Save(options with { MaxDegreeOfParallelism = 1, MaxCompressionMemory = 0 });

        using var bundle = OpenBytes(s_SkewedCompressed.Value);
        bundle.GetEntry("text4.txt").Replace(TextData(9000, seed: 44));
        using var output = new MemoryStream();
        await SaveAsync(bundle, output, options, useAsync, TestContext.Current.CancellationToken);

        output.ToArray().Should().Equal(expected);

        static byte[] Save(BundleWriteOptions options)
        {
            using var serial = OpenBytes(s_SkewedCompressed.Value);
            serial.GetEntry("text4.txt").Replace(TextData(9000, seed: 44));
            return SaveToBytes(serial, options);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_NoCompressionWithCompressedEntryLongerThanDeclared_ThrowsInvalidDataException(bool useAsync)
    {
        byte[] bytes = Build(6,
        [
            new("a.txt", TextData(300_000, seed: 1), BundleFileType.Unknown, Compress: true),
            new("b.txt", TextData(200_000, seed: 2), BundleFileType.Unknown, Compress: true)
        ]);
        using var bundle = OpenBytes(PatchEntrySize(bytes, "b.txt", 150_000));
        var options = new BundleWriteOptions { Compression = BundleCompression.NoCompression };

        await FluentActions.Awaiting(() => SaveAsync(bundle, new MemoryStream(), options, useAsync, TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(-1, false)]
    public async Task Save_NoCompressionWithCorruptEntryAmongMany_ThrowsInvalidDataExceptionAndLaterSavesSucceed(int degree, bool useAsync)
    {
        byte[] corrupt = PatchEntrySize((byte[])s_SkewedCompressed.Value.Clone(), "text20.txt", 1000);
        var options = new BundleWriteOptions { Compression = BundleCompression.NoCompression, MaxDegreeOfParallelism = degree };

        using var bundle = OpenBytes(corrupt);
        await FluentActions.Awaiting(() => SaveAsync(bundle, new MemoryStream(), options, useAsync, TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<InvalidDataException>();

        bundle.GetEntry("text20.txt").Replace(TextData(1000, seed: 20));
        await SaveAsync(bundle, new MemoryStream(), options, useAsync, TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(BudgetsAndDegrees))]
    public async Task Save_NoCompression_WritesSameBytesForEveryBudgetAndDegree(long maxCompressionMemory, int degree, bool useAsync)
    {
        var options = new BundleWriteOptions { Compression = BundleCompression.NoCompression, MaxCompressionMemory = maxCompressionMemory, MaxDegreeOfParallelism = degree };
        using var bundle = OpenBytes(s_SkewedCompressed.Value);
        using var output = new MemoryStream();

        await SaveAsync(bundle, output, options, useAsync, TestContext.Current.CancellationToken);

        // Decompressing every entry restores the uncompressed bundle the compressed one was saved from.
        output.ToArray().Should().Equal(s_SkewedBundle.Value);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    public async Task Save_ReplacedWithStream_ReadsTheStreamOnce(int degree, bool useAsync)
    {
        // The identifier is hashed from the same pass that copies the content, so it matches the bytes written even if the stream changes.
        byte[] content = RandomData(300_000, seed: 12);
        var stream = new CountingReadStream(content);
        using var bundle = OpenBytes(s_SkewedBundle.Value);
        bundle.GetEntry("random1.bin").Replace(stream, leaveOpen: true);
        using var output = new MemoryStream();

        await SaveAsync(bundle, output, new BundleWriteOptions { MaxDegreeOfParallelism = degree }, useAsync, TestContext.Current.CancellationToken);

        stream.BytesRead.Should().Be(content.Length);

        using var reopened = OpenBytes(output.ToArray());
        reopened.GetEntry("random1.bin").ReadAllBytes().Should().Equal(content);
    }

    /// <summary>
    /// A write-only stream that cancels <paramref name="cancellation"/> on its first write and discards the data.
    /// </summary>
    private sealed class CancelingStream(CancellationTokenSource cancellation) : Stream
    {
        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            cancellation.Cancel();
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }

    /// <summary>
    /// A seekable stream over <paramref name="data"/> that runs <paramref name="onFailure"/> once a read reaches <paramref name="failAt"/>.
    /// </summary>
    private sealed class FailingStream(byte[] data, int failAt, Action onFailure) : Stream
    {
        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (Position >= failAt)
                onFailure();

            int count = (int)Math.Min(buffer.Length, Math.Max(0, data.Length - Position));
            data.AsSpan((int)Position, count).CopyTo(buffer);
            Position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(Read(buffer.Span));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                _ => data.Length + offset
            };

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position { get; set; }
    }

    /// <summary>
    /// A seekable stream over <paramref name="data"/> that sets <paramref name="read"/> on the first read once it is <see cref="Armed"/>.
    /// </summary>
    private sealed class SignalingStream(byte[] data, ManualResetEventSlim read) : Stream
    {
        private readonly MemoryStream _inner = new(data, writable: false);

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (Armed)
                read.Set();

            return _inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(Read(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public bool Armed { get; set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }
    }
}