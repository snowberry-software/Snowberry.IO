namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// A <see cref="MemoryStream"/> over a byte array that counts how often its position is set, through <see cref="Position"/> or <see cref="Seek"/>.
/// </summary>
/// <param name="data">The bytes to read.</param>
internal sealed class SeekCountingStream(byte[] data) : MemoryStream(data)
{
    public override long Seek(long offset, SeekOrigin loc)
    {
        PositionChanges++;
        return base.Seek(offset, loc);
    }

    public override long Position
    {
        get => base.Position;
        set
        {
            PositionChanges++;
            base.Position = value;
        }
    }

    /// <summary>
    /// Gets the number of times the position was set.
    /// </summary>
    public int PositionChanges { get; private set; }
}