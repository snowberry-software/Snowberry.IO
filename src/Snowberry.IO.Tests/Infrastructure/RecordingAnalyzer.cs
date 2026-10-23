using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// An <see cref="Analyzer"/> that records every byte it is notified about.
/// </summary>
internal sealed class RecordingAnalyzer : Analyzer
{
    public override void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int amount)
    {
        Bytes.AddRange(buffer[..amount].ToArray());
    }

    public override void Initialize(IEndianReader reader)
    {
        Initialized = true;
    }

    /// <summary>
    /// Gets the bytes the analyzer was notified about, in read order.
    /// </summary>
    public List<byte> Bytes { get; } = [];

    /// <summary>
    /// Gets a value indicating whether <see cref="Initialize"/> was called.
    /// </summary>
    public bool Initialized { get; private set; }

    /// <summary>
    /// Gets the number of bytes the analyzer was notified about.
    /// </summary>
    public int TotalBytes => Bytes.Count;
}