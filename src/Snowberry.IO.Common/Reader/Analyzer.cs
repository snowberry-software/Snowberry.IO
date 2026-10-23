using System;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Common.Reader;

/// <summary>
/// Used to analyze and overwrite bytes in newly filled buffers.
/// </summary>
public abstract class Analyzer
{
    /// <summary>
    /// Analyzes the read bytes in the specified <paramref name="buffer"/>.
    /// </summary>
    /// <param name="reader">The current reader instance.</param>
    /// <param name="buffer">The span of bytes to analyze.</param>
    /// <param name="amount">The amount of bytes that were read.</param>
    public abstract void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int amount);

    /// <summary>
    /// Gets called when creating and initializing a new <see cref="IEndianReader"/> instance and passing the analyzer as argument.
    /// </summary>
    /// <param name="reader">The reader that gets initialized.</param>
    public abstract void Initialize(IEndianReader reader);

    /// <summary>
    /// Gets a value indicating whether <see cref="AnalyzeReadBytes"/> can change the bytes it is given, for example to decrypt them.
    /// </summary>
    /// <remarks>
    /// <para>The reads of null-terminated strings and lines search the bytes for the terminator after the analyzer has seen them,
    /// so they report the bytes one code unit at a time until the terminator is found.</para>
    /// <para>Return <see langword="false"/> only if <see cref="AnalyzeReadBytes"/> never changes the bytes. Those reads may then search
    /// the bytes first and report the consumed bytes in larger pieces. Every consumed byte is still reported exactly once and in order,
    /// and bytes after the terminator that a read does not consume are not reported.</para>
    /// </remarks>
    /// <value><see langword="true"/> unless overridden.</value>
    public virtual bool ModifiesBytes => true;
}