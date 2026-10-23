namespace Snowberry.IO.SingleFile.Format.Writing;

/// <summary>
/// How the payload of a <see cref="PlannedEntry"/> is written.
/// </summary>
internal enum PayloadKind
{
    /// <summary>The stored bytes are copied unchanged, compressed or not.</summary>
    CopyStored,

    /// <summary>Compressed bytes are decompressed while copying.</summary>
    Decompress,

    /// <summary>The payload was compressed in advance into a buffer.</summary>
    Buffer
}