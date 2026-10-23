using System;
using System.IO;

namespace Snowberry.IO.Common.Reader.Interfaces;

// NOTE(VNC): These members have the names and semantics of System.IO.BinaryReader, whose public members implement them
// on readers derived from it. Multi-byte values are read in little-endian byte order.
public partial interface IEndianReader
{
    /// <summary>
    /// Returns the next available character without advancing the position.
    /// </summary>
    /// <returns>The next available character, or -1 if no more characters are available or the stream does not support seeking.</returns>
    /// <exception cref="ArgumentException">The current character cannot be decoded with <see cref="Encoding"/>.</exception>
    int PeekChar();

    /// <summary>
    /// Reads the next character and advances the position according to <see cref="Encoding"/>.
    /// </summary>
    /// <returns>The next character, or -1 if no more characters are available.</returns>
    int Read();

    /// <summary>
    /// Reads up to <paramref name="count"/> characters into <paramref name="buffer"/>, starting at <paramref name="index"/>.
    /// </summary>
    /// <param name="buffer">The buffer that receives the characters.</param>
    /// <param name="index">The starting index in <paramref name="buffer"/>.</param>
    /// <param name="count">The maximum number of characters to read.</param>
    /// <returns>The number of characters read, which is zero at the end of the stream.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> or <paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="buffer"/> is too small for <paramref name="index"/> and <paramref name="count"/>.</exception>
    int Read(char[] buffer, int index, int count);

    /// <summary>
    /// Reads up to <paramref name="count"/> bytes into <paramref name="buffer"/>, starting at <paramref name="index"/>.
    /// </summary>
    /// <param name="buffer">The buffer that receives the bytes.</param>
    /// <param name="index">The starting index in <paramref name="buffer"/>.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero if the end of the stream is reached.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> or <paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="buffer"/> is too small for <paramref name="index"/> and <paramref name="count"/>.</exception>
    int Read(byte[] buffer, int index, int count);

    /// <summary>
    /// Reads a 32-bit integer in a compressed format from the current stream.
    /// </summary>
    /// <remarks>
    /// For more information, see <see href="https://en.wikipedia.org/wiki/LEB128">LEB128</see>.
    /// </remarks>
    /// <returns>The read 32-bit integer in compressed format.</returns>
    /// <exception cref="FormatException">Thrown when the encoded value uses more than 5 bytes.</exception>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    int Read7BitEncodedInt();

    /// <summary>
    /// Reads a 64-bit integer in a compressed format from the current stream.
    /// </summary>
    /// <remarks>
    /// For more information, see <see href="https://en.wikipedia.org/wiki/LEB128">LEB128</see>.
    /// </remarks>
    /// <returns>The read 64-bit integer in compressed format.</returns>
    /// <exception cref="FormatException">Thrown when the encoded value uses more than 10 bytes.</exception>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    long Read7BitEncodedInt64();

    /// <summary>
    /// Reads a <see cref="bool"/> value and advances the position by one byte.
    /// </summary>
    /// <returns><see langword="true"/> if the byte is non-zero; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    bool ReadBoolean();

    /// <summary>
    /// Reads a single byte from the current stream.
    /// </summary>
    /// <returns>The byte read.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    byte ReadByte();

    /// <summary>
    /// Reads the specified number of bytes from the current stream.
    /// </summary>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>A byte array containing the read bytes. This might be less than the number of bytes requested if the end of the stream is reached.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    byte[] ReadBytes(int count);

    /// <summary>
    /// Reads the next character and advances the position according to <see cref="Encoding"/>.
    /// </summary>
    /// <returns>The character read.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the character could be read.</exception>
    /// <exception cref="ArgumentException">A surrogate character was read.</exception>
    char ReadChar();

    /// <summary>
    /// Reads the specified number of characters and advances the position according to <see cref="Encoding"/>.
    /// </summary>
    /// <param name="count">The number of characters to read.</param>
    /// <returns>The characters read; fewer than <paramref name="count"/> if the end of the stream is reached.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    char[] ReadChars(int count);

    /// <summary>
    /// Reads a <see cref="decimal"/> value and advances the position by sixteen bytes.
    /// </summary>
    /// <returns>The <see cref="decimal"/> value read.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    /// <exception cref="IOException">The bytes do not represent a valid <see cref="decimal"/>.</exception>
    decimal ReadDecimal();

    /// <summary>
    /// Reads a little-endian 64-bit floating-point value.
    /// </summary>
    /// <returns>The read 64-bit floating-point value.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    double ReadDouble();

#if NET5_0_OR_GREATER
    /// <summary>
    /// Reads a little-endian half-precision floating-point value.
    /// </summary>
    /// <returns>The read half-precision floating-point value.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    Half ReadHalf();
#endif

    /// <summary>
    /// Reads a little-endian 16-bit signed integer.
    /// </summary>
    /// <returns>The read 16-bit signed integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    short ReadInt16();

    /// <summary>
    /// Reads a little-endian 32-bit signed integer.
    /// </summary>
    /// <returns>The read 32-bit signed integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    int ReadInt32();

    /// <summary>
    /// Reads a little-endian 64-bit signed integer.
    /// </summary>
    /// <returns>The read 64-bit signed integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    long ReadInt64();

    /// <summary>
    /// Reads a signed byte from the current stream.
    /// </summary>
    /// <returns>A signed byte read from the current stream.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    sbyte ReadSByte();

    /// <summary>
    /// Reads a little-endian 32-bit floating-point value.
    /// </summary>
    /// <returns>The read 32-bit floating-point value.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    float ReadSingle();

    /// <summary>
    /// Reads a string prefixed with its length in bytes, encoded as a 7-bit encoded integer.
    /// </summary>
    /// <returns>The string read.</returns>
    /// <exception cref="IOException">Thrown when the encoded string length is negative.</exception>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the full string could be read.</exception>
    /// <exception cref="FormatException">Thrown when the encoded string length uses more than 5 bytes.</exception>
    string ReadString();

    /// <summary>
    /// Reads a little-endian 16-bit unsigned integer.
    /// </summary>
    /// <returns>The read 16-bit unsigned integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    ushort ReadUInt16();

    /// <summary>
    /// Reads a little-endian 32-bit unsigned integer.
    /// </summary>
    /// <returns>The read 32-bit unsigned integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    uint ReadUInt32();

    /// <summary>
    /// Reads a little-endian 64-bit unsigned integer.
    /// </summary>
    /// <returns>The read 64-bit unsigned integer.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    ulong ReadUInt64();
}