namespace Snowberry.IO.SingleFile.Format.Manifests;

/// <summary>
/// The parsed bundle manifest.
/// </summary>
/// <param name="Header">The bundle header that precedes the file records.</param>
/// <param name="Entries">The file records, in manifest order.</param>
internal sealed record Manifest(BundleHeader Header, List<ManifestEntry> Entries);