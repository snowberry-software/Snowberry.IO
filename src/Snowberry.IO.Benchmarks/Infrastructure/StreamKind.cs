namespace Snowberry.IO.Benchmarks.Infrastructure;

/// <summary>
/// The kind of stream that a reader benchmark reads from.
/// </summary>
public enum StreamKind
{
    /// <summary>
    /// A <see cref="MemoryStream"/> over the data.
    /// </summary>
    Memory,

    /// <summary>
    /// An unbuffered <see cref="FileStream"/> over a temporary file.
    /// </summary>
    File,

    /// <summary>
    /// A <see cref="BufferedStream"/> over an unbuffered <see cref="FileStream"/> of a temporary file.
    /// </summary>
    BufferedFile
}