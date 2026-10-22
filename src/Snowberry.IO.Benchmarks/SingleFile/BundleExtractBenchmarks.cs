using BenchmarkDotNet.Attributes;
using Snowberry.IO.Benchmarks.Config;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// Measures <see cref="SingleFileBundle.ExtractToDirectory"/> against <see cref="SingleFileBundle.ExtractToDirectoryAsync"/> for a bundle whose
/// assemblies have the sizes of <see cref="Layout"/> and are stored uncompressed or compressed.
/// </summary>
/// <remarks>Both methods extract up to <see cref="Environment.ProcessorCount"/> entries at a time.</remarks>
[IoJob]
public class BundleExtractBenchmarks
{
    private SingleFileBundle _bundle = null!;
    private string? _directory;
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
    /// Deletes the extracted files after every iteration.
    /// </summary>
    [IterationCleanup]
    public void DeleteOutput()
    {
        BenchmarkData.DeleteDirectory(_output);
    }

    /// <summary>
    /// Extracts every entry with <see cref="SingleFileBundle.ExtractToDirectory"/>.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Extract()
    {
        _bundle.ExtractToDirectory(_output, overwrite: true);
    }

    /// <summary>
    /// Extracts every entry with <see cref="SingleFileBundle.ExtractToDirectoryAsync"/>.
    /// </summary>
    /// <returns>A task that completes when every entry has been extracted.</returns>
    [Benchmark]
    public Task ExtractAsync()
    {
        return _bundle.ExtractToDirectoryAsync(_output, overwrite: true);
    }

    /// <summary>
    /// Writes the bundle to a temporary file and opens it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The bundle does not have the expected compressed entries.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkData.CreateTempDirectory(nameof(BundleExtractBenchmarks));
        string path = Path.Combine(_directory, SyntheticBundle.c_FileName);
        int[] sizes = SyntheticBundle.GetAssemblySizes(Layout);
        SyntheticBundle.Write(path, sizes, Compressed);

        _bundle = SingleFileBundle.Open(path);
        _output = Path.Combine(_directory, "extracted");

        if (_bundle.Entries.Count(e => e.IsCompressed) != (Compressed ? sizes.Length : 0))
            throw new InvalidOperationException("The synthetic bundle does not have the expected compressed entries.");
    }

    /// <summary>
    /// Gets or sets whether the assemblies are stored compressed.
    /// </summary>
    [Params(false, true)]
    public bool Compressed { get; set; }

    /// <summary>
    /// Gets or sets the sizes of the assembly entries.
    /// </summary>
    [Params(EntryLayout.Uniform, EntryLayout.Skewed)]
    public EntryLayout Layout { get; set; }
}