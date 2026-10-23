using BenchmarkDotNet.Attributes;
using Snowberry.IO.Benchmarks.Config;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// Measures <see cref="SingleFileBundle.Save(string, BundleWriteOptions?)"/> with <see cref="BundleCompression.Compress"/> of an unchanged,
/// uncompressed bundle whose assemblies have the sizes of <see cref="Layout"/>, for several values of
/// <see cref="BundleWriteOptions.MaxDegreeOfParallelism"/>.
/// </summary>
/// <remarks>
/// <see cref="EntryLayout.ManySmall"/> buffers most of the compressed assemblies in temporary files, and <see cref="EntryLayout.Skewed"/>
/// shows how well the work is spread when one entry is much larger than the others.
/// </remarks>
[IoJob]
public class BundleSaveLayoutBenchmarks
{
    private SingleFileBundle _bundle = null!;
    private string? _directory;
    private BundleWriteOptions _options = BundleWriteOptions.Default;
    private string _output = string.Empty;

    /// <summary>
    /// Closes the bundle and deletes the temporary files.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _bundle.Dispose();
        BenchmarkData.DeleteDirectory(_directory);
    }

    /// <summary>
    /// Deletes the saved file after every iteration.
    /// </summary>
    [IterationCleanup]
    public void DeleteOutput()
    {
        if (File.Exists(_output))
            File.Delete(_output);
    }

    /// <summary>
    /// Saves the bundle, compressing every assembly, to a new file.
    /// </summary>
    [Benchmark]
    public void Save()
    {
        _bundle.Save(_output, _options);
    }

    /// <summary>
    /// Writes the bundle to a temporary file, opens it and creates the options.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkData.CreateTempDirectory(nameof(BundleSaveLayoutBenchmarks));
        string path = Path.Combine(_directory, SyntheticBundle.c_FileName);
        SyntheticBundle.Write(path, SyntheticBundle.GetAssemblySizes(Layout), compress: false);

        _bundle = SingleFileBundle.Open(path);
        _output = Path.Combine(_directory, "Saved.bin");
        _options = new SaveOptions(BundleCompression.Compress).ToWriteOptions(MaxDegreeOfParallelism);
    }

    /// <summary>
    /// Gets or sets the sizes of the assembly entries.
    /// </summary>
    [Params(EntryLayout.ManySmall, EntryLayout.Skewed)]
    public EntryLayout Layout { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of entries that are compressed concurrently.
    /// </summary>
    [Params(1, 4, 8)]
    public int MaxDegreeOfParallelism { get; set; }
}