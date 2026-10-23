namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// A <see cref="MemoryStream"/> that counts the write calls it receives and keeps the array passed to the last
/// <see cref="Write(byte[], int, int)"/> call.
/// </summary>
internal sealed class WriteCallCountingStream : MemoryStream
{
    /// <summary>
    /// Clears the written data and the counter.
    /// </summary>
    public void Reset()
    {
        SetLength(0);
        WriteCalls = 0;
        LastArray = null;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteCalls++;
        LastArray = buffer;
        base.Write(buffer, offset, count);
    }

#if NETCOREAPP
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        WriteCalls++;
        LastArray = null;

        // MemoryStream forwards span writes of derived types to Write(byte[], int, int), which would count the call twice.
        byte[] copy = buffer.ToArray();
        base.Write(copy, 0, copy.Length);
    }
#endif

    public override void WriteByte(byte value)
    {
        WriteCalls++;
        LastArray = null;
        base.WriteByte(value);
    }

    /// <summary>
    /// Gets the array passed to the last <see cref="Write(byte[], int, int)"/> call; <see langword="null"/> after any other write call.
    /// </summary>
    public byte[]? LastArray { get; private set; }

    /// <summary>
    /// Gets the number of <see cref="Write(byte[], int, int)"/>, <see cref="WriteByte(byte)"/> and span write calls.
    /// </summary>
    public int WriteCalls { get; private set; }
}