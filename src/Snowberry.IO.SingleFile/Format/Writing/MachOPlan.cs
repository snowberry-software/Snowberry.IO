using Snowberry.IO.SingleFile.Format.Hosts;

namespace Snowberry.IO.SingleFile.Format.Writing;

/// <summary>
/// How a bundle with a macOS host is written: the load commands are updated so that the <c>__LINKEDIT</c> segment and the string table
/// cover the bundle, and an ad-hoc code signature is appended unless signing is disabled.
/// </summary>
/// <param name="Image">The load commands of the host.</param>
/// <param name="Identifier">The signing identifier, or <see langword="null"/> to write the executable without a code signature.</param>
/// <param name="BundleEnd">The end of the manifest.</param>
/// <param name="SignatureOffset">The offset of the code signature, aligned to 16 bytes; <paramref name="BundleEnd"/> when not signing.</param>
/// <param name="SignatureLength">The length of the code signature, or <c>0</c> when not signing.</param>
internal sealed record MachOPlan(MachOImage Image, string? Identifier, long BundleEnd, long SignatureOffset, long SignatureLength)
{
    /// <summary>
    /// Gets the end of the written file.
    /// </summary>
    public long End => SignatureOffset + SignatureLength;
}
