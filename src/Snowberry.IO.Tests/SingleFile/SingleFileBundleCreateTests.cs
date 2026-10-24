using System.Buffers.Binary;
using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for creating a <see cref="SingleFileBundle"/> without opening an existing one.
/// </summary>
public class SingleFileBundleCreateTests
{
    [Fact]
    public async Task CreateAsync_HostFile_SavesBundleThatOpensFromFile()
    {
        string host = WriteTempFile(CreateHost(TestHost.Elf));
        string output = host + ".out";

        try
        {
            await using (var bundle = await SingleFileBundle.CreateAsync(host, cancellationToken: TestContext.Current.CancellationToken))
            {
                AddSampleEntries(bundle);
                await bundle.SaveAsync(output, cancellationToken: TestContext.Current.CancellationToken);
            }

            using var reopened = SingleFileBundle.Open(output);
            AssertSampleEntries(reopened);
            File.ReadAllBytes(host).Should().Equal(CreateHost(TestHost.Elf));
        }
        finally
        {
            File.Delete(host);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task CreateAsync_CanceledToken_ThrowsAndDisposesStream()
    {
        var stream = new MemoryStream(CreateHost(TestHost.Pe));

        await FluentActions.Awaiting(() => SingleFileBundle.CreateAsync(stream, cancellationToken: new CancellationToken(canceled: true)))
            .Should().ThrowAsync<OperationCanceledException>();
        stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_Stream_SavesBundle()
    {
        await using var bundle = await SingleFileBundle.CreateAsync(
            new MemoryStream(CreateHost(TestHost.Elf)), cancellationToken: TestContext.Current.CancellationToken);
        AddSampleEntries(bundle);

        using var output = new MemoryStream();
        await bundle.SaveAsync(output, cancellationToken: TestContext.Current.CancellationToken);

        using var reopened = OpenBytes(output.ToArray());
        AssertSampleEntries(reopened);
    }

    [Fact]
    public void Create_EmptyBundle_HasNoEntriesAndCannotBeSaved()
    {
        using var bundle = SingleFileBundle.Create();

        bundle.Entries.Should().BeEmpty();
        bundle.IsModified.Should().BeFalse();
        bundle.Header.Should().Be(new BundleHeader(6, 0, string.Empty, BundleFlags.None));
        FluentActions.Invoking(() => bundle.Save(new MemoryStream())).Should().ThrowExactly<InvalidOperationException>();
    }

    [Theory]
    [InlineData(TestHost.Pe)]
    [InlineData(TestHost.Elf)]
    public void Create_HostTemplate_AppendsBundleAfterUnchangedHost(TestHost kind)
    {
        byte[] host = CreateHost(kind);
        using var bundle = SingleFileBundle.Create(new MemoryStream(host, writable: false));
        AddSampleEntries(bundle);

        byte[] saved = SaveToBytes(bundle);

        saved.AsSpan(0, c_ManifestOffsetField).ToArray().Should().Equal(host.AsSpan(0, c_ManifestOffsetField).ToArray());
        saved.AsSpan(c_ManifestOffsetField + 8, host.Length - c_ManifestOffsetField - 8).ToArray()
            .Should().Equal(host.AsSpan(c_ManifestOffsetField + 8).ToArray());

        long manifestOffset = BinaryPrimitives.ReadInt64LittleEndian(saved.AsSpan(c_ManifestOffsetField));
        manifestOffset.Should().BeGreaterThan(host.Length).And.BeLessThan(saved.Length);

        using var reopened = OpenBytes(saved);
        AssertSampleEntries(reopened);
        reopened.Entries.Should().OnlyContain(e => e.Offset >= host.Length);
    }

    [Fact]
    public void Create_HeaderValues_AreKeptUntilSaved()
    {
        var header = new BundleHeader(6, 1, "callerId0000", BundleFlags.NetCoreApp3CompatMode);
        using var bundle = SingleFileBundle.Create(header);
        bundle.Header.Should().Be(header);
        AddSampleEntries(bundle);

        using var reopened = OpenBytes(SaveToBytes(bundle));
        reopened.Header.MinorVersion.Should().Be(1);
        reopened.Header.Flags.Should().Be(BundleFlags.NetCoreApp3CompatMode);
        reopened.Header.Id.Should().NotBe("callerId0000");
        bundle.Header.Should().Be(header);

        using var withoutId = SingleFileBundle.Create(new BundleHeader(6, 0, null!, BundleFlags.None));
        withoutId.Header.Id.Should().BeEmpty();
    }

    [Fact]
    public void Create_HostFile_ReadsHostAndMissingFileThrows()
    {
        string host = WriteTempFile(CreateHost(TestHost.Pe));

        try
        {
            using var bundle = SingleFileBundle.Create(host);
            AddSampleEntries(bundle);

            using var reopened = OpenBytes(SaveToBytes(bundle));
            AssertSampleEntries(reopened);
        }
        finally
        {
            File.Delete(host);
        }

        FluentActions.Invoking(() => SingleFileBundle.Create(host)).Should().ThrowExactly<FileNotFoundException>();
        FluentActions.Invoking(() => SingleFileBundle.Create("")).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Create_HostAlreadyContainsBundle_ThrowsInvalidDataException()
    {
        FluentActions.Invoking(() => SingleFileBundle.Create(new MemoryStream(SampleV6())))
            .Should().ThrowExactly<InvalidDataException>().WithMessage("*already contains a bundle*");
    }

    [Fact]
    public void Create_HostWithoutSignature_ThrowsAndHonorsLeaveOpen()
    {
        var stream = new MemoryStream(new byte[c_HostLength]);

        FluentActions.Invoking(() => SingleFileBundle.Create(stream, leaveOpen: true)).Should().ThrowExactly<InvalidDataException>();
        stream.CanRead.Should().BeTrue();

        FluentActions.Invoking(() => SingleFileBundle.Create(stream)).Should().ThrowExactly<InvalidDataException>();
        stream.CanRead.Should().BeFalse();
    }

    [Theory]
    [InlineData(3u, BundleFlags.None)]
    [InlineData(1u, BundleFlags.NetCoreApp3CompatMode)]
    public void Create_InvalidHeader_ThrowsArgumentException(uint majorVersion, BundleFlags flags)
    {
        var header = new BundleHeader(majorVersion, 0, string.Empty, flags);

        FluentActions.Invoking(() => SingleFileBundle.Create(header)).Should().ThrowExactly<ArgumentException>().WithParameterName("header");
        FluentActions.Invoking(() => SingleFileBundle.Create(new MemoryStream(CreateHost(TestHost.Pe)), header: header))
            .Should().ThrowExactly<ArgumentException>().WithParameterName("header");
    }

    [Fact]
    public void Create_PeCertificateTableInsideImage_KeepsWholeHost()
    {
        // The marker lies in the PE section (300 to 400), and a malformed certificate table points between it and the end of the image.
        byte[] host = CreateHost(TestHost.Pe);
        host.AsSpan(c_ManifestOffsetField + 8, s_BundleSignature.Length).Clear();
        s_BundleSignature.CopyTo(host, 320);
        SetSecurityDirectory(host, 370, 16);

        using var bundle = SingleFileBundle.Create(new MemoryStream(host, writable: false));
        bundle.Add("data.bin", BundleFileType.Unknown, s_Compressible);

        using var reopened = OpenBytes(SaveToBytes(bundle));
        reopened.GetEntry("data.bin").Offset.Should().Be(c_HostLength);
        reopened.GetEntry("data.bin").ReadAllBytes().Should().Equal(s_Compressible);
    }

    [Fact]
    public void Create_SignedPeHost_RemovesCertificateTable()
    {
        byte[] certificate = RandomData(16, seed: 7);
        byte[] host = [.. CreateHost(TestHost.Pe), .. certificate];
        SetSecurityDirectory(host, c_HostLength, (uint)certificate.Length);

        using var bundle = SingleFileBundle.Create(new MemoryStream(host, writable: false));
        bundle.Add("data.bin", BundleFileType.Unknown, s_Compressible);

        byte[] saved = SaveToBytes(bundle);

        BinaryPrimitives.ReadUInt64LittleEndian(saved.AsSpan(c_PeSecurityDirectoryOffset)).Should().Be(0);
        BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(c_PeCheckSumOffset)).Should().Be(0);

        using var reopened = OpenBytes(saved);
        reopened.GetEntry("data.bin").Offset.Should().Be(c_HostLength);
        reopened.GetEntry("data.bin").ReadAllBytes().Should().Equal(s_Compressible);
    }

    [Theory]
    [InlineData(1u, BundleFlags.None)]
    [InlineData(2u, BundleFlags.NetCoreApp3CompatMode)]
    [InlineData(6u, BundleFlags.None)]
    public void Create_WithoutHost_SavesBundleThatOpenReads(uint majorVersion, BundleFlags flags)
    {
        using var bundle = SingleFileBundle.Create(new BundleHeader(majorVersion, 0, "ignored", flags));
        AddSampleEntries(bundle);
        bundle.IsModified.Should().BeTrue();

        byte[] saved = SaveToBytes(bundle);

        BinaryPrimitives.ReadInt64LittleEndian(saved).Should().BeGreaterThan(0);
        saved.AsSpan(8, s_BundleSignature.Length).ToArray().Should().Equal(s_BundleSignature);

        using var reopened = OpenBytes(saved);
        AssertSampleEntries(reopened);
        reopened.Header.MajorVersion.Should().Be(majorVersion);
        reopened.Header.Flags.Should().Be(flags);
        reopened.Header.Id.Should().HaveLength(12).And.NotBe("ignored");
        reopened.GetEntry("app.dll").Offset.Should().Be(4096);
    }

    [Fact]
    public void Create_WithoutHost_SavesToFile()
    {
        string directory = NewTempDirectory();
        string output = Path.Combine(directory, "bundle.bin");
        Directory.CreateDirectory(directory);

        try
        {
            using (var bundle = SingleFileBundle.Create())
            {
                AddSampleEntries(bundle);
                bundle.Save(output);
            }

            using var reopened = SingleFileBundle.Open(output);
            AssertSampleEntries(reopened);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Create_WithoutHost_CompressesEntriesWhenRequested()
    {
        using var bundle = SingleFileBundle.Create();
        bundle.Add("text.txt", BundleFileType.Unknown, s_Compressible);

        byte[] saved = SaveToBytes(bundle, new BundleWriteOptions { Compression = BundleCompression.Compress, AssemblyAlignment = 16 });

        using var reopened = OpenBytes(saved);
        var entry = reopened.GetEntry("text.txt");
        entry.IsCompressed.Should().BeTrue();
        entry.ReadAllBytes().Should().Equal(s_Compressible);
    }

    private static void AddSampleEntries(SingleFileBundle bundle)
    {
        bundle.Add("app.dll", BundleFileType.Assembly, RandomData(300));
        bundle.Add("app.deps.json", BundleFileType.DepsJson, "{\"deps\":1}"u8.ToArray());
        bundle.Add("lib/native.so", BundleFileType.NativeBinary, s_Compressible);
    }

    private static void AssertSampleEntries(SingleFileBundle bundle)
    {
        bundle.Entries.Select(e => e.RelativePath).Should().Equal("app.dll", "app.deps.json", "lib/native.so");
        bundle.GetEntry("app.dll").ReadAllBytes().Should().Equal(RandomData(300));
        bundle.DepsJson!.ReadAllBytes().Should().Equal("{\"deps\":1}"u8.ToArray());
        bundle.GetEntry("lib/native.so").ReadAllBytes().Should().Equal(s_Compressible);
    }
}
