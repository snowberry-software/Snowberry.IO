using System.Text;

namespace Snowberry.IO.Utils;

/// <summary>
/// Encoding defaults shared by the readers and writers.
/// </summary>
internal static class EncodingDefaults
{
    /// <summary>
    /// UTF-8 without a byte order mark, the default encoding of the readers and writers on every target framework.
    /// </summary>
    public static readonly UTF8Encoding s_Utf8 = new(encoderShouldEmitUTF8Identifier: false);
}