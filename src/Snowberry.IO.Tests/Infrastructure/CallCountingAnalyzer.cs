using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// An <see cref="Analyzer"/> that records every byte it is notified about and counts the notifications, without changing a byte.
/// </summary>
/// <param name="modifiesBytes">The value that <see cref="ModifiesBytes"/> reports.</param>
internal sealed class CallCountingAnalyzer(bool modifiesBytes) : Analyzer
{
    public override void AnalyzeReadBytes(IEndianReader reader, Span<byte> buffer, int amount)
    {
        Calls++;
        Bytes.AddRange(buffer[..amount].ToArray());
    }

    public override void Initialize(IEndianReader reader)
    {
    }

    /// <summary>
    /// Gets the bytes the analyzer was notified about, in read order.
    /// </summary>
    public List<byte> Bytes { get; } = [];

    /// <summary>
    /// Gets the number of notifications.
    /// </summary>
    public int Calls { get; private set; }

    public override bool ModifiesBytes => modifiesBytes;
}