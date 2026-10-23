using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// A raw, possibly invalid, manifest record; no file data is written for it.
/// </summary>
/// <param name="Offset">The offset of the entry data in the bundle.</param>
/// <param name="Size">The uncompressed size.</param>
/// <param name="CompressedSize">The compressed size, or <c>0</c> for an uncompressed entry.</param>
/// <param name="Type">The entry type.</param>
/// <param name="RelativePath">The path of the entry in the bundle.</param>
internal readonly record struct RawEntry(long Offset, long Size, long CompressedSize, BundleFileType Type, string RelativePath);