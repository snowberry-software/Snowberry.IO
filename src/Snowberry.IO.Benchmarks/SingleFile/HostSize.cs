namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// The application host that precedes a <see cref="SyntheticBundle"/>.
/// </summary>
public enum HostSize
{
    /// <summary>
    /// A 1 KiB Windows (PE) header with the bundle marker in it.
    /// </summary>
    Small,

    /// <summary>
    /// A 10 MiB Windows (PE) image like the <c>singlefilehost</c> of a self-contained application: code, read-only data, and the bundle
    /// marker in a writable data section near the end of the image.
    /// </summary>
    Large
}