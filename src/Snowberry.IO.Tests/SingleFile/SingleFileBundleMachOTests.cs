using System.Buffers.Binary;
using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticMachOHost;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for creating and saving bundles with a macOS (Mach-O) application host, which saving re-signs like the .NET SDK.
/// </summary>
public class SingleFileBundleMachOTests
{
    [Fact]
    public void BundleWriteOptions_InvalidSigningIdentifier_ThrowsWhenSet()
    {
        FluentActions.Invoking(() => new BundleWriteOptions { MacOSCodeSignIdentifier = "" }).Should().ThrowExactly<ArgumentException>();
        FluentActions.Invoking(() => new BundleWriteOptions { MacOSCodeSignIdentifier = "a\0b" }).Should().ThrowExactly<ArgumentException>();

        BundleWriteOptions.Default.MacOSCodeSign.Should().BeTrue();
        BundleWriteOptions.Default.MacOSCodeSignIdentifier.Should().BeNull();
    }

    [Fact]
    public void Create_MachOHostWithoutLoadCommands_ThrowsInvalidDataException()
    {
        FluentActions.Invoking(() => SingleFileBundle.Create(new MemoryStream(SyntheticBundleBuilder.CreateHost(TestHost.MachO))))
            .Should().ThrowExactly<InvalidDataException>().WithMessage("*load commands*");
        FluentActions.Invoking(() => SingleFileBundle.Create(new MemoryStream(SyntheticBundleBuilder.CreateHost(TestHost.UniversalMachO))))
            .Should().ThrowExactly<InvalidDataException>().WithMessage("*Universal*");
    }

