namespace Snowberry.IO.SingleFile;

// https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileType.cs

/// <summary>
/// The type of a file stored in a single-file bundle.
/// </summary>
public enum BundleFileType : byte
{
    /// <summary>
    /// A file of unknown type, such as a content or resource file.
    /// </summary>
    Unknown,

    /// <summary>
    /// A managed assembly. Uncompressed assemblies are aligned when the bundle is written, so the runtime can map them directly.
    /// </summary>
    Assembly,

    /// <summary>
    /// A native library.
    /// </summary>
    NativeBinary,

    /// <summary>
    /// The application's <c>*.deps.json</c> file. It is never compressed.
    /// </summary>
    DepsJson,

    /// <summary>
    /// The application's <c>*.runtimeconfig.json</c> file. It is never compressed.
    /// </summary>
    RuntimeConfigJson,

    /// <summary>
    /// A debug symbols file.
    /// </summary>
    Symbols
}