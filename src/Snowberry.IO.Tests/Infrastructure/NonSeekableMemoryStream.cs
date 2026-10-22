namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// A <see cref="MemoryStream"/> over a byte array that reports itself as non-seekable.
/// </summary>
/// <param name="data">The bytes to read.</param>
internal sealed class NonSeekableMemoryStream(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;
}