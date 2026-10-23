namespace Snowberry.IO.SingleFile.Format.Manifests;

/// <summary>
/// The parsed bundle manifest.
/// </summary>
internal sealed record Manifest(BundleHeader Header, List<ManifestEntry> Entries);