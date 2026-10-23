using System;
using System.IO;
using System.Text;

namespace Snowberry.IO.Common.Reader.Interfaces;

/// <summary>
/// Binary reader with different endianness support, region views and an <see cref="Reader.Analyzer"/>.
/// </summary>
/// <remarks>
/// The members named like the ones of <see cref="BinaryReader"/>, such as <see cref="ReadInt32()"/> or <see cref="ReadString"/>,
/// behave like them and read little-endian data; the overloads that take an <see cref="EndianType"/>, such as
/// <see cref="ReadInt32(EndianType)"/>, read the given byte order. Every read honors the region view and is reported to <see cref="Analyzer"/>.
/// </remarks>
public partial interface IEndianReader : IDisposable
{
    /// <summary>
    /// Reads bytes from the current stream and writes them to the specified destination stream.
    /// </summary>
    /// <remarks>Only the remaining bytes of the current <see cref="Position"/> up to <see cref="Length"/> are copied, honoring an active <see cref="RegionView"/>. Requires a seekable stream.</remarks>
    /// <param name="destination">The stream to which the contents of the current stream will be copied.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
    void CopyTo(Stream destination);

    /// <summary>
    /// Reads a specified number of bytes from the current stream and writes them to the specified destination stream.
    /// </summary>
    /// <remarks>The copy is clamped to the remaining bytes of the current <see cref="Position"/> up to <see cref="Length"/>, honoring an active <see cref="RegionView"/>. Requires a seekable stream.</remarks>
    /// <param name="destination">The stream to which the contents of the current stream will be copied.</param>
    /// <param name="length">The number of bytes to copy.</param>
    /// <param name="bufferSize">The size of the buffer in bytes. This value must be greater than zero.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is negative, or when <paramref name="bufferSize"/> is not greater than zero.</exception>
    void CopyTo(Stream destination, int length, int bufferSize = 0x14000);

    /// <summary>
    /// Disables the region view mode.
    /// </summary>
    void DisableRegionView();

    /// <summary>
    /// Enables a view mode that restricts access to a specific region of the stream.
    /// </summary>
    /// <remarks>Requires a seekable stream.</remarks>
    /// <param name="region">The region to restrict access to.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="RegionRange.StartPosition"/> or <see cref="RegionRange.Size"/> of <paramref name="region"/> is negative,
    /// or when the region does not fit within <see cref="ActualLength"/>.
    /// </exception>
    void EnableRegionView(RegionRange region);

    /// <summary>
    /// Enables a view mode that restricts access to the given <paramref name="range"/> of the stream.
    /// </summary>
    /// <remarks>
    /// Indexes relative to the end, such as <c>^16</c> or an open end like <c>20..</c>, are resolved against <see cref="ActualLength"/>.
    /// Requires a seekable stream.
    /// </remarks>
    /// <param name="range">The range to restrict access to.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the end of <paramref name="range"/> resolves to a position before its start,
    /// or when the resolved region is not valid for <see cref="EnableRegionView(RegionRange)"/>.
    /// </exception>
    void EnableRegionView(Range range);

    /// <summary>
    /// Ensures that <see cref="Buffer"/> is at least the specified size.
    /// If the current buffer is smaller, a new buffer will be initialized.
    /// </summary>
    /// <param name="bufferSize">The minimum buffer size in bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="bufferSize"/> is negative.</exception>
    void EnsureBufferSize(int bufferSize);

    /// <summary>
    /// Advances the <see cref="Position"/> to the next boundary of the specified <paramref name="alignment"/>, skipping any padding bytes without reading them.
    /// </summary>
    /// <remarks>Requires a seekable stream.</remarks>
    /// <param name="alignment">The alignment, in bytes, to align the <see cref="Position"/> to. Must be a non-zero power of two.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="alignment"/> is zero or not a power of two.</exception>
    void ReadAlignment(byte alignment);

    /// <summary>
    /// Reads a single byte from the current stream, returning -1 at the end of the stream instead of throwing.
    /// </summary>
    /// <returns>The byte read as an <see cref="int"/>, or -1 if the end of the stream is reached.</returns>
    int ReadByteSafe();

    /// <summary>
    /// Reads a null-terminated string from the current stream.
    /// </summary>
    /// <remarks>
    /// The string ends at the first null code unit of <see cref="Encoding"/>, and <see cref="Position"/> ends right after it.
    /// The <see cref="Analyzer"/> receives the bytes up to and including the terminator, each before it is decoded.
    /// Requires a seekable stream.
    /// </remarks>
    /// <returns>A zero-terminated string.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before a null terminator is found.</exception>
    string ReadCString();

    /// <summary>
    /// Reads bytes from the current stream and advances the position within the stream until the specified number of bytes is read.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ReadExactly(Span{byte})"/>, a read that does not fit into an active <see cref="RegionView"/> throws before reading,
    /// and on a seekable stream <see cref="Position"/> is restored when the end of the stream is reached.
    /// </remarks>
    /// <param name="buffer">The byte array to which the data will be read. When this method returns, the specified region of this array is replaced by the bytes read from the current stream.</param>
    /// <param name="offset">The zero-based byte offset in the <paramref name="buffer"/> at which to begin storing the data read from the stream.</param>
    /// <param name="byteCount">The exact number of bytes to read from the stream.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buffer"/> is <see langword="null"/>.</exception>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before <paramref name="byteCount"/> bytes could be read.</exception>
    void ReadExactly(byte[] buffer, int offset, int byteCount);

