using System;
using System.IO;

namespace Snowberry.IO.Common.Reader.Interfaces;

// NOTE(VNC): The endian parameter is required on purpose. With a default value, overload resolution can pick an endian
// overload where the BinaryReader member was meant (the writer had Write(char) bind to Write(ushort, EndianType)).
public partial interface IEndianReader
{
    /// <summary>
    /// Reads a 64-bit floating-point value with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 64-bit floating-point value.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    double ReadDouble(EndianType endian);

    /// <summary>
    /// Reads a <see cref="Guid"/> in the little-endian layout of <see cref="Guid.ToByteArray()"/>.
    /// </summary>
    /// <returns>The read <see cref="Guid"/>.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    Guid ReadGuid();

    /// <summary>
    /// Reads a <see cref="Guid"/> with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <remarks>
    /// With <see cref="EndianType.BIG"/>, the first three fields (<c>Data1</c>, <c>Data2</c> and <c>Data3</c>) are read in big-endian order
    /// and the last 8 bytes as they are, which is the RFC 4122 byte order.
    /// </remarks>
    /// <returns>The read <see cref="Guid"/>.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    Guid ReadGuid(EndianType endian);

#if NET5_0_OR_GREATER
    /// <summary>
    /// Reads a half-precision floating-point value with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read half-precision floating-point value.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    Half ReadHalf(EndianType endian);
#endif
    /// <summary>
    /// Reads a 16-bit signed integer with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 16-bit signed integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    short ReadInt16(EndianType endian);

    /// <summary>
    /// Reads a 32-bit signed integer with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 32-bit signed integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    int ReadInt32(EndianType endian);

    /// <summary>
    /// Reads a 64-bit signed integer with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 64-bit signed integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    long ReadInt64(EndianType endian);

    /// <summary>
    /// Reads a 32-bit floating-point value with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 32-bit floating-point value.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    float ReadSingle(EndianType endian);

    /// <summary>
    /// Reads a 16-bit unsigned integer with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 16-bit unsigned integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    ushort ReadUInt16(EndianType endian);

    /// <summary>
    /// Reads a 32-bit unsigned integer with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 32-bit unsigned integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    uint ReadUInt32(EndianType endian);

    /// <summary>
    /// Reads a 64-bit unsigned integer with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 64-bit unsigned integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    ulong ReadUInt64(EndianType endian);
}