using System;

namespace Snowberry.IO.Common;

/// <summary>
/// Provides the byte order of the host.
/// </summary>
internal static class HostByteOrder
{
#if NETSTANDARD2_0
    // NOTE(VNC): Reading BitConverter.IsLittleEndian can cost a call to the static base helper on .NET Framework, while a static
    // readonly field of this assembly folds to a constant. It lives in its own class so BinaryEndianConverter needs no static constructor.
    private static readonly bool s_IsLittleEndian = BitConverter.IsLittleEndian;

    /// <summary>
    /// Gets a value indicating whether the host stores multi-byte values with the least significant byte first.
    /// </summary>
    public static bool IsLittleEndian => s_IsLittleEndian;
#else
    /// <summary>
    /// Gets a value indicating whether the host stores multi-byte values with the least significant byte first.
    /// </summary>
    public static bool IsLittleEndian => BitConverter.IsLittleEndian;
#endif

    /// <summary>
    /// Gets the <see cref="EndianType"/> of the host.
    /// </summary>
    public static EndianType Endian => IsLittleEndian ? EndianType.LITTLE : EndianType.BIG;
}