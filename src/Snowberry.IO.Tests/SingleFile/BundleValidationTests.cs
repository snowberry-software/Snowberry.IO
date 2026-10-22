using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for the validation of malformed bundles when they are opened.
/// </summary>
public class BundleValidationTests
{
    /// <summary>
    /// Asserts that <see cref="SingleFileBundle.Open(string)"/> rejects <paramref name="bytes"/> with an <see cref="InvalidDataException"/>
    /// and that <see cref="SingleFileBundle.TryOpen"/> returns <see langword="false"/>.
    /// </summary>
    private static InvalidDataException AssertRejected(byte[] bytes)
    {
        string path = WriteTempFile(bytes);

        try
        {
            var ex = FluentActions.Invoking(() => SingleFileBundle.Open(path)).Should().ThrowExactly<InvalidDataException>().Which;

            SingleFileBundle.TryOpen(path, out var bundle).Should().BeFalse();
            bundle.Should().BeNull();

            return ex;
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A valid bundle whose last entry, <c>zzzzz</c>, ends the manifest.
    /// </summary>
    private static byte[] Sample()
    {
        return Build(6,
        [
            new("app.dll", [1, 2, 3], BundleFileType.Assembly),
            new("zzzzz", [4, 5], BundleFileType.Unknown)
        ]);
    }

    [Fact]
    public void Open_AdjacentEntries_IsAccepted()
    {
        byte[] bytes = BuildRaw(6,
        [
            new(c_HostLength + 10, 10, 0, BundleFileType.Unknown, "b"),
            new(c_HostLength, 10, 0, BundleFileType.Unknown, "a"),
            new(c_HostLength + 20, 50, 5, BundleFileType.Unknown, "c")
        ], dataLength: 25);

        using var bundle = OpenBytes(bytes);

        bundle.Entries.Count.Should().Be(3);
    }

    [Fact]
    public void Open_DefinedFileTypes_AreAccepted()
    {
        var entries = Enum.GetValues<BundleFileType>().Select((type, i) => new RawEntry(c_HostLength + i, 1, 0, type, type.ToString())).ToList();

        using var bundle = OpenBytes(BuildRaw(6, entries, dataLength: entries.Count));

        bundle.Entries.Select(e => e.Type).Should().Equal(Enum.GetValues<BundleFileType>());
    }

    [Fact]
    public void Open_EmptyEntriesSharingAnOffset_IsAccepted()
    {
        // The .NET SDK places an empty entry at the offset of the entry that follows it.
        byte[] bytes = BuildRaw(6,
        [
            new(c_HostLength, 10, 0, BundleFileType.Unknown, "a"),
            new(c_HostLength + 10, 0, 0, BundleFileType.Unknown, "empty1"),
            new(c_HostLength + 10, 0, 0, BundleFileType.Unknown, "empty2"),
            new(c_HostLength + 10, 10, 0, BundleFileType.Unknown, "b"),
            new(c_HostLength + 5, 0, 0, BundleFileType.Unknown, "empty3")
        ], dataLength: 20);

        using var bundle = OpenBytes(bytes);

        bundle.Entries.Count.Should().Be(5);
        bundle.GetEntry("empty1").ReadAllBytes().Should().BeEmpty();
    }

    [Fact]
    public void Open_FileCountBeyondTheManifestLimit_IsRejectedWithoutLargeAllocations()
    {
        // 5 million records need more than the 64 MiB a manifest may occupy, although the file is large enough to hold them.
        byte[] bytes = BuildRaw(6, [], fileCountOverride: 5_000_000);
        using var stream = new ZeroPaddedStream(bytes, 1L << 40);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var ex = FluentActions.Invoking(() => SingleFileBundle.Open(stream, leaveOpen: true)).Should().ThrowExactly<InvalidDataException>().Which;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        ex.Message.Should().Contain("file count");
        allocated.Should().BeLessThan(8 * 1024 * 1024);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })] // more than five bytes
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })] // a negative length
    public void Open_InvalidPathLength_IsRejectedAsMalformed(byte[] encodedLength)
    {
        byte[] bytes = Sample();
        int index = IndexOfPath(bytes, "zzzzz");
        encodedLength.CopyTo(bytes, index);

        AssertRejected(bytes).Message.Should().Be("The bundle manifest is malformed.");
    }

    [Fact]
    public void Open_InvalidUtf8Path_IsRejectedAsMalformed()
    {
        byte[] bytes = Sample();
        int index = IndexOfPath(bytes, "zzzzz");
        bytes[index + 1] = 0xC3;
        bytes[index + 2] = 0x28;

        var ex = AssertRejected(bytes);
        ex.Message.Should().Be("The bundle manifest is malformed.");
        ex.InnerException.Should().BeAssignableTo<ArgumentException>();
    }

    [Fact]
    public void Open_ManifestLargerThanTheFirstRead_IsReadInFull()
    {
        // About 170 KiB of records, so the manifest is read again with a larger buffer.
        var entries = Enumerable.Range(0, 3000)
            .Select(i => new RawEntry(c_HostLength, 0, 0, BundleFileType.Unknown, $"directory/with/a/long/name/file{i:D5}.txt"))
            .ToList();

        using var bundle = OpenBytes(BuildRaw(6, entries));

        bundle.Entries.Count.Should().Be(3000);
        bundle.Entries[^1].RelativePath.Should().Be("directory/with/a/long/name/file02999.txt");
    }

    [Fact]
    public void Open_ManifestOffsetAtTheEndOfTheFile_IsRejected()
    {
        byte[] bytes = Sample();
        BitConverter.GetBytes((long)bytes.Length).CopyTo(bytes, c_ManifestOffsetField);

        AssertRejected(bytes).Message.Should().Contain("outside of the file");
    }

    [Theory]
    [InlineData(1L)]                                             // inside the host
    [InlineData(c_ManifestOffsetField + 8 + 31)]                 // inside the signature
    [InlineData(long.MaxValue)]
    [InlineData(-1L)]
    public void Open_ManifestOffsetOutsideTheBundle_IsRejected(long manifestOffset)
    {
        byte[] bytes = Sample();
        BitConverter.GetBytes(manifestOffset).CopyTo(bytes, c_ManifestOffsetField);

        AssertRejected(bytes).Message.Should().Contain("outside of the file");
    }

    [Fact]
    public void Open_ManyEntriesSharingOneBlob_IsRejected()
    {
        var entries = Enumerable.Range(0, 100).Select(i => new RawEntry(c_HostLength, 50, 0, BundleFileType.Unknown, "copy" + i)).ToList();

        AssertRejected(BuildRaw(6, entries, dataLength: 50));
    }

    [Theory]
    [InlineData(c_HostLength, 10L, 0L, c_HostLength + 5, 10L, 0L)]   // partial overlap
    [InlineData(c_HostLength, 10L, 0L, c_HostLength, 10L, 0L)]       // the same range twice
    [InlineData(c_HostLength, 20L, 0L, c_HostLength + 5, 5L, 0L)]    // contained
    [InlineData(c_HostLength + 5, 10L, 0L, c_HostLength, 10L, 0L)]   // out of order
    [InlineData(c_HostLength, 5L, 10L, c_HostLength + 9, 10L, 0L)]   // compressed size counts, not the uncompressed size
    public void Open_OverlappingEntries_IsRejected(long offset1, long size1, long compressedSize1, long offset2, long size2, long compressedSize2)
    {
        byte[] bytes = BuildRaw(6,
        [
            new(offset1, size1, compressedSize1, BundleFileType.Unknown, "a"),
            new(offset2, size2, compressedSize2, BundleFileType.Unknown, "b")
        ], dataLength: 100);

        AssertRejected(bytes).Message.Should().Contain("overlaps");
    }

    [Fact]
    public void Open_PathLongerThanTheManifest_IsRejectedAsMalformed()
    {
        byte[] bytes = Sample();
        int index = IndexOfPath(bytes, "zzzzz");
        bytes[index] = 0x7F;

        AssertRejected(bytes).Message.Should().Be("The bundle manifest is malformed.");
    }

    [Fact]
    public void Open_SignatureWithoutRoomForTheManifestOffset_IsRejected()
    {
        byte[] bytes = new byte[200];
        s_BundleSignature.CopyTo(bytes, 3);

        AssertRejected(bytes).Message.Should().Contain("invalid offset");
    }

    [Fact]
    public void Open_TruncatedManifestLargerThanTheFirstRead_IsRejectedAsMalformed()
    {
        var entries = Enumerable.Range(0, 3000)
            .Select(i => new RawEntry(c_HostLength, 0, 0, BundleFileType.Unknown, $"directory/with/a/long/name/file{i:D5}.txt"))
            .ToList();
        byte[] bytes = BuildRaw(6, entries);

        AssertRejected(bytes[..^10]).Message.Should().Be("The bundle manifest is malformed.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public void Open_TruncatedManifest_IsRejectedAsMalformed(int removedBytes)
    {
        byte[] bytes = Sample();

        var ex = AssertRejected(bytes[..^removedBytes]);
        ex.Message.Should().Be("The bundle manifest is malformed.");
        ex.InnerException.Should().BeOfType<EndOfStreamException>();
    }

    [Theory]
    [InlineData((byte)6)]
    [InlineData((byte)255)]
    public void Open_UndefinedFileType_IsRejected(byte type)
    {
        byte[] bytes = BuildRaw(6, [new(c_HostLength, 10, 0, (BundleFileType)type, "x")], dataLength: 10);

        AssertRejected(bytes).Message.Should().Contain("unknown file type");
    }

    [Fact]
    public void Open_ZeroManifestOffset_IsRejectedAsHostWithoutBundle()
    {
        byte[] bytes = Sample();
        BitConverter.GetBytes(0L).CopyTo(bytes, c_ManifestOffsetField);

        AssertRejected(bytes).Message.Should().Contain("without a bundle");
    }

    [Fact]
    public void Save_CompressedAndEmptyEntries_WritesEntriesThatDoNotOverlap()
    {
        byte[] bytes = Build(6,
        [
            new("a.dll", [1, 2, 3], BundleFileType.Assembly),
            new("empty.txt", [], BundleFileType.Unknown),
            new("b.dll", [4, 5], BundleFileType.Assembly),
            new("c.bin", new byte[2000], BundleFileType.Unknown, Compress: true)
        ]);

        using var bundle = OpenBytes(bytes);
        bundle.Add("new.txt", BundleFileType.Unknown, new byte[] { 6 });

        using var output = new MemoryStream();
        bundle.Save(output, new BundleWriteOptions { Compression = BundleCompression.Compress });

        using var reopened = OpenBytes(output.ToArray());
        reopened.Entries.Count.Should().Be(5);
    }
}