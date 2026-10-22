namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// The sizes of the assembly entries of a <see cref="SyntheticBundle"/>, see <see cref="SyntheticBundle.GetAssemblySizes"/>.
/// </summary>
public enum EntryLayout
{
    /// <summary>
    /// 64 assemblies of 256 KiB each (16 MiB).
    /// </summary>
    Uniform,

    /// <summary>
    /// 175 assemblies of 8 to 128 KiB (8.5 MiB), about as many as a self-contained application has; compressing them needs more
    /// buffers than the default <see cref="IO.SingleFile.BundleWriteOptions.MaxCompressionMemory"/> allows, so most of them
    /// spill to temporary files.
    /// </summary>
    ManySmall,

    /// <summary>
    /// 128 assemblies of 64 KiB followed by one of 16 MiB (24 MiB), so a single entry dominates the work.
    /// </summary>
    Skewed
}