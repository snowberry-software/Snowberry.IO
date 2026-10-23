namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// A <see cref="MemoryStream"/> that counts how often it is disposed.
/// </summary>
internal sealed class DisposeCountingStream : MemoryStream
{
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeCount++;

        base.Dispose(disposing);
    }

    /// <summary>
    /// Gets the number of <see cref="Dispose(bool)"/> calls with <c>disposing</c> set.
    /// </summary>
    public int DisposeCount { get; private set; }
}