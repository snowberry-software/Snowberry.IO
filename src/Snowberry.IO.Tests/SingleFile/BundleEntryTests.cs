using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for reading, adding and extracting a single <see cref="BundleEntry"/>.
/// </summary>
public class BundleEntryTests
{
    private static readonly byte[] s_Assembly = RandomData(300, seed: 7);

    private static byte[] Sample()
    {
        return Build(6,
        [
            new("app.dll", s_Assembly, BundleFileType.Assembly),
            new("lib/data.bin", new byte[2000], BundleFileType.Unknown, Compress: true)
        ]);
    }

    [Fact]
    public void Add_NonSeekableStream_BuffersAndDisposesStream()
    {
        using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));
        var content = new NonSeekableMemoryStream("streamed"u8.ToArray());

        var entry = bundle.Add("streamed.bin", BundleFileType.Unknown, content);

        content.CanRead.Should().BeFalse();
        entry.ReadAllBytes().Should().Equal("streamed"u8.ToArray());
    }

    [Fact]
    public void Add_NullOrDisposedStream_Throws()
    {
        using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));
        var unreadable = new MemoryStream();
        unreadable.Dispose();

        FluentActions.Invoking(() => bundle.Add("x.bin", BundleFileType.Unknown, (Stream)null!)).Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => bundle.Add("x.bin", BundleFileType.Unknown, unreadable)).Should().ThrowExactly<ArgumentException>();
        bundle.TryGetEntry("x.bin", out _).Should().BeFalse();
    }

    [Fact]
    public void Add_SeekableStream_IsReadFromPositionAndHonorsLeaveOpen()
    {
        using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));
        var kept = new MemoryStream("xxpayload"u8.ToArray()) { Position = 2 };
        var owned = new MemoryStream("owned"u8.ToArray());

        var entry = bundle.Add("plugins/kept.bin", BundleFileType.Unknown, kept, leaveOpen: true);
        bundle.Add("plugins/owned.bin", BundleFileType.Unknown, owned);

        entry.IsNew.Should().BeTrue();
        entry.Size.Should().Be(7);

        using var output = new MemoryStream();
        bundle.Save(output);

        using (var saved = SingleFileBundle.Open(new MemoryStream(output.ToArray(), writable: false)))
        {
            saved.GetEntry("plugins/kept.bin").ReadAllBytes().Should().Equal("payload"u8.ToArray());
            saved.GetEntry("plugins/owned.bin").ReadAllBytes().Should().Equal("owned"u8.ToArray());
        }

        bundle.Remove(entry).Should().BeTrue();
        bundle.Remove("plugins/owned.bin").Should().BeTrue();
        kept.CanRead.Should().BeTrue();
        owned.CanRead.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_NestedEntry_CreatesSubdirectoriesAndReturnsPath()
    {
        string directory = NewTempDirectory();
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));

            string path = await bundle.GetEntry("lib/data.bin").ExtractToDirectoryAsync(directory, cancellationToken: cancellationToken);

            path.Should().Be(Path.Combine(Path.GetFullPath(directory), "lib", "data.bin"));
            (await File.ReadAllBytesAsync(path, cancellationToken)).Should().Equal(new byte[2000]);

            await FluentActions.Awaiting(() => bundle.GetEntry("lib/data.bin").ExtractToDirectoryAsync(directory, cancellationToken: cancellationToken))
                .Should().ThrowExactlyAsync<IOException>();
            await bundle.GetEntry("lib/data.bin").ExtractToDirectoryAsync(directory, overwrite: true, cancellationToken);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ExtractToDirectoryAsync_UnsafePath_ThrowsBeforeWriting()
    {
        string directory = NewTempDirectory();
        byte[] bytes = Build(6, [new("../evil.txt", [1], BundleFileType.Unknown)]);
        using var bundle = SingleFileBundle.Open(new MemoryStream(bytes, writable: false));

        await FluentActions.Awaiting(() => bundle.GetEntry("../evil.txt").ExtractToDirectoryAsync(directory, cancellationToken: TestContext.Current.CancellationToken))
            .Should().ThrowExactlyAsync<InvalidDataException>();
        Directory.Exists(directory).Should().BeFalse();
    }

    [Fact]
    public void ExtractToDirectory_NestedEntry_WritesOneFileAndReturnsPath()
    {
        string directory = NewTempDirectory();

        try
        {
            using var bundle = OpenBytes(SampleV6());

            string path = bundle.GetEntry("lib/native.dll").ExtractToDirectory(directory);

            path.Should().Be(Path.Combine(Path.GetFullPath(directory), "lib", "native.dll"));
            File.ReadAllBytes(path).Should().Equal(s_Compressible);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ExtractToFileAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        string directory = NewTempDirectory();
        Directory.CreateDirectory(directory);

        try
        {
            using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));

            await FluentActions.Awaiting(() => bundle.GetEntry("app.dll").ExtractToFileAsync(Path.Combine(directory, "app.dll"), cancellationToken: new CancellationToken(canceled: true)))
                .Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ExtractToFileAsync_ExistingFile_ThrowsUnlessOverwrite()
    {
        string directory = NewTempDirectory();
        string path = Path.Combine(directory, "app.dll");
        Directory.CreateDirectory(directory);
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await using var bundle = await SingleFileBundle.OpenAsync(new MemoryStream(Sample(), writable: false), cancellationToken: cancellationToken);
            var entry = bundle.GetEntry("app.dll");

            await entry.ExtractToFileAsync(path, cancellationToken: cancellationToken);
            (await File.ReadAllBytesAsync(path, cancellationToken)).Should().Equal(s_Assembly);

            await FluentActions.Awaiting(() => entry.ExtractToFileAsync(path, cancellationToken: cancellationToken)).Should().ThrowExactlyAsync<IOException>();

            await bundle.GetEntry("lib/data.bin").ExtractToFileAsync(path, overwrite: true, cancellationToken);
            (await File.ReadAllBytesAsync(path, cancellationToken)).Should().Equal(new byte[2000]);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData(10L, false)]
    [InlineData(10L, true)]
    [InlineData(2001L, false)]
    [InlineData(2001L, true)]
    public async Task ExtractToFile_CompressedEntryNotMatchingDeclaredSize_ThrowsInvalidDataException(long declaredSize, bool useAsync)
    {
        string directory = NewTempDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "data.bin");

        try
        {
            using var bundle = SingleFileBundle.Open(new MemoryStream(PatchEntrySize(Sample(), "lib/data.bin", declaredSize), writable: false));
            var entry = bundle.GetEntry("lib/data.bin");

            if (useAsync)
                await FluentActions.Awaiting(() => entry.ExtractToFileAsync(path, cancellationToken: TestContext.Current.CancellationToken)).Should().ThrowExactlyAsync<InvalidDataException>();
            else
                FluentActions.Invoking(() => entry.ExtractToFile(path)).Should().ThrowExactly<InvalidDataException>();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ExtractToFile_ExistingFile_ThrowsUnlessOverwrite()
    {
        string directory = NewTempDirectory();
        string path = Path.Combine(directory, "data.bin");
        Directory.CreateDirectory(directory);

        try
        {
            using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));
            var entry = bundle.GetEntry("lib/data.bin");

            entry.ExtractToFile(path);
            File.ReadAllBytes(path).Should().Equal(new byte[2000]);

            FluentActions.Invoking(() => entry.ExtractToFile(path)).Should().ThrowExactly<IOException>();

            bundle.GetEntry("app.dll").ExtractToFile(path, overwrite: true);
            File.ReadAllBytes(path).Should().Equal(s_Assembly);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData("app.dll", false)]
    [InlineData("app.dll", true)]
    [InlineData("lib/data.bin", false)]
    [InlineData("lib/data.bin", true)]
    public async Task ExtractToFile_OverwriteLargerFile_WritesExactContent(string relativePath, bool useAsync)
    {
        string directory = NewTempDirectory();
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "out.bin");
        File.WriteAllBytes(path, RandomData(100_000, seed: 3));

        try
        {
            using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));
            var entry = bundle.GetEntry(relativePath);

            if (useAsync)
                await entry.ExtractToFileAsync(path, overwrite: true, TestContext.Current.CancellationToken);
            else
                entry.ExtractToFile(path, overwrite: true);

            File.ReadAllBytes(path).Should().Equal(entry.ReadAllBytes());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ExtractToFile_RemovedEntryOrEmptyPath_Throws()
    {
        using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));
        var entry = bundle.GetEntry("app.dll");
        bundle.Remove(entry);

        FluentActions.Invoking(() => entry.ExtractToFile(Path.Combine(NewTempDirectory(), "app.dll"))).Should().ThrowExactly<InvalidOperationException>();
        FluentActions.Invoking(() => entry.ExtractToFile(string.Empty)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Open_AddedStreamThatShrank_ReadThrowsIOException()
    {
        using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));
        var content = new MemoryStream(new byte[100]);
        var entry = bundle.Add("added.bin", BundleFileType.Unknown, content, leaveOpen: true);

        content.SetLength(40);

        using var stream = entry.Open();
        FluentActions.Invoking(() => stream.CopyTo(Stream.Null)).Should().ThrowExactly<IOException>();
    }

    [Fact]
    public async Task Open_TruncatedSource_ReadAsyncThrowsInvalidDataException()
    {
        var source = new MemoryStream();
        source.Write(Sample());
        using var bundle = SingleFileBundle.Open(source, leaveOpen: true);
        var entry = bundle.GetEntry("app.dll");

        source.SetLength(entry.Offset + 100);

        await using var stream = entry.Open();
        await FluentActions.Awaiting(() => stream.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken)).Should().ThrowExactlyAsync<InvalidDataException>();
    }

    [Fact]
    public void Open_TruncatedSource_ReadThrowsInvalidDataException()
    {
        var source = new MemoryStream();
        source.Write(Sample());
        using var bundle = SingleFileBundle.Open(source, leaveOpen: true);
        var entry = bundle.GetEntry("app.dll");

        source.SetLength(entry.Offset + 100);

        using var stream = entry.Open();
        var ex = FluentActions.Invoking(() => stream.CopyTo(Stream.Null)).Should().ThrowExactly<InvalidDataException>().Which;
        ex.Message.Should().Contain("app.dll");
    }

    [Fact]
    public void Open_UncompressedEntry_ReadsContentThenReturnsZero()
    {
        using var bundle = SingleFileBundle.Open(new MemoryStream(Sample(), writable: false));
        using var stream = bundle.GetEntry("app.dll").Open();
        using var copy = new MemoryStream();

        stream.CopyTo(copy);

        copy.ToArray().Should().Equal(s_Assembly);
        stream.Read(new byte[10], 0, 10).Should().Be(0);
    }
}