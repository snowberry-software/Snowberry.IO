namespace Snowberry.IO.SingleFile;

/// <summary>
/// The header of a single-file bundle manifest.
/// </summary>
/// <param name="MajorVersion">
/// The bundle format major version: <c>1</c> for .NET Core 3.x, <c>2</c> for .NET 5 and <c>6</c> for .NET 6 and later.
/// </param>
/// <param name="MinorVersion">The bundle format minor version.</param>
/// <param name="Id">
/// The bundle identifier. The runtime uses it to name the directory it extracts native files to, so it changes whenever the bundled content changes.
/// </param>
/// <param name="Flags">The bundle flags; always <see cref="BundleFlags.None"/> for format version 1.</param>
public sealed record BundleHeader(uint MajorVersion, uint MinorVersion, string Id, BundleFlags Flags)
{
    /// <summary>
    /// Gets a value indicating whether the bundle format supports compressed entries, which requires format version 6 or later.
    /// </summary>
    public bool SupportsCompression => MajorVersion >= 6;
}