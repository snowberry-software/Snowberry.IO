using System;
using System.IO;
using System.Text;

namespace Snowberry.IO.Common.Reader.Interfaces;

/// <summary>
/// Binary reader with different endianness support.
/// </summary>
public partial interface IEndianReader : IDisposable
{
    /// <summary>
    /// Reads bytes from the current stream and writes them to the specified destination stream.
    /// </summary>
    /// <param name="destination">The stream to which the contents of the current stream will be copied.</param>
    void CopyTo(Stream destination);

    /// <summary>
    /// Reads a specified number of bytes from the current stream and writes them to the specified destination stream.
    /// </summary>
    /// <param name="destination">The stream to which the contents of the current stream will be copied.</param>
    /// <param name="length">The number of bytes to copy.</param>
    /// <param name="bufferSize">The size of the buffer in bytes. This value must be greater than zero.</param>
    void CopyTo(Stream destination, int length, int bufferSize = 0x14000);

    /// <summary>
    /// Reads a single byte from the current stream.
    /// </summary>
    /// <remarks>
    /// This method does not check if the current <see cref="Position"/> is greater than the current <see cref="Length"/>.
    /// Avoid using this method in loops as it can lead to infinite loops if the end of the stream is reached.
    /// </remarks>
    /// <returns>The byte read.</returns>
    byte ReadByte();

    /// <summary>
    /// Reads a single byte from the current stream, returning -1 if the <see cref="Position"/> is greater than the <see cref="Length"/>.
    /// </summary>
    /// <returns>The byte read as an <see cref="int"/>, or -1 if the end of the stream is reached.</returns>
    int ReadByteSafe();

    /// <summary>
    /// Reads a specified number of bytes from the current stream into a buffer.
    /// </summary>
    /// <param name="buffer">The buffer to store the read bytes.</param>
    /// <param name="offset">The zero-based byte offset in the buffer at which to begin storing the data read from the stream.</param>
    /// <param name="byteCount">The maximum number of bytes to read from the current stream.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero if the end of the stream is reached.
    /// </returns>
    int Read(byte[] buffer, int offset, int byteCount);

    /// <summary>
    /// Reads a specified number of bytes from the current stream into the internal buffer.
    /// </summary>
    /// <param name="byteCount">The number of bytes to read.</param>
    /// <param name="offset">The zero-based byte offset in the buffer at which to begin storing the data.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero if the end of the stream is reached.
    /// </returns>
    int ReadInInternalBuffer(int byteCount, int offset);

    /// <summary>
    /// Reads a specified number of bytes from the current stream.
    /// </summary>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>A byte array containing the read bytes.</returns>
    byte[] ReadBytes(int count);

    /// <summary>
    /// Reads a null-terminated string from the current stream.
    /// </summary>
    /// <returns>A zero-terminated string, or <see langword="null"/> if the end of the stream is reached.</returns>
    string? ReadCString();

    /// <summary>
    /// Reads a fixed-size null-terminated string from the current stream.
    /// </summary>
    /// <remarks>
    /// The reader's <see cref="Position"/> will be adjusted by the specified <paramref name="size"/>.<br/>
    /// For example, if the read string is "Hello" (length 5) but the expected size is 8, the <see cref="Position"/> will be increased by the remaining 3 bytes.
    /// </remarks>
    /// <param name="size">The size of the string to read.</param>
    /// <param name="adjustPosition">Automatically adjust the <see cref="Position"/> by the unread count of <paramref name="size"/>.</param>
    /// <returns>The fixed-size string.</returns>
    string? ReadSizedCString(int size, bool adjustPosition = true);

    /// <summary>
    /// Reads a size-prefixed string from the current stream.
    /// </summary>
    /// <returns>The size-prefixed string, or <see langword="null"/> if the end of the stream is reached.</returns>
    string? ReadString();

    /// <summary>
    /// Reads a line of characters from the current stream.
    /// </summary>
    /// <remarks>
    /// Supported line endings:
    /// <list type="bullet">
    ///     <item>CR LF</item>
    ///     <item>LF</item>
    /// </list>
    /// </remarks>
    /// <returns>The next line from the input stream, or <see langword="null"/> if the end of the stream is reached.</returns>
    string? ReadLine();

    /// <summary>
    /// Reads a <see cref="Sha1"/> hash from the current stream.
    /// </summary>
    /// <returns>The read <see cref="Sha1"/> hash.</returns>
    Sha1 ReadSha1();

