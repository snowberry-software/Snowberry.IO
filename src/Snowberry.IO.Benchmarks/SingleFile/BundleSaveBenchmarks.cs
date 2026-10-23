using BenchmarkDotNet.Attributes;
using Snowberry.IO.Benchmarks.Config;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// Measures <see cref="SingleFileBundle.Save(string, BundleWriteOptions?)"/> of a bundle of 64 assemblies of 256 KiB each, unchanged and after
/// replacing one assembly, with the options of <see cref="Options"/>.
/// </summary>
/// <remarks>
/// <see cref="SaveAfterReplace"/> opens the bundle and replaces the assembly before every iteration, so every save computes the hashes of all
/// entries for the new bundle identifier, as the first save of a changed bundle does.
/// </remarks>
[IoJob]
public class BundleSaveBenchmarks
{
    private const int c_AssemblyCount = 64;
    private const int c_AssemblySize = 256 * 1024;
    private SingleFileBundle _bundle = null!;

    private string? _directory;
    private BundleWriteOptions _options = BundleWriteOptions.Default;
    private string _output = string.Empty;
    private string _path = string.Empty;
    private SingleFileBundle? _replacedBundle;
    private byte[] _replacement = [];

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
    /// Deletes the saved file and closes the bundle opened for <see cref="SaveAfterReplace"/> after every iteration.
    /// </summary>
    [IterationCleanup]
    public void DeleteOutput()
    {
        _replacedBundle?.Dispose();
        _replacedBundle = null;

        if (File.Exists(_output))
            File.Delete(_output);
    }

    /// <summary>
    /// Opens the bundle and replaces its first assembly before every iteration of <see cref="SaveAfterReplace"/>.
    /// </summary>
    [IterationSetup(Target = nameof(SaveAfterReplace))]
    public void OpenAndReplace()
    {
        _replacedBundle = SingleFileBundle.Open(_path);
        _replacedBundle.GetEntry(SyntheticBundle.GetAssemblyPath(0)).Replace(_replacement);
    }

    /// <summary>
    /// Saves the unchanged bundle to a new file.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Save()
    {
        _bundle.Save(_output, _options);
    }

    /// <summary>
    /// Saves the bundle with one replaced assembly to a new file.
    /// </summary>
    /// <exception cref="InvalidOperationException">The iteration setup did not open the bundle.</exception>
    [Benchmark]
    public void SaveAfterReplace()
    {
        var bundle = _replacedBundle ?? throw new InvalidOperationException("The bundle with the replaced assembly is not open.");
        bundle.Save(_output, _options);
    }

    /// <summary>
    /// Writes the bundle to a temporary file, opens it and creates the options and the replacement content.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _directory = BenchmarkData.CreateTempDirectory(nameof(BundleSaveBenchmarks));
        _path = Path.Combine(_directory, SyntheticBundle.c_FileName);
        SyntheticBundle.Write(_path, c_AssemblyCount, c_AssemblySize, SourceCompressed, HostSize);

        _bundle = SingleFileBundle.Open(_path);
        _output = Path.Combine(_directory, "Saved.bin");
        _options = Options.ToWriteOptions();
        _replacement = SyntheticBundle.CreateContent(c_AssemblySize, seed: c_AssemblyCount);
    }

    /// <summary>
    /// Gets or sets the application host that precedes the bundle.
    /// </summary>
    [Params(HostSize.Small, HostSize.Large)]
    public HostSize HostSize { get; set; }

    /// <summary>
    /// Gets or sets how the entries are stored.
    /// </summary>
    [ParamsSource(nameof(OptionsValues))]
    public SaveOptions Options { get; set; } = new(BundleCompression.Preserve);

    /// <summary>
    /// Gets the values of <see cref="Options"/>.
    /// </summary>
    public static IEnumerable<SaveOptions> OptionsValues => SaveOptions.All();

    /// <summary>
    /// Gets or sets whether the assemblies of the opened bundle are stored compressed.
    /// </summary>
    [Params(false, true)]
    public bool SourceCompressed { get; set; }
}