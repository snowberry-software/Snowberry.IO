using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for opening a <see cref="SingleFileBundle"/> and reading its entries.
/// </summary>
public class SingleFileBundleTests
{
    [Fact]
    public async Task Dispose_ThenUseBundleEntryOrStream_ThrowsObjectDisposedException()
    {
        var bundle = OpenBytes(SampleV6());
        var entry = bundle.GetEntry("app.dll");
        var stream = entry.Open();

        await bundle.DisposeAsync();
        bundle.Dispose();

        FluentActions.Invoking(() => bundle.GetEntry("app.dll")).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => entry.Open()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => bundle.Save(new MemoryStream())).Should().ThrowExactly<ObjectDisposedException>();
        var ex = FluentActions.Invoking(() => stream.ReadByte()).Should().ThrowExactly<ObjectDisposedException>().Which;
        ex.ObjectName.Should().Contain(nameof(SingleFileBundle));
    }

    [Fact]
    public void EntryOpen_CompressedEntry_DecompressesWhileReading()
    {
        using var bundle = OpenBytes(SampleV6());
        using var stream = bundle.GetEntry("lib/native.dll").Open();
        using var copy = new MemoryStream();

        stream.CanSeek.Should().BeFalse();
        stream.Length.Should().Be(s_Compressible.Length);

        stream.CopyTo(copy);
        copy.ToArray().Should().Equal(s_Compressible);
    }

    [Fact]
    public void EntryOpen_UncompressedEntry_ReturnsSeekableStream()
    {
        using var bundle = OpenBytes(SampleV6());
        using var stream = bundle.GetEntry("app.dll").Open();

        stream.CanSeek.Should().BeTrue();
        stream.Length.Should().Be(300);

        stream.Position = 100;
        byte[] buffer = new byte[10];
        stream.ReadExactly(buffer);
        buffer.Should().Equal(RandomData(300).AsSpan(100, 10).ToArray());

        FluentActions.Invoking(() => stream.Seek(-1, SeekOrigin.Begin)).Should().ThrowExactly<IOException>();
    }

    [Fact]
    public void GetEntry_MissingPath_ThrowsAndTryGetEntryIsCaseSensitive()
    {
        using var bundle = OpenBytes(SampleV6());

        FluentActions.Invoking(() => bundle.GetEntry("missing")).Should().ThrowExactly<KeyNotFoundException>();
        bundle.TryGetEntry("APP.DLL", out _).Should().BeFalse();
        bundle.TryGetEntry("app.dll", out var entry).Should().BeTrue();
        entry!.ToString().Should().Be("app.dll");
    }

    [Fact]
    public async Task OpenAsync_File_ReadsEntries()
    {
        string path = WriteTempFile(SampleV6());

        try
        {
            await using var bundle = await SingleFileBundle.OpenAsync(path, TestContext.Current.CancellationToken);

            (await bundle.GetEntry("lib/native.dll").ReadAllBytesAsync(TestContext.Current.CancellationToken)).Should().Equal(s_Compressible);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_DuplicatePaths_ThrowsInvalidDataException()
    {
        byte[] bytes = Build(6, [new("same", [1], BundleFileType.Unknown), new("same", [2], BundleFileType.Unknown)]);

        FluentActions.Invoking(() => OpenBytes(bytes)).Should().ThrowExactly<InvalidDataException>();
    }

    [Theory]
    [InlineData(TestHost.Elf)]
    [InlineData(TestHost.MachO)]
    public void Open_ElfOrMachOHost_ReadsContent(TestHost host)
    {
        using var bundle = OpenBytes(SampleV6(host));

        bundle.GetEntry("lib/native.dll").ReadAllBytes().Should().Equal(s_Compressible);
    }

    [Fact]
    public void Open_EntryInsideHostImage_ThrowsInvalidDataException()
    {
        using (OpenBytes(BuildRaw(6, [new(c_HostLength, 10, 0, BundleFileType.Unknown, "x")], dataLength: 10, host: TestHost.Elf)))
        {
        }

        // The PE image ends at 400, but data must also start after the bundle signature, which is inside the host.
        FluentActions.Invoking(() => OpenBytes(BuildRaw(6, [new(c_PeImageEnd - 10, 10, 0, BundleFileType.Unknown, "x")], dataLength: 10))).Should().ThrowExactly<InvalidDataException>();
        FluentActions.Invoking(() => OpenBytes(BuildRaw(6, [new(c_ElfImageEnd - 10, 10, 0, BundleFileType.Unknown, "x")], dataLength: 10, host: TestHost.Elf))).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void Open_FileCountLargerThanManifest_ThrowsInvalidDataException()
    {
        FluentActions.Invoking(() => OpenBytes(BuildRaw(6, [], fileCountOverride: 1_000_000))).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void Open_FileWithoutSignature_ThrowsAndTryOpenReturnsFalse()
    {
        string path = WriteTempFile(new byte[4096]);

        try
        {
            var ex = FluentActions.Invoking(() => SingleFileBundle.Open(path)).Should().ThrowExactly<InvalidDataException>().Which;
            ex.Message.Should().Contain("signature");

            SingleFileBundle.TryOpen(path, out var bundle).Should().BeFalse();
            bundle.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-1L, 10L, 0L)]
    [InlineData(c_ManifestOffsetField, 5L, 0L)] // inside the application host, before the end of the signature
    [InlineData(c_HostLength, -5L, 0L)]
    [InlineData(c_HostLength, 10L, -1L)]
    [InlineData(c_HostLength, 1_000_000L, 0L)] // extends past the manifest
    [InlineData(c_HostLength, 10L, 1_000_000L)]
    [InlineData(long.MaxValue, 10L, 0L)]
    public void Open_InvalidEntryLocation_ThrowsInvalidDataException(long offset, long size, long compressedSize)
    {
        FluentActions.Invoking(() => OpenBytes(BuildRaw(6, [new(offset, size, compressedSize, BundleFileType.Unknown, "x")], dataLength: 100))).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void Open_InvalidOwnedStream_ThrowsAndDisposesStream()
    {
        var stream = new MemoryStream(new byte[1000]);

        FluentActions.Invoking(() => SingleFileBundle.Open(stream)).Should().ThrowExactly<InvalidDataException>();
        stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public void Open_NonSeekableStream_ThrowsArgumentException()
    {
        FluentActions.Invoking(() => SingleFileBundle.Open(new NonSeekableMemoryStream(SampleV6()))).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Open_SampleBundle_ReadsHeaderAndEntries()
    {
        using var bundle = OpenBytes(SampleV6());

        bundle.Header.Should().Be(new BundleHeader(6, 0, "originalId00", BundleFlags.NetCoreApp3CompatMode));
        bundle.Header.SupportsCompression.Should().BeTrue();
        bundle.Entries.Select(e => e.RelativePath).Should().Equal(["app.dll", "app.deps.json", "app.runtimeconfig.json", "lib/native.dll"]);
        (bundle.DepsJson?.RelativePath).Should().Be("app.deps.json");
        (bundle.RuntimeConfigJson?.RelativePath).Should().Be("app.runtimeconfig.json");
        bundle.IsModified.Should().BeFalse();

        var native = bundle.GetEntry("lib/native.dll");
        native.IsCompressed.Should().BeTrue();
        native.Size.Should().Be(s_Compressible.Length);
        native.Type.Should().Be(BundleFileType.NativeBinary);
        native.Offset.Should().BeGreaterThanOrEqualTo(c_HostLength);
    }

    [Fact]
    public void Open_SecondTime_AllocatesNoLargeBuffers()
    {
        byte[] bytes = SampleV6();
        OpenBytes(bytes).Dispose();

        long before = GC.GetAllocatedBytesForCurrentThread();
        OpenBytes(bytes).Dispose();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // The host header and the manifest are read into pooled buffers.
        allocated.Should().BeLessThan(32 * 1024);
    }

    [Fact]
    public void Open_SizeBeyondDeflateRatio_ThrowsNamingEntry()
    {
        byte[] bytes = Build(6, [new("bomb.bin", s_Compressible, BundleFileType.Unknown, Compress: true)]);
        bytes = PatchEntrySize(bytes, "bomb.bin", 2_000_000_000);

        var ex = FluentActions.Invoking(() => OpenBytes(bytes)).Should().ThrowExactly<InvalidDataException>().Which;
        ex.Message.Should().Contain("bomb.bin");
    }

    [Fact]
    public async Task Open_Stream_DisposesStreamUnlessLeaveOpen()
    {
        var kept = new MemoryStream(SampleV6());
        SingleFileBundle.Open(kept, leaveOpen: true).Dispose();
        kept.CanRead.Should().BeTrue();

        var owned = new MemoryStream(SampleV6());
        SingleFileBundle.Open(owned).Dispose();
        owned.CanRead.Should().BeFalse();

        var ownedAsync = new MemoryStream(SampleV6());
        await (await SingleFileBundle.OpenAsync(ownedAsync, cancellationToken: TestContext.Current.CancellationToken)).DisposeAsync();
        ownedAsync.CanRead.Should().BeFalse();
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(6u)]
    public void Open_SupportedVersion_ReadsContent(uint majorVersion)
    {
        byte[] data = RandomData(1000, (int)majorVersion);
        using var bundle = OpenBytes(Build(majorVersion, [new("a.bin", data, BundleFileType.Unknown)]));

        bundle.Header.MajorVersion.Should().Be(majorVersion);
        bundle.GetEntry("a.bin").ReadAllBytes().Should().Equal(data);
    }

    [Fact]
    public void Open_TrailingDataAfterManifest_IgnoresTrailingData()
    {
        byte[] bytes = [.. SampleV6(), .. new byte[200_000]];

        using var bundle = OpenBytes(bytes);

        bundle.Entries.Count.Should().Be(4);
    }

    [Fact]
    public void Open_UniversalMachOHost_ThrowsInvalidDataException()
    {
        FluentActions.Invoking(() => OpenBytes(SampleV6(TestHost.UniversalMachO))).Should().ThrowExactly<InvalidDataException>();
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3u)]
    [InlineData(5u)]
    [InlineData(7u)]
    public void Open_UnsupportedVersion_ThrowsInvalidDataException(uint majorVersion)
    {
        FluentActions.Invoking(() => OpenBytes(BuildRaw(majorVersion, []))).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void ReadAllBytesAndOpen_UncompressedEntry_ReadStraightFromTheSource()
    {
        byte[] data = RandomData(1_000_000, seed: 3);
        var source = new CountingReadStream(Build(6, [new("big.bin", data, BundleFileType.Unknown)]));
        using var bundle = SingleFileBundle.Open(source);
        var entry = bundle.GetEntry("big.bin");

        int reads = source.Reads;
        long bytes = source.BytesRead;
        entry.ReadAllBytes().Should().Equal(data);

        (source.Reads - reads).Should().Be(1);
        (source.BytesRead - bytes).Should().Be(data.Length);

        reads = source.Reads;
        using var stream = entry.Open();
        byte[] buffer = new byte[300_000];
        stream.ReadExactly(buffer);

        (source.Reads - reads).Should().Be(1);
        buffer.Should().Equal(data.AsSpan(0, buffer.Length).ToArray());
    }

    [Fact]
    public void ReadAllBytes_CompressedEntryLargerThanDeclared_ThrowsInvalidDataException()
    {
        byte[] bytes = Build(6, [new("big.bin", s_Compressible, BundleFileType.Unknown, Compress: true)]);
        bytes = PatchEntrySize(bytes, "big.bin", 10);

        using var bundle = OpenBytes(bytes);

        FluentActions.Invoking(() => bundle.GetEntry("big.bin").ReadAllBytes()).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void ReadAllBytes_CompressedEntrySmallerThanDeclared_ThrowsInvalidDataException()
    {
        byte[] bytes = Build(6, [new("short.bin", s_Compressible, BundleFileType.Unknown, Compress: true)]);
        bytes = PatchEntrySize(bytes, "short.bin", s_Compressible.Length + 1);

        using var bundle = OpenBytes(bytes);

        FluentActions.Invoking(() => bundle.GetEntry("short.bin").ReadAllBytes()).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void ReadAllBytes_ConcurrentReadsFromFileBundle_ReturnEachContent()
    {
        var files = Enumerable.Range(0, 32)
            .Select(i => new BundleFile($"f{i}.bin", RandomData(50_000, i), BundleFileType.Unknown, Compress: i % 2 == 0))
            .ToList();
        string path = WriteTempFile(Build(6, files));

        try
        {
            using var bundle = SingleFileBundle.Open(path);

            Parallel.For(0, 200, i =>
            {
                var file = files[i % files.Count];
                bundle.GetEntry(file.RelativePath).ReadAllBytes().Should().Equal(file.Data);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(3 * 1024 * 1024)] // deflates far below an eighth of its size, so the array grows
    [InlineData(1500)]
    public void ReadAllBytes_HighlyCompressedEntry_ReturnsContent(int length)
    {
        byte[] data = new byte[length];
        data[^1] = 7;
        using var bundle = OpenBytes(Build(6, [new("zeros.bin", data, BundleFileType.Unknown, Compress: true)]));

        bundle.GetEntry("zeros.bin").ReadAllBytes().Should().Equal(data);
    }
}