    [Theory]
    [InlineData(c_CpuTypeArm64, false, 16384)]
    [InlineData(c_CpuTypeArm64, true, 16384)]
    [InlineData(c_CpuTypeX64, false, 4096)]
    [InlineData(c_CpuTypeX64, true, 4096)]
    public void Save_CreatedFromMachOTemplate_SignsWithFileNameLikeSdk(uint cpuType, bool signedTemplate, int pageSize)
    {
        byte[] template = Build(cpuType, signedTemplate ? "singlefilehost" : null);
        string directory = NewTempDirectory();
        string output = Path.Combine(directory, "MyApp");
        Directory.CreateDirectory(directory);

        try
        {
            using (var bundle = SingleFileBundle.Create(new MemoryStream(template, writable: false)))
            {
                AddEntries(bundle);
                bundle.Save(output);
            }

            byte[] saved = File.ReadAllBytes(output);
            var info = MachOSignatureVerifier.Verify(saved);

            info.Identifier.Should().Be("MyApp");
            info.PageSize.Should().Be(pageSize);
            info.CommandCount.Should().Be(6);
            saved.AsSpan(0, 16).ToArray().Should().Equal(template.AsSpan(0, 16).ToArray());

            using var reopened = SingleFileBundle.Open(output);
            AssertEntries(reopened);
            info.StringTableEnd.Should().BeGreaterThan(reopened.Entries.Max(e => e.Offset + e.Size));

            // The bundle replaces the template's signature and follows the string table.
            reopened.Entries.Should().OnlyContain(e => e.Offset >= c_StringTableEnd);
            reopened.GetEntry("app.dll").Offset.Should().Be(cpuType == c_CpuTypeArm64 ? 0x4000 : 0x3080);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveAsync_SignedMachOBundleToStream_KeepsSigningIdentifier()
    {
        using var bundle = SingleFileBundle.Create(new MemoryStream(Build(signingIdentifier: "singlefilehost")));
        AddEntries(bundle);

        // The template's identifier names the template, so a created bundle needs one; an opened bundle keeps its own.
        FluentActions.Invoking(() => bundle.Save(new MemoryStream())).Should().ThrowExactly<InvalidOperationException>().WithMessage("*template*");
        byte[] first = SaveToBytes(bundle, new BundleWriteOptions { MacOSCodeSignIdentifier = "MyApp" });
        MachOSignatureVerifier.Verify(first).Identifier.Should().Be("MyApp");

        await using var reopened = await SingleFileBundle.OpenAsync(new MemoryStream(first), cancellationToken: TestContext.Current.CancellationToken);
        reopened.GetEntry("app.dll").Replace("changed"u8.ToArray());
        using var output = new MemoryStream();
        await reopened.SaveAsync(output, cancellationToken: TestContext.Current.CancellationToken);

        byte[] second = output.ToArray();
        var info = MachOSignatureVerifier.Verify(second);
        info.Identifier.Should().Be("MyApp");
        info.CommandCount.Should().Be(6);

        using var again = OpenBytes(second);
        again.GetEntry("app.dll").ReadAllBytes().Should().Equal("changed"u8.ToArray());
    }

    [Theory]
    [InlineData(c_CpuTypeArm64, false)]
    [InlineData(c_CpuTypeX64, true)]
    public async Task Save_MachOBundleLargerThanStagingBuffer_HashesEveryPage(uint cpuType, bool useAsync)
    {
        // Larger than the 512 KiB staging buffer, so the hashed bytes arrive in several writes that split pages.
        byte[] large = RandomData(1_500_000, seed: 3);
        using var bundle = SingleFileBundle.Create(new MemoryStream(Build(cpuType)));
        AddEntries(bundle);
        bundle.Add("large.bin", BundleFileType.Unknown, large);
        var options = new BundleWriteOptions { MacOSCodeSignIdentifier = "MyApp", MaxDegreeOfParallelism = 1 };
        using var output = new MemoryStream();

        if (useAsync)
            await bundle.SaveAsync(output, options, TestContext.Current.CancellationToken);
        else
            bundle.Save(output, options);

        byte[] saved = output.ToArray();
        MachOSignatureVerifier.Verify(saved).Identifier.Should().Be("MyApp");

        using var reopened = OpenBytes(saved);
        reopened.GetEntry("large.bin").ReadAllBytes().Should().Equal(large);
    }

    [Fact]
    public void Save_MachOBundleUnchanged_IsReproducible()
    {
        using var bundle = SingleFileBundle.Create(new MemoryStream(Build()));
        AddEntries(bundle);
        byte[] first = SaveToBytes(bundle, new BundleWriteOptions { MacOSCodeSignIdentifier = "MyApp" });

        using var reopened = OpenBytes(first);
        SaveToBytes(reopened).Should().Equal(first);
    }

    [Fact]
    public void Save_MachOHostWithoutRoomForSignatureCommand_ThrowsInvalidOperationException()
    {
        // The section starts right after the load commands, so the code signature command does not fit.
        using var bundle = SingleFileBundle.Create(new MemoryStream(Build(firstSectionOffset: 32 + 392)));
        AddEntries(bundle);

        FluentActions.Invoking(() => bundle.Save(new MemoryStream(), new BundleWriteOptions { MacOSCodeSignIdentifier = "MyApp" }))
            .Should().ThrowExactly<InvalidOperationException>().WithMessage("*room*");

        // Without signing, no command is added.
        MachOSignatureVerifier.Verify(SaveToBytes(bundle, new BundleWriteOptions { MacOSCodeSign = false })).CommandCount.Should().Be(5);
    }

    [Fact]
    public void Save_MachOWithoutCodeSign_RemovesSignatureAndExtendsLinkEdit()
    {
        using var bundle = SingleFileBundle.Create(new MemoryStream(Build(signingIdentifier: "MyApp")));
        AddEntries(bundle);

        byte[] saved = SaveToBytes(bundle, new BundleWriteOptions { MacOSCodeSign = false });
        var info = MachOSignatureVerifier.Verify(saved);

        info.SignatureOffset.Should().Be(-1);
        info.CommandCount.Should().Be(5);
        info.StringTableEnd.Should().Be(saved.Length);

        // The removed command is cleared.
        saved.AsSpan(32 + 392, 16).ToArray().Should().OnlyContain(b => b == 0);

        using var reopened = OpenBytes(saved);
        AssertEntries(reopened);
    }

    [Fact]
    public void Save_MachOStringTableAfterBundleStart_ThrowsNotSupportedException()
    {
        using var bundle = SingleFileBundle.Create(new MemoryStream(Build()));
        AddEntries(bundle);
        byte[] saved = SaveToBytes(bundle, new BundleWriteOptions { MacOSCodeSign = false });

        // Move the string table behind the first entry: it can no longer be extended over the bundle.
        const int c_SymbolTableCommand = 400;
        uint stringTableEnd = BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(c_SymbolTableCommand + 16))
            + BinaryPrimitives.ReadUInt32LittleEndian(saved.AsSpan(c_SymbolTableCommand + 20));
        BinaryPrimitives.WriteUInt32LittleEndian(saved.AsSpan(c_SymbolTableCommand + 16), stringTableEnd - 16);
        BinaryPrimitives.WriteUInt32LittleEndian(saved.AsSpan(c_SymbolTableCommand + 20), 16);

        using var reopened = OpenBytes(saved);
        FluentActions.Invoking(() => reopened.Save(new MemoryStream(), new BundleWriteOptions { MacOSCodeSign = false }))
            .Should().ThrowExactly<NotSupportedException>().WithMessage("*string table*");
    }

    [Fact]
    public void Save_UnknownMachOSigningIdentifier_FailsBeforeReadingEntries()
    {
        var content = new CountingReadStream(s_Compressible);
        using var bundle = SingleFileBundle.Create(new MemoryStream(Build()));
        bundle.Add("text.txt", BundleFileType.Unknown, content, leaveOpen: true);

        FluentActions.Invoking(() => bundle.Save(new MemoryStream(), new BundleWriteOptions { Compression = BundleCompression.Compress }))
            .Should().ThrowExactly<InvalidOperationException>();
        content.BytesRead.Should().Be(0);
    }

    [Fact]
    public void Save_UnsignedMachOToStreamWithoutIdentifier_ThrowsInvalidOperationException()
    {
        using var bundle = SingleFileBundle.Create(new MemoryStream(Build()));
        AddEntries(bundle);

        FluentActions.Invoking(() => bundle.Save(new MemoryStream()))
            .Should().ThrowExactly<InvalidOperationException>().WithMessage($"*{nameof(BundleWriteOptions.MacOSCodeSignIdentifier)}*");

        var info = MachOSignatureVerifier.Verify(SaveToBytes(bundle, new BundleWriteOptions { MacOSCodeSignIdentifier = "com.example.app" }));
        info.Identifier.Should().Be("com.example.app");
    }

    private static void AddEntries(SingleFileBundle bundle)
    {
        bundle.Add("app.dll", BundleFileType.Assembly, RandomData(5000));
        bundle.Add("app.deps.json", BundleFileType.DepsJson, "{\"deps\":1}"u8.ToArray());
        bundle.Add("libnative.dylib", BundleFileType.NativeBinary, RandomData(20000, seed: 2));
    }

    private static void AssertEntries(SingleFileBundle bundle)
    {
        bundle.Entries.Select(e => e.RelativePath).Should().Equal("app.dll", "app.deps.json", "libnative.dylib");
        bundle.GetEntry("app.dll").ReadAllBytes().Should().Equal(RandomData(5000));
        bundle.GetEntry("libnative.dylib").ReadAllBytes().Should().Equal(RandomData(20000, seed: 2));
    }
}
