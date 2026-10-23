namespace Snowberry.IO.SingleFile;

/// <summary>
/// Controls how entries are stored when a bundle is saved.
/// </summary>
/// <remarks>
/// Compression requires bundle format version 6 or later (see <see cref="BundleHeader.SupportsCompression"/>); older bundles are always
/// saved uncompressed. <see cref="BundleFileType.DepsJson"/> and <see cref="BundleFileType.RuntimeConfigJson"/> entries are never compressed,
/// and compressed data is only kept when it is smaller than <see cref="BundleWriteOptions.CompressionThreshold"/> allows.
/// </remarks>
public enum BundleCompression
{
    /// <summary>
    /// Keeps unchanged entries exactly as they are stored. New and replaced entries are compressed when the opened bundle already contains
    /// compressed entries.
    /// </summary>
    Preserve = 0,

    /// <summary>
    /// Compresses every entry that may be compressed, including unchanged entries stored uncompressed. Entries already stored compressed are
    /// copied unchanged.
    /// </summary>
    /// <remarks>
    /// Only self-contained applications can read compressed entries, so saving a framework-dependent application whose bundle format supports
    /// compression throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    Compress = 1,

    /// <summary>
    /// Stores every entry uncompressed, decompressing entries that are stored compressed.
    /// </summary>
    NoCompression = 2,

    /// <summary>
    /// Compresses every entry that may be compressed when the opened bundle already contains compressed entries, as
    /// <see cref="Compress"/> does; otherwise stores entries as <see cref="Preserve"/> does.
    /// </summary>
    /// <remarks>
    /// Only the host of a self-contained application can read compressed entries, so a bundle that already contains them can be compressed
    /// fully, and one that does not is never given compressed entries.
    /// </remarks>
    Auto = 3
}