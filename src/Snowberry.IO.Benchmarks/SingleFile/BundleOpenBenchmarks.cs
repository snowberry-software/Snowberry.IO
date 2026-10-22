using BenchmarkDotNet.Attributes;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// Measures opening a <see cref="SingleFileBundle"/> and enumerating its <see cref="SingleFileBundle.Entries"/>.
/// </summary>
/// <remarks>
/// Opening searches the bundle signature from the start of the file, so with <see cref="HostSize.Large"/> it reads most of the 10 MiB host.
/// </remarks>
public class BundleOpenBenchmarks
{
    private const int c_AssemblySize = 1024;
    private byte[] _bundle = [];

    private string? _directory;
    private string _path = string.Empty;

    private static long Enumerate(SingleFileBundle bundle)
    {
        long total = 0;

        foreach (var entry in bundle.Entries)
            total += entry.Size + entry.RelativePath.Length;

        return total;
    }

    /// <summary>
    /// Deletes the temporary file.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkData.DeleteDirectory(_directory);
    }

    /// <summary>
    /// Opens the bundle file with <see cref="SingleFileBundle.Open(string)"/> and enumerates its entries.
    /// </summary>
    /// <returns>The total size of the entries.</returns>
    [Benchmark(Baseline = true)]
    public long OpenFile()
    {
        using var bundle = SingleFileBundle.Open(_path);
        return Enumerate(bundle);
    }

    /// <summary>
    /// Opens the bundle file with <see cref="SingleFileBundle.OpenAsync(string, CancellationToken)"/> and enumerates its entries.
    /// </summary>
    /// <returns>The total size of the entries.</returns>
    [Benchmark]
    public async Task<long> OpenFileAsync()
    {
        await using var bundle = await SingleFileBundle.OpenAsync(_path).ConfigureAwait(false);
        return Enumerate(bundle);
    }

    /// <summary>
    /// Opens the bundle from a <see cref="MemoryStream"/> with <see cref="SingleFileBundle.Open(Stream, bool)"/> and enumerates its entries.
    /// </summary>
    /// <returns>The total size of the entries.</returns>
    [Benchmark]
    public long OpenMemory()
    {
        using var bundle = SingleFileBundle.Open(new MemoryStream(_bundle, writable: false));
        return Enumerate(bundle);
    }

    /// <summary>
    /// Writes the bundle to a temporary file and loads a copy of it into memory.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkData.CreateTempDirectory(nameof(BundleOpenBenchmarks));
        _path = Path.Combine(_directory, SyntheticBundle.c_FileName);
        SyntheticBundle.Write(_path, EntryCount, c_AssemblySize, compress: false, HostSize);
        _bundle = File.ReadAllBytes(_path);
    }

    /// <summary>
    /// Gets or sets the number of assembly entries in the bundle.
    /// </summary>
    [Params(16, 1024)]
    public int EntryCount { get; set; }

    /// <summary>
    /// Gets or sets the application host that precedes the bundle.
    /// </summary>
    [Params(HostSize.Small, HostSize.Large)]
    public HostSize HostSize { get; set; }
}