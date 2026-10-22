using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Benchmarks.Infrastructure;

/// <summary>
/// An <see cref="Analyzer"/> that does nothing, so a benchmark measures only the cost of reporting the reads to an analyzer.
/// </summary>
internal sealed class NoOpAnalyzer : Analyzer
{
    /// <inheritdoc/>
    public override void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int amount)
    {
    }

    /// <inheritdoc/>
    public override void Initialize(IEndianReader reader)
    {
    }
}