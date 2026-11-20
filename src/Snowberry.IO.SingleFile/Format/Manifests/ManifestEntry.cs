namespace Snowberry.IO.SingleFile.Format.Manifests;

/// <summary>
/// A file record of the bundle manifest.
/// </summary>
/// <param name="Offset">The offset of the stored data in the bundle file.</param>
/// <param name="Size">The uncompressed size of the file, in bytes.</param>
/// <param name="CompressedSize">The size of the stored compressed data, in bytes, or <c>0</c> if the file is stored uncompressed.</param>
/// <param name="Type">The kind of file.</param>
/// <param name="RelativePath">The path of the file relative to the bundle's extraction directory.</param>
internal readonly record struct ManifestEntry(long Offset, long Size, long CompressedSize, BundleFileType Type, string RelativePath);