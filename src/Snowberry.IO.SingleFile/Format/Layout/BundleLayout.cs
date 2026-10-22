using Snowberry.IO.SingleFile.Format.Hosts;
using Snowberry.IO.SingleFile.Format.Manifests;

namespace Snowberry.IO.SingleFile.Format.Layout;

/// <summary>
/// The validated layout of an opened bundle.
/// </summary>
/// <param name="SignatureOffset">The offset of the bundle signature inside the application host.</param>
/// <param name="ManifestOffset">The offset of the manifest.</param>
/// <param name="BundleOffset">The offset where bundled file data starts; everything before it belongs to the application host.</param>
/// <param name="AssemblyAlignment">The assembly alignment of the existing bundle, or of its application host if it has no uncompressed assemblies.</param>
/// <param name="Host">The parsed application host.</param>
/// <param name="Manifest">The parsed manifest.</param>
internal sealed record BundleLayout(long SignatureOffset, long ManifestOffset, long BundleOffset, int AssemblyAlignment, HostImage Host, Manifest Manifest);