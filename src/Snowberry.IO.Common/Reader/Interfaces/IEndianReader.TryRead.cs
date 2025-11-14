using System;

namespace Snowberry.IO.Common.Reader.Interfaces;

public partial interface IEndianReader
{
    /// <summary>
    /// Tries to read a 16-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt16(out short value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a 16-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt16(out ushort value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a 32-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt32(out int value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a 32-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt32(out uint value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a 64-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadInt64(out long value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a 64-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadUInt64(out ulong value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a 32-bit floating-point value from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadFloat(out float value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a 64-bit floating-point value from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadDouble(out double value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a <see cref="Guid"/> from the current stream with the specified endianness.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or <see cref="Guid.Empty"/> if the read failed.</param>
    /// <param name="endian">The endianness to use.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadGuid(out Guid value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Tries to read a <see cref="Sha1"/> hash from the current stream.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or <see cref="Sha1.Zero"/> if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadSha1(out Sha1 value);

    /// <summary>
    /// Tries to read a byte from the current stream.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadByte(out byte value);

    /// <summary>
    /// Tries to read a signed byte from the current stream.
    /// </summary>
    /// <param name="value">When this method returns, contains the value read from the stream, or zero if the read failed.</param>
    /// <returns><see langword="true"/> if the value was successfully read; otherwise, <see langword="false"/>.</returns>
    bool TryReadSByte(out sbyte value);
}
