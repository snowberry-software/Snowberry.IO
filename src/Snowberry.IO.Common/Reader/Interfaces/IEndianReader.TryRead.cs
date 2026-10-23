using System;

namespace Snowberry.IO.Common.Reader.Interfaces;

// NOTE(VNC): Like the read methods, every TryRead* method has a little-endian overload without an endian parameter and,
// for multi-byte values, an overload with a required EndianType parameter.
public partial interface IEndianReader
{
    /// <summary>
    /// Tries to read a single byte from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is not changed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadByte(out byte value);

    /// <summary>
    /// Tries to read a little-endian 64-bit floating-point value from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadDouble(out double value);

    /// <summary>
    /// Tries to read a 64-bit floating-point value from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadDouble(out double value, EndianType endian);

    /// <summary>
    /// Tries to read a <see cref="Guid"/> in the little-endian layout of <see cref="Guid.ToByteArray()"/> from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or <see cref="Guid.Empty"/> if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadGuid(out Guid value);

    /// <summary>
    /// Tries to read a <see cref="Guid"/> from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>
    /// The byte order is the one of <see cref="ReadGuid(EndianType)"/>.
    /// When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.
    /// </remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or <see cref="Guid.Empty"/> if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadGuid(out Guid value, EndianType endian);

    /// <summary>
    /// Tries to read a little-endian 16-bit signed integer from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt16(out short value);

    /// <summary>
    /// Tries to read a 16-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt16(out short value, EndianType endian);

    /// <summary>
    /// Tries to read a little-endian 32-bit signed integer from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt32(out int value);

    /// <summary>
    /// Tries to read a 32-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt32(out int value, EndianType endian);

    /// <summary>
    /// Tries to read a little-endian 64-bit signed integer from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt64(out long value);

    /// <summary>
    /// Tries to read a 64-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt64(out long value, EndianType endian);

    /// <summary>
    /// Tries to read a signed byte from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is not changed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadSByte(out sbyte value);

    /// <summary>
    /// Tries to read a <see cref="Sha1"/> hash from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or <see cref="Sha1.s_Zero"/> if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadSha1(out Sha1 value);

    /// <summary>
    /// Tries to read a little-endian 32-bit floating-point value from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadSingle(out float value);

    /// <summary>
    /// Tries to read a 32-bit floating-point value from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadSingle(out float value, EndianType endian);

    /// <summary>
    /// Tries to read a little-endian 16-bit unsigned integer from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt16(out ushort value);

    /// <summary>
    /// Tries to read a 16-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt16(out ushort value, EndianType endian);

    /// <summary>
    /// Tries to read a little-endian 32-bit unsigned integer from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt32(out uint value);

    /// <summary>
    /// Tries to read a 32-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt32(out uint value, EndianType endian);

    /// <summary>
    /// Tries to read a little-endian 64-bit unsigned integer from the current stream.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt64(out ulong value);

    /// <summary>
    /// Tries to read a 64-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <remarks>When the read fails, <see cref="Position"/> is restored only when the underlying data source can seek; otherwise the bytes read before the end of the data are consumed.</remarks>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt64(out ulong value, EndianType endian);
}