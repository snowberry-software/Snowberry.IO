using System.Buffers.Text;

namespace Snowberry.IO.SingleFile.Format;

/// <summary>
/// Constants and identifiers of the single-file bundle format, as written by the .NET SDK bundler.
/// </summary>
/// <remarks>
/// The application host contains an 8-byte manifest offset followed by a 32-byte signature. Bundled files follow the
/// host, and the manifest follows the files. See
/// https://github.com/dotnet/runtime/tree/main/src/installer/managed/Microsoft.NET.HostModel/Bundle.
/// </remarks>
internal static class BundleFormat
{
    /// <summary>
    /// The length of the identifiers that <see cref="CreateBundleId"/> creates.
    /// </summary>
    public const int c_BundleIdLength = 12;

    /// <summary>
    /// The length in bytes of the manifest offset stored before the signature.
    /// </summary>
    public const int c_ManifestOffsetLength = sizeof(long);

    /// <summary>
    /// The length in bytes of <see cref="Signature"/>.
    /// </summary>
    public const int c_SignatureLength = 32;

    /// <summary>
    /// Creates the bundle identifier from the SHA-256 hash of the per-file content hashes, as the .NET 10 SDK bundler does.
    /// </summary>
    /// <remarks>
    /// The identifier is the first 12 characters of the Base64Url encoding of the hash. The .NET 6 to 9 SDKs wrote a 32-character identifier
    /// instead: the standard Base64 encoding without its first 12 characters, with <c>/</c> replaced by <c>_</c>.
    /// </remarks>
    /// <param name="bundleHash">The SHA-256 hash of the per-file content hashes.</param>
    /// <returns>The bundle identifier.</returns>
    public static string CreateBundleId(ReadOnlySpan<byte> bundleHash)
    {
        return Base64Url.EncodeToString(bundleHash)[..c_BundleIdLength];
    }

    /// <summary>
    /// Determines whether a bundle with the given major version can be read and written.
    /// </summary>
    /// <param name="majorVersion">The major version from the bundle header.</param>
    /// <returns><see langword="true"/> if the version is supported; otherwise, <see langword="false"/>.</returns>
    public static bool IsSupportedMajorVersion(uint majorVersion)
    {
        return majorVersion is 1 or 2 or 6;
    }

    /// <summary>
    /// Gets the 32-byte signature that marks an application host as a bundle; the manifest offset is stored just before it.
    /// </summary>
    /// <remarks>
    /// The signature is the SHA-256 hash of <c>.net core bundle</c>. See
    /// https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs.
    /// </remarks>
    public static ReadOnlySpan<byte> Signature =>
    [
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    ];
}