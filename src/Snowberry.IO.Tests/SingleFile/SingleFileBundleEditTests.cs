using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for adding, replacing and removing the entries of a <see cref="SingleFileBundle"/>.
/// </summary>
public class SingleFileBundleEditTests
{
    [Fact]
    public void AddFromFileAndReplaceFromFile_Files_AreReadWhenSaving()
    {
        string added = WriteTempFile("added"u8.ToArray());
        string replacement = WriteTempFile("replacement"u8.ToArray());

        try
        {
            byte[] saved;

            using (var bundle = OpenBytes(SampleV6()))
            {
                bundle.AddFromFile("plugins/added.dll", BundleFileType.Assembly, added);
                bundle.GetEntry("app.dll").ReplaceFromFile(replacement);
                saved = SaveToBytes(bundle);
            }

            using var reopened = OpenBytes(saved);
            reopened.GetEntry("plugins/added.dll").ReadAllBytes().Should().Equal("added"u8.ToArray());
            reopened.GetEntry("app.dll").ReadAllBytes().Should().Equal("replacement"u8.ToArray());

            // Disposing the bundle closed the files, so they can be deleted.
            File.Delete(added);
            File.Delete(replacement);
        }
        finally
        {
            File.Delete(added);
            File.Delete(replacement);
        }
    }

    [Fact]
    public void Add_BackslashPath_NormalizesSeparatorsAndIsSaved()
    {
        using var bundle = OpenBytes(SampleV6());

        var entry = bundle.Add(@"plugins\extra.dll", BundleFileType.Assembly, "extra"u8.ToArray());

        entry.RelativePath.Should().Be("plugins/extra.dll");
        entry.IsNew.Should().BeTrue();
        bundle.Entries[^1].Should().BeSameAs(entry);
        FluentActions.Invoking(entry.Revert).Should().ThrowExactly<InvalidOperationException>();

        using var saved = OpenBytes(SaveToBytes(bundle));
        saved.GetEntry("plugins/extra.dll").ReadAllBytes().Should().Equal("extra"u8.ToArray());
        saved.GetEntry("plugins/extra.dll").Type.Should().Be(BundleFileType.Assembly);
    }

    [Fact]
    public void Add_ExistingPath_ThrowsArgumentException()
    {
        using var bundle = OpenBytes(SampleV6());

        FluentActions.Invoking(() => bundle.Add("app.dll", BundleFileType.Assembly, new byte[1])).Should().ThrowExactly<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil.dll")]
    [InlineData("a/../../evil.dll")]
    [InlineData("a/./b.dll")]
    [InlineData("a//b.dll")]
    [InlineData("/abs.dll")]
    [InlineData("C:/abs.dll")]
    [InlineData("nul\0.dll")]
    public void Add_InvalidPath_ThrowsArgumentException(string relativePath)
    {
        using var bundle = OpenBytes(SampleV6());

        FluentActions.Invoking(() => bundle.Add(relativePath, BundleFileType.Unknown, new byte[1])).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Add_PathLongerThanWindowsLimit_ThrowsOnlyForPeHost()
    {
        string path = new('a', 300);

        using (var windowsBundle = OpenBytes(SampleV6(TestHost.Pe)))
            FluentActions.Invoking(() => windowsBundle.Add(path, BundleFileType.Unknown, new byte[1])).Should().ThrowExactly<ArgumentException>();

        using var linuxBundle = OpenBytes(SampleV6(TestHost.Elf));
        linuxBundle.Add(path, BundleFileType.Unknown, new byte[1]).RelativePath.Should().Be(path);
    }

    [Fact]
    public void Add_UndefinedType_ThrowsArgumentOutOfRangeException()
    {
        using var bundle = OpenBytes(SampleV6());

        FluentActions.Invoking(() => bundle.Add("x.bin", (BundleFileType)200, new byte[1])).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void IsModified_AddThenRemove_ReturnsFalse()
    {
        using var bundle = OpenBytes(SampleV6());

        bundle.Add("temp.txt", BundleFileType.Unknown, new byte[1]);
        bundle.IsModified.Should().BeTrue();

        bundle.Remove("temp.txt");
        bundle.IsModified.Should().BeFalse();
    }

    [Fact]
    public void RemoveAll_Predicate_RemovesMatchingEntries()
    {
        using var bundle = OpenBytes(SampleV6());

        bundle.RemoveAll(e => e.Type is BundleFileType.Assembly or BundleFileType.NativeBinary).Should().Be(2);
        bundle.Entries.Select(e => e.RelativePath).Should().Equal(["app.deps.json", "app.runtimeconfig.json"]);
    }

    [Fact]
    public void Remove_Entry_DetachesEntryAndIsSaved()
    {
        using var bundle = OpenBytes(SampleV6());
        var entry = bundle.GetEntry("app.dll");

        bundle.Remove("app.dll").Should().BeTrue();
        bundle.Remove("app.dll").Should().BeFalse();
        bundle.Remove(entry).Should().BeFalse();
        FluentActions.Invoking(() => entry.Open()).Should().ThrowExactly<InvalidOperationException>();
        bundle.IsModified.Should().BeTrue();

        using (var other = OpenBytes(SampleV6()))
            bundle.Remove(other.GetEntry("app.deps.json")).Should().BeFalse();

        using var saved = OpenBytes(SaveToBytes(bundle));
        saved.TryGetEntry("app.dll", out _).Should().BeFalse();
        saved.Entries.Count.Should().Be(3);
    }

    [Fact]
    public void Replace_ByteArray_IsSavedAndRevertible()
    {
        using var bundle = OpenBytes(SampleV6());
        var entry = bundle.GetEntry("app.dll");

        entry.Replace("replaced"u8.ToArray());

        entry.IsModified.Should().BeTrue();
        entry.IsNew.Should().BeFalse();
        entry.Offset.Should().Be(-1);
        entry.Size.Should().Be(8);
        bundle.IsModified.Should().BeTrue();

        using (var saved = OpenBytes(SaveToBytes(bundle)))
            saved.GetEntry("app.dll").ReadAllBytes().Should().Equal("replaced"u8.ToArray());

        entry.Revert();

        entry.IsModified.Should().BeFalse();
        bundle.IsModified.Should().BeFalse();
        entry.ReadAllBytes().Should().Equal(RandomData(300));
    }

    [Fact]
    public void Replace_NonSeekableStream_BuffersAndDisposesStream()
    {
        using var bundle = OpenBytes(SampleV6());
        var content = new NonSeekableMemoryStream("streamed"u8.ToArray());

        bundle.GetEntry("app.dll").Replace(content);

        content.CanRead.Should().BeFalse();
        bundle.GetEntry("app.dll").ReadAllBytes().Should().Equal("streamed"u8.ToArray());
    }

    [Fact]
    public void Replace_SeekableStream_IsReadWhenSavingAndHonorsLeaveOpen()
    {
        using var bundle = OpenBytes(SampleV6());
        var content = new MemoryStream("xxpayload"u8.ToArray()) { Position = 2 };

        bundle.GetEntry("app.dll").Replace(content, leaveOpen: true);

        using (var saved = OpenBytes(SaveToBytes(bundle)))
            saved.GetEntry("app.dll").ReadAllBytes().Should().Equal("payload"u8.ToArray());

        bundle.GetEntry("app.dll").Revert();
        content.CanRead.Should().BeTrue();

        var owned = new MemoryStream("owned"u8.ToArray());
        bundle.GetEntry("app.dll").Replace(owned);
        bundle.GetEntry("app.dll").Revert();
        owned.CanRead.Should().BeFalse();
    }
}