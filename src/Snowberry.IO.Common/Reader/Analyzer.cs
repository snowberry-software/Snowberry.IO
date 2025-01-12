using System;
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
    /// <param name="buffer">The span of bytes to analyze.</param>
    /// <param name="amount">The amount of bytes that were read.</param>
    public abstract void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int amount);
}