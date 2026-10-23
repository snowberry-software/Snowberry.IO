namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// A <see cref="MemoryStream"/> that counts how often it is flushed.
/// </summary>
internal sealed class FlushCountingStream : MemoryStream
{
    public override void Flush()
    {
        FlushCount++;
        base.Flush();
    }

    /// <summary>
    /// Gets the number of <see cref="Flush"/> calls.
    /// </summary>
    public int FlushCount { get; private set; }
}