    /// <summary>
    /// Reads padding bytes aligned to the specified alignment.
    /// </summary>
    /// <param name="alignment">The alignment for the padding.</param>
    void ReadAlignment(byte alignment);

    /// <summary>
    /// Reads a 64-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 64-bit signed integer.</returns>
    long ReadLong(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads a 64-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 64-bit unsigned integer.</returns>
    ulong ReadULong(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads a 32-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 32-bit unsigned integer.</returns>
    uint ReadUInt32(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads a 16-bit unsigned integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 16-bit unsigned integer.</returns>
    ushort ReadUInt16(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads a 32-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 32-bit signed integer.</returns>
    int ReadInt32(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads a 16-bit signed integer from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 16-bit signed integer.</returns>
    short ReadInt16(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads a <see cref="Guid"/> from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read <see cref="Guid"/>.</returns>
    Guid ReadGuid(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads a 32-bit floating-point value from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 32-bit floating-point value.</returns>
    float ReadFloat(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads a 64-bit floating-point value from the current stream with the specified endianness.
    /// </summary>
    /// <param name="endian">The endianness to use.</param>
    /// <returns>The read 64-bit floating-point value.</returns>
    double ReadDouble(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads the remaining bytes from the current position to the end of the stream into a byte array.
    /// </summary>
    /// <param name="maxBufferSize">The maximum buffer size in bytes.</param>
    /// <returns>A byte array containing the remaining bytes in the stream.</returns>
    byte[] ReadUntilEnd(int maxBufferSize = 16 * 1024);

    /// <summary>
    /// Reads a <see cref="bool"/> value from the current stream and advances the position by one byte.
    /// </summary>
    /// <returns><see langword="true"/> if the byte is non-zero; otherwise, <see langword="false"/>.</returns>
    bool ReadBool();

    /// <summary>
    /// Reads a 64-bit integer in a compressed format from the current stream.
    /// </summary>
    /// <remarks>
    /// For more information, see <see href="https://en.wikipedia.org/wiki/LEB128">LEB128</see>.
    /// </remarks>
    /// <returns>The read 64-bit integer in compressed format.</returns>
    long Read7BitEncodedLong();

    /// <summary>
    /// Reads a 32-bit integer in a compressed format from the current stream.
    /// </summary>
    /// <remarks>
    /// For more information, see <see href="https://en.wikipedia.org/wiki/LEB128">LEB128</see>.
    /// </remarks>
    /// <returns>The read 32-bit integer in compressed format.</returns>
    int Read7BitEncodedInt();

    /// <summary>
    /// Enables a view mode that restricts access to a specific region of the stream.
    /// </summary>
    /// <param name="region">The region to restrict access to.</param>
    void EnableRegionView(RegionRange region);

    /// <summary>
    /// Ensures that the internal buffer size is at least the specified size.
    /// If the current buffer is smaller, a new buffer will be initialized.
    /// </summary>
    /// <param name="bufferSize">The minimum buffer size in bytes.</param>
    void EnsureBufferSize(int bufferSize);

    /// <summary>
    /// Disables the region view mode.
    /// </summary>
    void DisableRegionView();

    /// <summary>
    /// Gets a value indicating whether the reader can read any new data.
    /// </summary>
    bool CanReadData { get; }

    /// <summary>
    /// Gets the length of the stream.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Gets or sets the position within the stream.
    /// </summary>
    long Position { get; set; }

    /// <summary>
    /// Gets the actual current position in the stream, without adjustments from region view mode.
    /// </summary>
    long ActualPosition { get; }

    /// <summary>
    /// Gets the actual current length of the stream, without adjustments from region view mode.
    /// </summary>
    long ActualLength { get; }

    /// <summary>
    /// Gets a value indicating whether the reader is in region view mode.
    /// </summary>
    bool IsRegionViewEnabled { get; }

    /// <summary>
    /// Gets the current region view of the stream.
    /// </summary>
    RegionRange RegionView { get; }

    /// <summary>
    /// Gets the internal buffer of the reader.
    /// </summary>
#pragma warning disable CA1819 // Properties should not return arrays
    byte[] Buffer { get; }
#pragma warning restore CA1819 // Properties should not return arrays

    /// <summary>
    /// Gets a value indicating whether the reader is disposed.
    /// </summary>
    bool Disposed { get; }

    /// <summary>
    /// Gets the encoding used by the reader.
    /// </summary>
    Encoding Encoding { get; }

    /// <summary>
    /// Gets or sets the current analyzer used by the reader.
    /// </summary>
    Analyzer? Analyzer { get; set; }
}
