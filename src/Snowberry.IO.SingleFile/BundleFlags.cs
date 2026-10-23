namespace Snowberry.IO.SingleFile;

/// <summary>
/// The flags stored in the header of a single-file bundle.
/// </summary>
[Flags]
public enum BundleFlags : ulong
{
    /// <summary>
    /// No flags are set.
    /// </summary>
    None = 0,

    /// <summary>
    /// The bundle runs in .NET Core 3.x compatibility mode, which extracts all files to disk.
    /// </summary>
    NetCoreApp3CompatMode = 1
}