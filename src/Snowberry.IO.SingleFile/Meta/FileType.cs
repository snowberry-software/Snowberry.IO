namespace Snowberry.IO.SingleFile.Meta;

// https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileType.cs

/// <summary>
/// The type of a file in a single-file bundle.
/// </summary>
public enum FileType : byte
{
    /// <summary>
    /// Unknown file type.
    /// </summary>
    Unknown,

    /// <summary>
    /// Assembly file.
    /// </summary>
    Assembly,

    /// <summary>
    /// A native binary.
    /// </summary>
    NativeBinary,

    /// <summary>
    /// The *.deps.json file.
    /// </summary>
    DepsJson,

    /// <summary>
    /// The *.runtimeconfig.json file.
    /// </summary>
    RuntimeConfigJson,

    /// <summary>
    /// Debug symbols file.
    /// </summary>
    Symbols
}