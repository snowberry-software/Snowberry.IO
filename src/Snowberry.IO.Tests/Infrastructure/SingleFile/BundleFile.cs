using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// A file to embed in a synthetic bundle.
/// </summary>
/// <param name="RelativePath">The path of the entry in the bundle.</param>
/// <param name="Data">The uncompressed content.</param>
/// <param name="Type">The entry type.</param>
/// <param name="Compress"><see langword="true"/> to store the content deflate-compressed.</param>
internal readonly record struct BundleFile(string RelativePath, byte[] Data, BundleFileType Type, bool Compress = false);