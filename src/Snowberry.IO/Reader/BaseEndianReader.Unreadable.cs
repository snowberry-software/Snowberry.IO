namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <summary>
    /// Moves <see cref="Position"/> from data that cannot be read to the next data that can, after a read at <see cref="Position"/>
    /// returned no bytes.
    /// </summary>
    /// <remarks>
    /// Only process memory goes on after data that cannot be read, so only <see cref="Win32ProcessReader"/> moves; other readers have
    /// no data after the end.
    /// </remarks>
    /// <param name="maxPosition">The highest <see cref="Position"/> to move to.</param>
    /// <returns>
    /// <see langword="true"/> if <see cref="Position"/> moved to readable data at or before <paramref name="maxPosition"/>; otherwise,
    /// <see langword="false"/>, and <see cref="Position"/> is unchanged.
    /// </returns>
    internal virtual bool SkipUnreadableData(long maxPosition)
    {
        return false;
    }
}