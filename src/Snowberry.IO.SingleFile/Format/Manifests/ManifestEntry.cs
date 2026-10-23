namespace Snowberry.IO.SingleFile.Format.Manifests;

/// <summary>
/// A file record of the bundle manifest.
/// </summary>
internal readonly record struct ManifestEntry(long Offset, long Size, long CompressedSize, BundleFileType Type, string RelativePath);