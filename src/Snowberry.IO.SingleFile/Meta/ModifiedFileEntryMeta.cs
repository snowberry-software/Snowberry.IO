namespace Snowberry.IO.SingleFile.Meta;

/// <summary>
/// Stores the modification of a <see cref="FileEntry"/>.
/// </summary>
public class ModifiedFileEntryMeta : IDisposable
{
    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        ModifiedDataStream?.Dispose();
    }

    /// <summary>
    /// The stream that contains the modified data.
    /// </summary>
    /// <remarks>This should not be accessed directly.</remarks>
    public Stream? ModifiedDataStream;

    /// <summary>
    /// The modified data location in the <see cref="ModifiedDataStream"/>.
    /// </summary>
    /// <remarks>This should not be accessed directly.</remarks>
    public FileLocation FileLocation { get; set; }
}
