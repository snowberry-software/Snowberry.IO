using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Common.Reader;

/// <summary>
/// Used to analyze and overwrite bytes in newly filled buffers.
/// </summary>
public abstract class Analyzer
{
    /// <summary>
    /// Gets called when creating and initializing a new <see cref="IEndianReader"/> instance and passing the analyzer as argument.
    /// </summary>
    /// <param name="reader">The reader that gets initialized.</param>
    public abstract void Initialize(IEndianReader reader);

    /// <summary>
    /// Analyzes the read bytes in the specified <paramref name="buffer"/>.
    /// </summary>
    /// <param name="reader">The current reader instance.</param>
    /// <param name="buffer">The buffer to analyze.</param>
    /// <param name="amount">The amount of bytes that were read.</param>
    /// <param name="offset">The offset in the <paramref name="buffer"/> for the new read data, othwerise <see langword="-1"/>.</param>
    public abstract void AnalyzeReadBytes(IEndianReader reader, byte[] buffer, int amount, long offset = -1);
}