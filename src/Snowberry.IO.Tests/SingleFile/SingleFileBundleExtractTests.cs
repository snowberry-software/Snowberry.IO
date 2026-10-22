using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for extracting all entries of a <see cref="SingleFileBundle"/> to a directory.
/// </summary>
public class SingleFileBundleExtractTests
{
    [Fact]
    public void ExtractToDirectory_CaseCollision_ThrowsOnCaseInsensitiveFileSystems()
    {
        Assert.SkipUnless(s_IsCaseInsensitiveFileSystem, "Windows and macOS only.");

        string directory = NewTempDirectory();
        byte[] bytes = Build(6, [new("Lib.dll", [1], BundleFileType.Unknown), new("lib.dll", [2], BundleFileType.Unknown)]);

        try
        {
            using var bundle = OpenBytes(bytes);

            FluentActions.Invoking(() => bundle.ExtractToDirectory(directory, overwrite: true)).Should().ThrowExactly<InvalidDataException>();
            Directory.Exists(directory).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ExtractToDirectory_EmptyBundle_CreatesDirectory()
    {
        string directory = NewTempDirectory();

        try
        {
            using var bundle = OpenBytes(BuildRaw(6, []));

            bundle.ExtractToDirectory(directory);

            Directory.Exists(directory).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task ExtractToDirectory_ExistingFiles_ThrowsUnlessOverwrite()
    {
        string directory = NewTempDirectory();

        try
        {
            using var bundle = OpenBytes(SampleV6());

            await bundle.ExtractToDirectoryAsync(directory, cancellationToken: TestContext.Current.CancellationToken);

            File.ReadAllBytes(Path.Combine(directory, "lib", "native.dll")).Should().Equal(s_Compressible);
            FluentActions.Invoking(() => bundle.ExtractToDirectory(directory)).Should().ThrowExactly<IOException>();

            bundle.ExtractToDirectory(directory, overwrite: true);
            Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length.Should().Be(4);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtractToDirectory_SkewedEntrySizes_WritesEveryEntry(bool useAsync)
    {
        string directory = NewTempDirectory();
        var files = Enumerable.Range(0, 40)
            .Select(i => new BundleFile($"dir{i % 3}/f{i}.bin", i % 5 == 0 ? RandomData(200_000 + i, i) : s_Compressible[..(i * 50)], BundleFileType.Unknown, Compress: i % 2 == 0))
            .ToList();

        try
        {
            using var bundle = OpenBytes(Build(6, files));

            if (useAsync)
                await bundle.ExtractToDirectoryAsync(directory, cancellationToken: TestContext.Current.CancellationToken);
            else
                bundle.ExtractToDirectory(directory);

            foreach (var file in files)
                File.ReadAllBytes(Path.Combine(directory, file.RelativePath)).Should().Equal(file.Data, file.RelativePath);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ExtractToDirectory_UnsafePath_ThrowsBeforeWritingAnything()
    {
        string directory = NewTempDirectory();
        byte[] bytes = Build(6, [new("good.txt", [1], BundleFileType.Unknown), new("../evil.txt", [2], BundleFileType.Unknown)]);

        try
        {
            using var bundle = OpenBytes(bytes);

            FluentActions.Invoking(() => bundle.ExtractToDirectory(directory)).Should().ThrowExactly<InvalidDataException>();
            FluentActions.Invoking(() => bundle.GetEntry("../evil.txt").ExtractToDirectory(directory)).Should().ThrowExactly<InvalidDataException>();
            Directory.Exists(directory).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }
}