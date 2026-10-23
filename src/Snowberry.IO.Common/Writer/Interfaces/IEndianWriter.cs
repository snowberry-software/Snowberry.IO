using System;
using System.IO;
#if NET9_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif
using System.Text;

namespace Snowberry.IO.Common.Writer.Interfaces;

/// <summary>
/// Supports writing with different endian type.
/// </summary>
public interface IEndianWriter : IDisposable
{
    /// <inheritdoc cref="BinaryWriter.Write(string)"/>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(string value);

    /// <inheritdoc cref="BinaryWriter.Write(byte)"/>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(byte value);

    /// <inheritdoc cref="BinaryWriter.Write(sbyte)"/>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(sbyte value);

    /// <summary>
    /// Writes a <see cref="Guid" />.
    /// </summary>
    /// <remarks>
    /// With <see cref="EndianType.LITTLE"/>, the bytes match <see cref="Guid.ToByteArray()"/>. With <see cref="EndianType.BIG"/>, the layout is mixed:
    /// the first three fields (<c>Data1</c>, <c>Data2</c> and <c>Data3</c>) are written in big-endian order and the last 8 bytes as they are,
    /// which is the RFC 4122 byte order.
    /// </remarks>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(Guid value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes a <see cref="double" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(double value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes a <see cref="float" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(float value, EndianType endian = EndianType.LITTLE);

#if NET5_0_OR_GREATER
    // Half converts implicitly from byte and sbyte. The lower priority keeps a Write(byte, EndianType) call bound to
    // Write(short, EndianType) instead of making it ambiguous with this overload.

    /// <summary>
    /// Writes a <see cref="Half" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
#if NET9_0_OR_GREATER
    [OverloadResolutionPriority(-1)]
#endif
    IEndianWriter Write(Half value, EndianType endian = EndianType.LITTLE);
#endif

    /// <summary>
    /// Writes a <see cref="ulong" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(ulong value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes an <see cref="int" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(int value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes a <see cref="uint" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(uint value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes a <see cref="long" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(long value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes a <see cref="short" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(short value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes a <see cref="ushort" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(ushort value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes a <see cref="Sha1" />.
    /// </summary>
    /// <remarks>Writes the 20 bytes returned by <see cref="Sha1.GetHashBuffer"/>.</remarks>
    /// <param name="value">The value.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(Sha1 value);

    /// <inheritdoc cref="BinaryWriter.Write(byte[])"/>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(byte[] buffer);

    /// <summary>
    /// Writes a one-byte <see cref="bool"/> value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(bool value);

    /// <inheritdoc cref="BinaryWriter.Write7BitEncodedInt(int)"/>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write7BitEncodedInt(int value);

    /// <summary>
    /// Writes a 64-bit signed integer in a compressed 7-bit encoded format, one <see langword="byte"/> at a time, starting with the least significant bits.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write7BitEncodedInt64(long value);

    /// <summary>
    /// Writes a zero terminated string.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>The current writer instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
    IEndianWriter WriteCString(string text);

    /// <summary>
    /// Writes <paramref name="text"/> followed by a new line (using <see cref="Environment.NewLine"/>).
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>The current writer instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
    IEndianWriter WriteLine(string text);

    /// <summary>
    /// Writes zero <see langword="byte"/> padding.
    /// </summary>
    /// <remarks>The underlying stream must be seekable.</remarks>
    /// <param name="alignment">The padding's alignment. Must be a non-zero power of two.</param>
    /// <returns>The current writer instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="alignment"/> is zero or not a power of two.</exception>
    IEndianWriter WritePadding(byte alignment);

    /// <summary>
    /// Writes a string with a fixed size.
    /// </summary>
    /// <remarks>
    /// The encoded text is followed by zero <see langword="byte"/>s up to <paramref name="size"/>, and the first of them acts as the null terminator.
    /// When the encoded length of <paramref name="text"/> equals <paramref name="size"/>, no terminator is written.
    /// </remarks>
    /// <param name="text">The text to write.</param>
    /// <param name="size">The fixed size of the payload, in bytes.</param>
    /// <returns>The current writer instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size"/> is smaller than the character length of <paramref name="text"/>, or when the encoded byte count of <paramref name="text"/> is greater than <paramref name="size"/>.</exception>
    IEndianWriter WriteSizedCString(string text, int size);

    /// <summary>
    /// Writes the characters of <paramref name="text"/>, encoded using <see cref="Encoding"/>, to the current stream.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>The current writer instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is <see langword="null"/>.</exception>
    IEndianWriter WriteStringCharacters(string text);

    /// <summary>
    /// Gets the encoding that will be used.
    /// </summary>
    Encoding Encoding { get; }

    /// <summary>
    /// Gets the length of the stream.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Gets or sets the current position of the writer.
    /// </summary>
    long Position { get; set; }
}