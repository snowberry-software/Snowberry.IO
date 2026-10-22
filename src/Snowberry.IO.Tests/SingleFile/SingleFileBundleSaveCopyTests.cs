using System.Buffers.Binary;
using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for how saving a <see cref="SingleFileBundle"/> copies the host and the stored entries to the destination.
/// </summary>
public class SingleFileBundleSaveCopyTests
{
    private const int c_StagingBufferSize = 512 * 1024;

    [Theory]
    [InlineData(c_StagingBufferSize - 4, false)] // the manifest offset spans the first two staging buffers
    [InlineData(c_StagingBufferSize - 4, true)]
    [InlineData((4 * c_StagingBufferSize) - 1, true)]
    [InlineData((2 * 1024 * 1024) + 100, false)]
    public async Task Save_HostLongerThanStagingBuffer_PatchesManifestOffset(int manifestOffsetField, bool useAsync)
    {
        byte[] host = new byte[(3 * 1024 * 1024) + 17];
        new Random(5).NextBytes(host);
        s_BundleSignature.CopyTo(host, manifestOffsetField + 8);
        byte[] original = BuildWithHost(host, manifestOffsetField, [new("a.bin", RandomData(5000, 1), BundleFileType.Unknown)]);

        using var bundle = OpenBytes(original);
        bundle.Add("b.bin", BundleFileType.Unknown, RandomData(7000, 2));
        using var output = new MemoryStream();

        if (useAsync)
            await bundle.SaveAsync(output, cancellationToken: TestContext.Current.CancellationToken);
        else
            bundle.Save(output);

        byte[] saved = output.ToArray();
        using var reopened = OpenBytes(saved);
        reopened.GetEntry("b.bin").ReadAllBytes().Should().Equal(RandomData(7000, 2));

        // Apart from the manifest offset, the host is copied unchanged.
        BinaryPrimitives.ReadInt64LittleEndian(saved.AsSpan(manifestOffsetField)).Should().BeGreaterThan(original.Length - 200);
        saved.AsSpan(0, manifestOffsetField).SequenceEqual(host.AsSpan(0, manifestOffsetField)).Should().BeTrue();
        saved.AsSpan(manifestOffsetField + 8, host.Length - manifestOffsetField - 8).SequenceEqual(host.AsSpan(manifestOffsetField + 8)).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_LargeBundle_WritesFullStagingBuffers(bool useAsync)
    {
        byte[] original = Build(6, [.. Enumerable.Range(0, 12).Select(i => new BundleFile($"f{i}.bin", RandomData(300_000, i), BundleFileType.Unknown))]);
        using var bundle = OpenBytes(original);
        bundle.Add("new.bin", BundleFileType.Unknown, RandomData(10_000, 99));
        var output = new WriteRecordingStream();

        if (useAsync)
            await bundle.SaveAsync(output, cancellationToken: TestContext.Current.CancellationToken);
        else
            bundle.Save(output);

        output.WriteSizes[..^1].Should().AllSatisfy(size => size.Should().Be(c_StagingBufferSize));
        output.WriteSizes.Sum().Should().Be(output.ToArray().Length);

        using var reopened = OpenBytes(output.ToArray());
        reopened.GetEntry("f7.bin").ReadAllBytes().Should().Equal(RandomData(300_000, 7));
        reopened.GetEntry("new.bin").ReadAllBytes().Should().Equal(RandomData(10_000, 99));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_SourceTruncatedInsideCopiedEntry_ThrowsNamingEntry(bool useAsync)
    {
        var source = new MemoryStream();
        source.Write(Build(6,
        [
            new("a.bin", RandomData(1000, 1), BundleFileType.Unknown),
            new("b.bin", RandomData(1000, 2), BundleFileType.Unknown),
            new("c.bin", RandomData(1000, 3), BundleFileType.Unknown)
        ]));
        using var bundle = SingleFileBundle.Open(source, leaveOpen: true);
        source.SetLength(bundle.GetEntry("b.bin").Offset + 300);

        var save = () => useAsync ? bundle.SaveAsync(new MemoryStream(), cancellationToken: TestContext.Current.CancellationToken) : Task.Run(() => bundle.Save(new MemoryStream()));

        (await save.Should().ThrowExactlyAsync<InvalidDataException>()).Which.Message.Should().Be("The bundle entry 'b.bin' ended after 300 of its 1000 stored bytes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_UnchangedWithNonZeroPadding_WritesZeroPadding(bool useAsync)
    {
        // The padding before aligned assemblies is copied as part of one range of the bundle, but must still be written as zeros.
        byte[] original = Build(6,
        [
            new("a.dll", RandomData(5000, 1), BundleFileType.Assembly),
            new("b.bin", RandomData(3000, 2), BundleFileType.Unknown),
            new("c.dll", RandomData(7000, 3), BundleFileType.Assembly),
            new("empty.dll", [], BundleFileType.Assembly),
            new("d.dll", RandomData(2000, 4), BundleFileType.Assembly)
        ]);
        byte[] expected = (byte[])original.Clone();
        byte[] dirty = (byte[])original.Clone();

        using (var reference = OpenBytes(original))
        {
            var stored = reference.Entries.Where(e => e.Size > 0).OrderBy(e => e.Offset).ToList();

            for (int i = 1; i < stored.Count; i++)
            {
                long gapStart = stored[i - 1].Offset + stored[i - 1].Size;
                dirty.AsSpan((int)gapStart, (int)(stored[i].Offset - gapStart)).Fill(0xCC);
            }
        }

        using var bundle = OpenBytes(dirty);
        var output = new WriteRecordingStream();

        if (useAsync)
            await bundle.SaveAsync(output, cancellationToken: TestContext.Current.CancellationToken);
        else
            bundle.Save(output);

        output.ToArray().Should().Equal(expected);
    }
}