using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for saving a <see cref="SingleFileBundle"/>.
/// </summary>
public class SingleFileBundleSaveTests
{
    [Fact]
    public void BundleWriteOptions_InvalidValue_ThrowsWhenSet()
    {
        FluentActions.Invoking(() => new BundleWriteOptions { CompressionThreshold = 0 }).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BundleWriteOptions { AssemblyAlignment = 3 }).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BundleWriteOptions { Compression = (BundleCompression)42 }).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BundleWriteOptions { CompressionLevel = (System.IO.Compression.CompressionLevel)42 }).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BundleWriteOptions { MaxDegreeOfParallelism = 0 }).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new BundleWriteOptions { MaxCompressionMemory = -1 }).Should().ThrowExactly<ArgumentOutOfRangeException>();

        var options = BundleWriteOptions.Default with { Compression = BundleCompression.NoCompression };
        options.Compression.Should().Be(BundleCompression.NoCompression);
        BundleWriteOptions.Default.Compression.Should().Be(BundleCompression.Preserve);
    }

    [Fact]
    public async Task SaveAsync_Asynchronous_NeverUsesSynchronousIo()
    {
        await using var bundle = await SingleFileBundle.OpenAsync(new GuardStream(SampleV6(), allowSync: false), cancellationToken: TestContext.Current.CancellationToken);
        bundle.GetEntry("app.dll").Replace(s_Compressible);

        await bundle.SaveAsync(new GuardStream([], allowSync: false), new BundleWriteOptions { Compression = BundleCompression.Compress }, TestContext.Current.CancellationToken);
        (await bundle.GetEntry("lib/native.dll").ReadAllBytesAsync(TestContext.Current.CancellationToken)).Length.Should().Be(s_Compressible.Length);
    }

    [Fact]
    public async Task SaveAsync_CompressWithoutMemory_SpillsAndWritesEntries()
    {
        var files = Enumerable.Range(0, 8).Select(i => new BundleFile($"text{i}.txt", s_Compressible, BundleFileType.Unknown)).ToList();
        await using var bundle = await SingleFileBundle.OpenAsync(new MemoryStream(Build(6, files)), cancellationToken: TestContext.Current.CancellationToken);
        using var output = new MemoryStream();

        await bundle.SaveAsync(output, new BundleWriteOptions { Compression = BundleCompression.Compress, MaxCompressionMemory = 0 }, TestContext.Current.CancellationToken);

        using var reopened = OpenBytes(output.ToArray());
        reopened.GetEntry("text3.txt").ReadAllBytes().Should().Equal(s_Compressible);
    }

    [Fact]
    public async Task SaveAsync_File_WritesBundleWithoutTemporaryFiles()
    {
        string input = WriteTempFile(SampleV6());
        string output = input + ".out.exe";

        try
        {
            await using (var bundle = await SingleFileBundle.OpenAsync(input, TestContext.Current.CancellationToken))
            {
                bundle.Add("added.txt", BundleFileType.Unknown, "async"u8.ToArray());
                await bundle.SaveAsync(output, cancellationToken: TestContext.Current.CancellationToken);
            }

            using var reopened = SingleFileBundle.Open(output);
            reopened.GetEntry("added.txt").ReadAllBytes().Should().Equal("async"u8.ToArray());
            Directory.GetFiles(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.*.tmp").Should().BeEmpty();
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Fact]
    public void Save_AssembliesAlignedBeyondSdkMaximum_AlignsTo16KiB()
    {
        // Assemblies that happen to sit at 64 KiB multiples do not make every saved assembly start at one.
        byte[] original = Build(6,
        [
            new("a.dll", RandomData(37, 1), BundleFileType.Assembly),
            new("b.dll", RandomData(53, 2), BundleFileType.Assembly)
        ], assemblyAlignment: 64 * 1024);
        using var bundle = OpenBytes(original);

        using var saved = OpenBytes(SaveToBytes(bundle));

        long offsetA = saved.GetEntry("a.dll").Offset;
        long offsetB = saved.GetEntry("b.dll").Offset;
        (offsetB % (16 * 1024)).Should().Be(0);
        (offsetB - offsetA).Should().Be(16 * 1024);
    }

    [Fact]
    public void Save_AutoOnCompressedBundle_CompressesUnchangedEntries()
    {
        using var bundle = OpenBytes(Build(6,
        [
            new("lib.dll", s_Compressible, BundleFileType.NativeBinary, Compress: true),
            new("text.txt", s_Compressible, BundleFileType.Unknown),
            new("app.deps.json", s_Compressible, BundleFileType.DepsJson)
        ]));

        using var reopened = OpenBytes(SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Auto }));

        reopened.GetEntry("text.txt").IsCompressed.Should().BeTrue();
        reopened.GetEntry("text.txt").ReadAllBytes().Should().Equal(s_Compressible);
        reopened.GetEntry("lib.dll").IsCompressed.Should().BeTrue();
        reopened.GetEntry("app.deps.json").IsCompressed.Should().BeFalse();
    }

    [Fact]
    public void Save_AutoOnUncompressedFrameworkDependentBundle_KeepsEntriesUncompressed()
    {
        const string runtimeConfig = "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\"}}}";
        using var bundle = OpenBytes(Build(6,
        [
            new("text.txt", s_Compressible, BundleFileType.Unknown),
            new("app.runtimeconfig.json", Encoding.UTF8.GetBytes(runtimeConfig), BundleFileType.RuntimeConfigJson)
        ]));
        bundle.Add("new.txt", BundleFileType.Unknown, s_Compressible);

        using var reopened = OpenBytes(SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Auto }));

        reopened.GetEntry("text.txt").IsCompressed.Should().BeFalse();
        reopened.GetEntry("new.txt").IsCompressed.Should().BeFalse();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(4096L)]
    public void Save_CompressBeyondMemoryLimit_SpillsAndWritesSameBytes(long maxCompressionMemory)
    {
        var files = Enumerable.Range(0, 8).Select(i => new BundleFile($"text{i}.txt", s_Compressible, BundleFileType.Unknown)).ToList();
        using var bundle = OpenBytes(Build(6, files));

        byte[] inMemory = SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Compress });
        byte[] spilled = SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Compress, MaxCompressionMemory = maxCompressionMemory });

        spilled.Should().Equal(inMemory);

        using var reopened = OpenBytes(spilled);
        reopened.Entries.Should().AllSatisfy(e => e.IsCompressed.Should().BeTrue());
    }

    [Fact]
    public void Save_CompressVersion2Bundle_KeepsEntriesUncompressed()
    {
        using var bundle = OpenBytes(Build(2, [new("text.txt", s_Compressible, BundleFileType.Unknown)]));

        using var reopened = OpenBytes(SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Compress }));

        reopened.GetEntry("text.txt").IsCompressed.Should().BeFalse();
        reopened.Header.MajorVersion.Should().Be(2u);
    }

    [Fact]
    public void Save_CompressWithInfiniteThreshold_KeepsIncompressibleEntriesCompressed()
    {
        byte[] random = RandomData(5000);
        using var bundle = OpenBytes(Build(6, [new("random.bin", random, BundleFileType.Unknown)]));
        var options = new BundleWriteOptions { Compression = BundleCompression.Compress, CompressionThreshold = double.PositiveInfinity };

        using var reopened = OpenBytes(SaveToBytes(bundle, options));

        reopened.GetEntry("random.bin").IsCompressed.Should().BeTrue();
        reopened.GetEntry("random.bin").ReadAllBytes().Should().Equal(random);
    }

    [Theory]
    [InlineData("{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\"}}}", true)]
    [InlineData("{\"runtimeOptions\":{\"frameworks\":[{\"name\":\"Microsoft.NETCore.App\"}]}}", true)]
    [InlineData("{\"runtimeOptions\":{\"includedFrameworks\":[{\"name\":\"Microsoft.NETCore.App\"}]}}", false)]
    [InlineData("not json", false)]
    public void Save_CompressWithRuntimeConfig_ThrowsOnlyForFrameworkDependentApp(string runtimeConfig, bool frameworkDependent)
    {
        using var bundle = OpenBytes(Build(6,
        [
            new("text.txt", s_Compressible, BundleFileType.Unknown),
            new("app.runtimeconfig.json", Encoding.UTF8.GetBytes(runtimeConfig), BundleFileType.RuntimeConfigJson)
        ]));
        var options = new BundleWriteOptions { Compression = BundleCompression.Compress };

        if (frameworkDependent)
            FluentActions.Invoking(() => bundle.Save(new MemoryStream(), options)).Should().ThrowExactly<InvalidOperationException>();
        else
            bundle.Save(new MemoryStream(), options);

        // Preserve never introduces compression into an uncompressed bundle, so it is always allowed.
        bundle.Save(new MemoryStream());
    }

    [Fact]
    public void Save_Compress_CompressesEligibleEntriesInParallel()
    {
        var files = new List<BundleFile>
        {
            new("random.bin", RandomData(5000), BundleFileType.Unknown),
            new("app.deps.json", s_Compressible, BundleFileType.DepsJson)
        };
        files.AddRange(Enumerable.Range(0, 16).Select(i => new BundleFile($"text{i}.txt", s_Compressible, BundleFileType.Unknown)));
        using var bundle = OpenBytes(Build(6, files));

        byte[] parallel = SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Compress });
        byte[] serial = SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Compress, MaxDegreeOfParallelism = 1 });
        using var reopened = OpenBytes(parallel);

        parallel.Should().Equal(serial);
        reopened.GetEntry("text7.txt").IsCompressed.Should().BeTrue();
        reopened.GetEntry("random.bin").IsCompressed.Should().BeFalse(); // does not beat the threshold
        reopened.GetEntry("app.deps.json").IsCompressed.Should().BeFalse(); // never compressed
        reopened.GetEntry("text7.txt").ReadAllBytes().Should().Equal(s_Compressible);
        reopened.Header.Id.Should().Be(bundle.Header.Id); // content did not change
    }

    [Fact]
    public void Save_CompressedDepsJson_WritesDepsJsonUncompressed()
    {
        // The host reads deps.json through its recorded location, so it must never stay compressed.
        using var bundle = OpenBytes(Build(6,
        [
            new("app.deps.json", s_Compressible, BundleFileType.DepsJson, Compress: true),
            new("lib.dll", s_Compressible, BundleFileType.NativeBinary, Compress: true)
        ]));
        bundle.Add("new.txt", BundleFileType.Unknown, new byte[1]);

        using var reopened = OpenBytes(SaveToBytes(bundle));

        reopened.GetEntry("app.deps.json").IsCompressed.Should().BeFalse();
        reopened.GetEntry("lib.dll").IsCompressed.Should().BeTrue();
        reopened.GetEntry("app.deps.json").ReadAllBytes().Should().Equal(s_Compressible);
    }

    [Fact]
    public void Save_File_KeepsUnixExecutePermission()
    {
        Assert.SkipWhen(s_IsWindows, "Unix only.");

        string input = WriteTempFile(SampleV6());
        string output = input + ".out";

        try
        {
#pragma warning disable CA1416 // Guarded by the skip above.
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
            File.SetUnixFileMode(input, mode);

            using (var bundle = SingleFileBundle.Open(input))
                bundle.Save(output);

            File.GetUnixFileMode(output).HasFlag(UnixFileMode.UserExecute).Should().BeTrue();
#pragma warning restore CA1416
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Fact]
    public void Save_MachOHostWithoutLoadCommands_ThrowsNotSupportedException()
    {
        using var bundle = OpenBytes(SampleV6(TestHost.MachO));

        FluentActions.Invoking(() => bundle.Save(new MemoryStream())).Should().ThrowExactly<NotSupportedException>();
    }

    [Fact]
    public void Save_Modified_GeneratesDeterministicId()
    {
        using var bundle = OpenBytes(SampleV6());
        bundle.GetEntry("app.dll").Replace("new"u8.ToArray());

        using var first = OpenBytes(SaveToBytes(bundle));
        using var second = OpenBytes(SaveToBytes(bundle));
        using var serial = OpenBytes(SaveToBytes(bundle, BundleWriteOptions.Default with { MaxDegreeOfParallelism = 1 }));

        first.Header.Id.Should().NotBe("originalId00");
        first.Header.Id.Length.Should().Be(12);
        second.Header.Id.Should().Be(first.Header.Id);
        serial.Header.Id.Should().Be(first.Header.Id);
        first.Header.Flags.Should().Be(BundleFlags.NetCoreApp3CompatMode);
    }

    [Fact]
    public void Save_NoCompression_DecompressesAllEntries()
    {
        using var bundle = OpenBytes(SampleV6());

        using var reopened = OpenBytes(SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.NoCompression }));

        reopened.Entries.Should().AllSatisfy(e => e.IsCompressed.Should().BeFalse());
        reopened.GetEntry("lib/native.dll").ReadAllBytes().Should().Equal(s_Compressible);
    }

    [Fact]
    public void Save_NonSeekableDestination_WritesBundle()
    {
        using var bundle = OpenBytes(SampleV6());
        bundle.GetEntry("app.dll").Replace("x"u8.ToArray());
        var destination = new WriteOnlyStream();

        bundle.Save(destination);

        using var reopened = OpenBytes(destination.ToArray());
        reopened.GetEntry("app.dll").ReadAllBytes().Should().Equal("x"u8.ToArray());
    }

    [Fact]
    public void Save_OverOpenedFile_ThrowsAndLeavesFileUnchanged()
    {
        byte[] original = SampleV6();
        string path = WriteTempFile(original);

        try
        {
            using (var bundle = SingleFileBundle.Open(path))
            {
                bundle.GetEntry("app.dll").Replace("x"u8.ToArray());
                FluentActions.Invoking(() => bundle.Save(path)).Should().ThrowExactly<IOException>();
            }

            File.ReadAllBytes(path).Should().Equal(original);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_PreserveOnUncompressedBundle_KeepsEntriesUncompressed()
    {
        using var bundle = OpenBytes(Build(6, [new("a.dll", RandomData(100), BundleFileType.Assembly)]));
        bundle.GetEntry("a.dll").Replace(s_Compressible);

        using var reopened = OpenBytes(SaveToBytes(bundle));

        reopened.GetEntry("a.dll").IsCompressed.Should().BeFalse();
    }

    [Fact]
    public void Save_Preserve_CopiesCompressedEntriesAndCompressesChanges()
    {
        byte[] original = SampleV6();
        using var bundle = OpenBytes(original);
        var native = bundle.GetEntry("lib/native.dll");
        byte[] storedBefore = original.AsSpan((int)native.Offset, (int)native.CompressedSize).ToArray();

        bundle.GetEntry("app.dll").Replace(s_Compressible);
        byte[] saved = SaveToBytes(bundle);

        using var reopened = OpenBytes(saved);
        var savedNative = reopened.GetEntry("lib/native.dll");
        saved.AsSpan((int)savedNative.Offset, (int)savedNative.CompressedSize).ToArray().Should().Equal(storedBefore);

        // The bundle already contained compressed entries, so the changed entry is compressed too.
        reopened.GetEntry("app.dll").IsCompressed.Should().BeTrue();
        reopened.GetEntry("app.dll").ReadAllBytes().Should().Equal(s_Compressible);
    }

    [Fact]
    public void Save_ReadOnlyDestination_ThrowsArgumentException()
    {
        using var bundle = OpenBytes(SampleV6());

        FluentActions.Invoking(() => bundle.Save(new MemoryStream([], writable: false))).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Save_SignedPeHost_RemovesStaleCertificateReference()
    {
        byte[] original = [.. SampleV6(), .. new byte[512]];
        SetSecurityDirectory(original, (uint)(original.Length - 512), 512);
        using var bundle = OpenBytes(original);

        byte[] saved = SaveToBytes(bundle);

        BinaryPrimitives.ReadUInt64LittleEndian(saved.AsSpan(c_PeSecurityDirectoryOffset)).Should().Be(0ul);
        BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(c_PeCheckSumOffset)).Should().Be(0u);
        saved.Length.Should().Be(original.Length - 512);
    }

    [Fact]
    public void Save_SourceTruncatedAfterOpen_ThrowsIOOrInvalidDataException()
    {
        var source = new MemoryStream();
        source.Write(SampleV6());
        using var bundle = SingleFileBundle.Open(source, leaveOpen: true);
        bundle.GetEntry("app.dll").Replace("x"u8.ToArray());

        source.SetLength(c_HostLength + 100);

        var ex = Record.Exception(() => bundle.Save(new MemoryStream()));
        (ex is IOException or InvalidDataException).Should().BeTrue($"Unexpected exception: {ex}");
    }

    [Fact]
    public void Save_Synchronous_NeverUsesAsynchronousIo()
    {
        using var bundle = SingleFileBundle.Open(new GuardStream(SampleV6(), allowSync: true));
        bundle.GetEntry("app.dll").Replace(s_Compressible);

        bundle.Save(new GuardStream([], allowSync: true), new BundleWriteOptions { Compression = BundleCompression.Compress });
        bundle.GetEntry("lib/native.dll").ReadAllBytes().Length.Should().Be(s_Compressible.Length);
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void Save_UnchangedOlderVersion_WritesIdenticalBytes(uint majorVersion)
    {
        byte[] original = Build(majorVersion,
        [
            new("app.dll", RandomData(300), BundleFileType.Assembly),
            new("app.deps.json", "{\"deps\":1}"u8.ToArray(), BundleFileType.DepsJson),
            new("app.runtimeconfig.json", "{\"rc\":1}"u8.ToArray(), BundleFileType.RuntimeConfigJson)
        ]);
        using var bundle = OpenBytes(original);

        SaveToBytes(bundle).Should().Equal(original);
    }

    [Theory]
    [InlineData(TestHost.Elf, 16)]   // .NET 5 and .NET 6 on x64 Linux
    [InlineData(TestHost.Elf, 64)]
    [InlineData(TestHost.Pe, 4096)]
    public void Save_UnchangedWithAssemblyAlignment_WritesIdenticalBytes(TestHost host, int alignment)
    {
        byte[] original = Build(6,
        [
            new("a.dll", RandomData(37, 1), BundleFileType.Assembly),
            new("b.dll", RandomData(53, 2), BundleFileType.Assembly),
            new("c.dll", RandomData(11, 3), BundleFileType.Assembly)
        ], host: host, assemblyAlignment: alignment);
        using var bundle = OpenBytes(original);

        SaveToBytes(bundle).Should().Equal(original);
    }

    [Fact]
    public void Save_UnchangedWithLongAndNonAsciiPathsAndLongId_WritesIdenticalBytes()
    {
        // Paths of 128 bytes or more have a two-byte length prefix; the .NET 6 to 9 SDKs wrote 32-character identifiers.
        byte[] original = Build(6,
        [
            new(string.Concat(Enumerable.Repeat("directory/", 20)) + "file.dll", RandomData(100, 1), BundleFileType.Assembly),
            new("ressourcen/übersetzung.日本語.resources.dll", RandomData(50, 2), BundleFileType.Assembly),
            new("lib/native.so", s_Compressible, BundleFileType.NativeBinary, Compress: true)
        ], bundleId: "ABCDEFGHIJKLMNOPQRSTUVWXYZ012345");
        using var bundle = OpenBytes(original);

        SaveToBytes(bundle).Should().Equal(original);
    }

    [Fact]
    public void Save_Unchanged_WritesIdenticalBytes()
    {
        byte[] original = SampleV6();
        using var bundle = OpenBytes(original);

        SaveToBytes(bundle).Should().Equal(original);
    }

    [Fact]
    public void Save_UncompressedAssemblies_AlignsToAssemblyAlignment()
    {
        using var bundle = OpenBytes(SampleV6());
        bundle.Add("second.dll", BundleFileType.Assembly, RandomData(10, 7));

        using (var reopened = OpenBytes(SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.NoCompression })))
        {
            foreach (var entry in reopened.Entries.Where(e => e.Type == BundleFileType.Assembly))
                (entry.Offset % 4096).Should().Be(0);
        }

        using (var reopened = OpenBytes(SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.NoCompression, AssemblyAlignment = 16 })))
        {
            (reopened.GetEntry("second.dll").Offset % 16).Should().Be(0);
            (reopened.GetEntry("second.dll").Offset % 4096).Should().NotBe(0);
        }
    }

    [Fact]
    public void Save_WithoutEntries_ThrowsInvalidOperationException()
    {
        using var bundle = OpenBytes(SampleV6());
        bundle.RemoveAll(_ => true);

        FluentActions.Invoking(() => bundle.Save(new MemoryStream())).Should().ThrowExactly<InvalidOperationException>();
    }
}