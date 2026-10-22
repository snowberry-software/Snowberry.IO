using System.IO.Compression;
using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// A combination of <see cref="BundleWriteOptions.Compression"/> and <see cref="BundleWriteOptions.CompressionLevel"/> that a save benchmark
/// uses.
/// </summary>
/// <param name="compression">How the entries are stored.</param>
/// <param name="level">The compression level of entries that are compressed while saving.</param>
public sealed class SaveOptions(BundleCompression compression, CompressionLevel level = CompressionLevel.SmallestSize)
{
    /// <summary>
    /// Returns the combinations that differ in what is written: <see cref="BundleCompression.NoCompression"/>,
    /// <see cref="BundleCompression.Preserve"/> (the default) and <see cref="BundleCompression.Auto"/> with the default level, and
    /// <see cref="BundleCompression.Compress"/> with <see cref="CompressionLevel.Optimal"/> and <see cref="CompressionLevel.SmallestSize"/>.
    /// </summary>
    /// <returns>The combinations.</returns>
    public static IEnumerable<SaveOptions> All()
    {
        yield return new SaveOptions(BundleCompression.NoCompression);
        yield return new SaveOptions(BundleCompression.Preserve);
        yield return new SaveOptions(BundleCompression.Auto);
        yield return new SaveOptions(BundleCompression.Compress, CompressionLevel.Optimal);
        yield return new SaveOptions(BundleCompression.Compress, CompressionLevel.SmallestSize);
    }

    /// <summary>
    /// Returns the name shown in the benchmark results, such as <c>Compress-Optimal</c>; the level is left out where it is the default.
    /// </summary>
    /// <returns>The name of the combination.</returns>
    public override string ToString()
    {
        return Level == CompressionLevel.SmallestSize && Compression != BundleCompression.Compress ? Compression.ToString() : $"{Compression}-{Level}";
    }

    /// <summary>
    /// Creates the <see cref="BundleWriteOptions"/> of this combination.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">The value of <see cref="BundleWriteOptions.MaxDegreeOfParallelism"/>.</param>
    /// <returns>The options.</returns>
    public BundleWriteOptions ToWriteOptions(int maxDegreeOfParallelism = -1)
    {
        return new BundleWriteOptions
        {
            Compression = Compression,
            CompressionLevel = Level,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };
    }

    /// <summary>
    /// Gets how the entries are stored.
    /// </summary>
    public BundleCompression Compression { get; } = compression;

    /// <summary>
    /// Gets the compression level of entries that are compressed while saving.
    /// </summary>
    public CompressionLevel Level { get; } = level;
}