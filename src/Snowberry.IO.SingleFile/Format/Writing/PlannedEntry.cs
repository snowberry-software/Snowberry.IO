using Snowberry.IO.SingleFile.Streams;

namespace Snowberry.IO.SingleFile.Format.Writing;

/// <summary>
/// The planned location and payload of an entry while a bundle is written.
/// </summary>
/// <param name="entry">The entry to write.</param>
internal sealed class PlannedEntry(BundleEntry entry)
{
    /// <summary>
    /// Verifies that the content read while saving has the planned size.
    /// </summary>
    /// <param name="actual">The number of bytes read.</param>
    /// <exception cref="IOException"><paramref name="actual"/> differs from <see cref="Size"/>.</exception>
    public void EnsureContentLength(long actual)
    {
        if (actual != Size)
            throw new IOException($"The content of bundle entry '{Entry.RelativePath}' changed length from {Size} to {actual} bytes while the bundle was being saved.");
    }

    /// <summary>
    /// Records the content hash; for an unchanged entry it is also kept for later saves of the same bundle.
    /// </summary>
    /// <param name="hash">The SHA-256 hash of the content.</param>
    public void SetHash(byte[] hash)
    {
        Hash = hash;

        if (!Entry.IsModified)
            Entry.Data.Hash = hash;
    }

    /// <summary>
    /// Gets or sets the buffer holding the payload that was compressed in advance, or <see langword="null"/> if there is none.
    /// </summary>
    public SpillBuffer? Buffer { get; set; }

    /// <summary>
    /// Gets or sets the compressed size of the entry in bytes, or <c>0</c> if the entry is stored uncompressed.
    /// </summary>
    public long CompressedSize { get; set; }

    /// <summary>
    /// Gets the entry to write.
    /// </summary>
    public BundleEntry Entry { get; } = entry;

    /// <summary>
    /// Gets or sets the SHA-256 hash of the entry content, or <see langword="null"/> if it is not known.
    /// </summary>
    public byte[]? Hash { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the hash is computed in the background while the bundle is written, so writing the payload does
    /// not compute it.
    /// </summary>
    public bool HashInBackground { get; set; }

    /// <summary>
    /// Gets or sets how the payload is written.
    /// </summary>
    public PayloadKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the offset of the payload in the bundle.
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// Gets or sets the number of bytes the payload occupies in the bundle.
    /// </summary>
    public long PayloadLength { get; set; }

    /// <summary>
    /// Gets or sets the uncompressed size of the entry in bytes.
    /// </summary>
    public long Size { get; set; }
}