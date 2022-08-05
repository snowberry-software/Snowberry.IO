using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snowberry.IO.Writer.Interfaces;

/// <summary>
/// Supports writing with different endian type.
/// </summary>
public interface IEndianWriter : IDisposable
{
    /// <inheritdoc cref="BinaryWriter.Write7BitEncodedInt(int)"/>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write7BitEncodedInt(int value);

    /// <inheritdoc cref="BinaryWriter.Write7BitEncodedInt64(long)"/>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write7BitEncodedInt64(long value);

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

    /// <summary>
    /// Writes a <see cref="ulong" />.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(ulong value, EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Writes a <see cref="int" />.
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
    /// <param name="value">The value.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(Sha1 value);

    /// <summary>
    /// Writes a string with a fixed size.
    /// </summary>
    /// <remarks>When the string length isn't equal to the size, the remaining amount of the length will be used to fill the rest with zero <see langword="byte"/> characters.</remarks>
    /// <param name="text">The text to write.</param>
    /// <param name="size">The fixed size of the payload.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter WriteSizedCString(string text, int size);

    /// <summary>
    /// Writes a new line (using <see cref="Environment.NewLine"/>).
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter WriteLine(string text);

    /// <summary>
    /// Writes a span of characters to the current stream.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter WriteStringCharacters(string text);

    /// <summary>
    /// Writes a zero terminated string.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter WriteCString(string text);

    /// <summary>
    /// Writes zero <see langword="byte"/> padding.
    /// </summary>
    /// <param name="alignment">The padding's alignment.</param>
    /// <returns>The current writer instance.</returns>
    IEndianWriter WritePadding(byte alignment);

    /// <inheritdoc cref="BinaryWriter.Write(byte[])"/>
    /// <returns>The current writer instance.</returns>
    IEndianWriter Write(byte[] buffer);

    /// <summary>
    /// The current position of the writer.
    /// </summary>
    long Position { get; set; }

    /// <summary>
    /// The length of the stream.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// The encoding that will be used.
    /// </summary>
    Encoding Encoding { get; }
}