    /// <summary>
    /// Reads a specified number of bytes from the current stream into <see cref="Buffer"/>.
    /// </summary>
    /// <param name="byteCount">The number of bytes to read.</param>
    /// <param name="offset">The zero-based byte offset in the buffer at which to begin storing the data.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero if the end of the stream is reached.
    /// </returns>
    int ReadInInternalBuffer(int byteCount, int offset);

    /// <summary>
    /// Reads a line of characters from the current stream.
    /// </summary>
    /// <remarks>
    /// Supported line endings:
    /// <list type="bullet">
    ///     <item>CR LF</item>
    ///     <item>LF</item>
    ///     <item>CR on its own</item>
    /// </list>
    /// The line ending is not part of the returned string, and <see cref="Position"/> ends right after it.
    /// The <see cref="Analyzer"/> receives the bytes of the line and its ending, each before it is decoded. After a CR, the next
    /// code unit is also reported to check for an LF; when it is not an LF, it is reported again when it is read as part of the next line.
    /// Requires a seekable stream.
    /// </remarks>
    /// <returns>The next line from the input stream, or <see langword="null"/> if the end of the stream is reached.</returns>
    string? ReadLine();

    /// <summary>
    /// Reads a <see cref="Sha1"/> hash from the current stream.
    /// </summary>
    /// <returns>The read <see cref="Sha1"/> hash.</returns>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before the value could be read.</exception>
    Sha1 ReadSha1();

    /// <summary>
    /// Reads a fixed-size null-terminated string from the current stream.
    /// </summary>
    /// <remarks>
    /// The string ends at the first null code unit of <see cref="Encoding"/> or after <paramref name="size"/> bytes, whichever comes first.<br/>
    /// When <paramref name="adjustPosition"/> is <see langword="true"/>, <see cref="Position"/> always ends <paramref name="size"/> bytes after where it started.
    /// For example, if the read string is "Hello" (5 bytes plus the terminator) but the size is 8, <see cref="Position"/> advances by 8.<br/>
    /// When <paramref name="adjustPosition"/> is <see langword="false"/>, <see cref="Position"/> ends right after the terminator, or after <paramref name="size"/> bytes when no terminator is found.<br/>
    /// The <see cref="Analyzer"/> receives the bytes up to and including the terminator, each before it is decoded; the bytes skipped after the terminator are not reported.<br/>
    /// Requires a seekable stream.
    /// </remarks>
    /// <param name="size">The size of the string to read, in bytes.</param>
    /// <param name="adjustPosition"><see langword="true"/> to advance <see cref="Position"/> past all <paramref name="size"/> bytes; <see langword="false"/> to stop right after the terminator.</param>
    /// <returns>The fixed-size string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size"/> is negative or greater than the number of remaining bytes in the stream.</exception>
    /// <exception cref="EndOfStreamException">Thrown when the end of the stream is reached before <paramref name="size"/> bytes could be read.</exception>
    string ReadSizedCString(int size, bool adjustPosition = true);

    /// <summary>
    /// Reads the remaining bytes from the current position to the end of the stream into a byte array.
    /// </summary>
    /// <remarks>Requires a seekable stream.</remarks>
    /// <param name="maxBufferSize">The maximum buffer size in bytes.</param>
    /// <returns>A byte array containing the remaining bytes in the stream.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxBufferSize"/> is not greater than zero.</exception>
    byte[] ReadUntilEnd(int maxBufferSize = 16 * 1024);

    /// <summary>
    /// Gets the actual current length of the stream, without adjustments from region view mode.
    /// </summary>
    long ActualLength { get; }

    /// <summary>
    /// Gets the actual current position in the stream, without adjustments from region view mode.
    /// </summary>
    long ActualPosition { get; }

    /// <summary>
    /// Gets or sets the current analyzer used by the reader.
    /// </summary>
    Analyzer? Analyzer { get; set; }

    /// <summary>
    /// Gets the stream that the reader reads from.
    /// </summary>
    /// <remarks>
    /// The stream shares <see cref="Position"/>, <see cref="Length"/>, the region view and the <see cref="Analyzer"/> with the reader,
    /// so seeking or reading it moves the reader.
    /// </remarks>
    Stream BaseStream { get; }

    /// <summary>
    /// Gets the scratch buffer that <see cref="ReadInInternalBuffer"/> fills and the <c>*At</c> reader extensions read from.
    /// </summary>
    Memory<byte> Buffer { get; }

    /// <summary>
    /// Gets a value indicating whether the reader can read any new data.
    /// </summary>
    bool CanReadData { get; }

    /// <summary>
    /// Gets a value indicating whether the reader is disposed.
    /// </summary>
    bool Disposed { get; }

    /// <summary>
    /// Gets the encoding used by the reader for every character and string read.
    /// </summary>
    Encoding Encoding { get; }

    /// <summary>
    /// Gets a value indicating whether the reader is in region view mode.
    /// </summary>
    bool IsRegionViewEnabled { get; }

    /// <summary>
    /// Gets the length of the stream.
    /// </summary>
    /// <remarks>While <see cref="IsRegionViewEnabled"/> is <see langword="true"/>, this is the size of <see cref="RegionView"/>. Otherwise it requires a seekable stream.</remarks>
    long Length { get; }

    /// <summary>
    /// Gets or sets the position within the stream.
    /// </summary>
    /// <remarks>While <see cref="IsRegionViewEnabled"/> is <see langword="true"/>, the position is relative to the start of <see cref="RegionView"/>. Requires a seekable stream.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a negative value is set while <see cref="IsRegionViewEnabled"/> is <see langword="true"/>.</exception>
    long Position { get; set; }

    /// <summary>
    /// Gets the current region view of the stream.
    /// </summary>
    RegionRange RegionView { get; }
}