using AwesomeAssertions;
using Snowberry.IO.SingleFile.Format;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for the entry path rules in <see cref="BundlePath"/>.
/// </summary>
public class BundlePathTests
{
    [Fact]
    public void GetRootPrefix_DirectoryWithOrWithoutSeparator_EndsInOneSeparator()
    {
        string root = Path.GetFullPath(NewTempDirectory());
        string fileSystemRoot = Path.GetPathRoot(root)!;

        BundlePath.GetRootPrefix(root).Should().Be(root + Path.DirectorySeparatorChar);
        BundlePath.GetRootPrefix(root + Path.DirectorySeparatorChar).Should().Be(root + Path.DirectorySeparatorChar);
        BundlePath.GetRootPrefix(fileSystemRoot).Should().Be(fileSystemRoot);
    }

    [Theory]
    [InlineData(@"a\b.dll", "a/b.dll")]
    [InlineData("a/b/c.dll", "a/b/c.dll")]
    public void Normalize_BackslashOrSlashPath_ReturnsForwardSlashes(string input, string expected)
    {
        BundlePath.Normalize(input, BundlePath.c_MaxWindowsPathBytes, "path").Should().Be(expected);
    }

    [Fact]
    public void Normalize_Null_ThrowsArgumentNullException()
    {
        FluentActions.Invoking(() => BundlePath.Normalize(null!, BundlePath.c_MaxUnixPathBytes, "path")).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Normalize_PathLongerThanWindowsLimit_ThrowsOnlyForWindowsLimit()
    {
        string tooLong = new('a', BundlePath.c_MaxWindowsPathBytes + 1);

        FluentActions.Invoking(() => BundlePath.Normalize(tooLong, BundlePath.c_MaxWindowsPathBytes, "path")).Should().ThrowExactly<ArgumentException>();
        BundlePath.Normalize(tooLong, BundlePath.c_MaxUnixPathBytes, "path").Should().Be(tooLong);
    }

    [Theory]
    [InlineData("sub/file.txt", true)]
    [InlineData("../evil.txt", false)]
    [InlineData("sub/../../evil.txt", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("sub/..", false)]
    public void TryGetSafeOutputPathUnder_RootPrefix_MatchesTryGetSafeOutputPath(string relativePath, bool expected)
    {
        string root = NewTempDirectory();

        BundlePath.TryGetSafeOutputPathUnder(BundlePath.GetRootPrefix(root), relativePath, out string? underPrefix).Should().Be(expected);
        BundlePath.TryGetSafeOutputPath(root, relativePath, out string? underRoot).Should().Be(expected);
        underPrefix.Should().Be(underRoot);
    }

    [Fact]
    public void TryGetSafeOutputPath_FileSystemRoot_ResolvesUnderRoot()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;

        BundlePath.TryGetSafeOutputPath(root, "dir/file.txt", out string? fullPath).Should().BeTrue();
        fullPath.Should().Be(Path.Combine(root, "dir", "file.txt"));

        BundlePath.TryGetSafeOutputPath(root, "dir/..", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("console.txt")]
    [InlineData("com10")]
    [InlineData("sub/nul_file.txt")]
    [InlineData("lpt.txt")]
    [InlineData("COMA")]
    public void TryGetSafeOutputPath_NameThatResemblesADevice_ReturnsTrue(string relativePath)
    {
        BundlePath.TryGetSafeOutputPath(NewTempDirectory(), relativePath, out _).Should().BeTrue();
    }

    [Fact]
    public void TryGetSafeOutputPath_NestedPath_ResolvesUnderRoot()
    {
        string root = NewTempDirectory();

        BundlePath.TryGetSafeOutputPath(root, "sub/dir/file.txt", out string? fullPath).Should().BeTrue();
        fullPath.Should().StartWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar);
        fullPath.Should().EndWith("file.txt");
    }

    [Theory]
    [InlineData("nul.txt")]
    [InlineData("Con")]
    [InlineData("com1.dll")]
    [InlineData("LPT¹")]
    [InlineData("sub/aux.tar.gz")]
    [InlineData("PRN .txt")]
    [InlineData("lpt0")]
    public void TryGetSafeOutputPath_ReservedDeviceName_ReturnsFalseOnWindows(string relativePath)
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        BundlePath.TryGetSafeOutputPath(NewTempDirectory(), relativePath, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetSafeOutputPath_RootWithTrailingSeparator_ResolvesUnderRoot()
    {
        string root = NewTempDirectory() + Path.DirectorySeparatorChar;

        BundlePath.TryGetSafeOutputPath(root, "file.txt", out string? fullPath).Should().BeTrue();
        fullPath.Should().Be(Path.Combine(Path.GetFullPath(root), "file.txt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("../../../../../../evil.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("\\evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("C:\\evil.txt")]
    [InlineData("a\0b.txt")]
    [InlineData(".")]
    [InlineData("sub/..")]
    [InlineData("sub/")]
    public void TryGetSafeOutputPath_UnsafePath_ReturnsFalse(string relativePath)
    {
        BundlePath.TryGetSafeOutputPath(NewTempDirectory(), relativePath, out string? fullPath).Should().BeFalse();
        fullPath.Should().BeNull();
    }

    [Theory]
    [InlineData("data.txt.")]
    [InlineData("data.txt ")]
    [InlineData("...")]
    [InlineData("x/...")]
    [InlineData("file.txt:stream")]
    public void TryGetSafeOutputPath_WindowsTrimmedOrStreamPath_ReturnsFalseOnWindows(string relativePath)
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        BundlePath.TryGetSafeOutputPath(NewTempDirectory(), relativePath, out _).Should().BeFalse();
    }
}