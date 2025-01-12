namespace Snowberry.IO.SingleFile;

// https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/TargetInfo.cs#L58
public enum TargetInfo
{
    /// <summary>
    /// Applies no padding to the bundle file entries.
    /// </summary>
    Unknown,

    Other,
    Windows,
    Arm64
}
