using System.Text;

namespace Snowberry.IO.Reader.Interfaces;

public interface IEndianReader : IDisposable
{
    /// <summary>
    /// The minimum size of the buffer used in a reader.
    /// </summary>
    public const int MinBufferSize = Sha1.StructSize;
    // Sha1 is the largest data type for this contract.

    /// <summary>
    /// Reads the bytes from the current stream and writes them to another stream.
    /// </summary>
    /// <param name="destination">The stream to which the contents of the current stream will be copied.</param>
    void CopyTo(Stream destination);

    /// <summary>
    /// Reads the bytes from the current stream and writes them to another stream.
    /// </summary>
    /// <param name="destination">The stream to which the contents of the current stream will be copied.</param>
    /// <param name="length">The length to copy.</param>
    /// <param name="bufferSize">The size in bytes of the buffer. This value must be greater than zero.</param>
    void CopyTo(Stream destination, int length, int bufferSize = 0x14000);

    /// <summary>
    /// Reads a single byte.
    /// </summary>
    /// <remarks>This won't check if the current <see cref="Position"/> is greater than the current <see cref="Length"/>.<para/>
    /// This should not be used in loops, if the case from the example above occurs then it could lead to an infinite loop as the last result from the buffer is returned.
    /// </remarks>
    /// <returns>A single byte.</returns>
    byte ReadByte();

    /// <summary>
    /// Same as <see cref="ReadByte"/> with the difference that the result will be <see langword="-1"/> if the <see cref="Position"/> is greater than the <see cref="Length"/>.
    /// </summary>
    /// <returns>A single byte as <see cref="int"/>.</returns>
    int ReadByteSafe();

    /// <summary>
    /// Reads the given amount of bytes into the specified <paramref name="Buffer"/>.
    /// </summary>
    /// <param name="Buffer">The target buffer.</param>
    /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
    /// <param name="byteCount">The maximum number of bytes to be read from the current stream.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero (0)
    /// if the end of the stream has been reached.
    /// </returns>
    int Read(byte[] buffer, int offset, int byteCount);

    /// <summary>
    /// Reads the given amount of bytes into the <see cref="Buffer"/>.
    /// </summary>
    /// <param name="byteCount">The byte count.</param>
    /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero (0)
    /// if the end of the stream has been reached.
    /// </returns>
    int ReadInInternalBuffer(int byteCount, int offset);

    /// <summary>
    /// Reads the given amount of bytes from the current stream.
    /// </summary>
    /// <param name="count">The amount of bytes to read from the current stream.</param>
    /// <returns>The read buffer.</returns>
    byte[] ReadBytes(int count);

    /// <summary>
    /// Reads a CString from the current stream.
    /// </summary>
    /// <returns>A zero (<see langword="0"/>) <see cref="char"/> terminated <see cref="string" />, or <see langword="null"/> if the end of the input stream is reached.</returns>
    string? ReadCString();

    /// <summary>
    /// Reads a fixed sized CString.
    /// </summary>
    /// <remarks>
    /// The reader's <see cref="Position"/> will automatically be adjusted by using <paramref name="adjustPosition" />.<para/>
    /// For instance if the read <see cref="string"/> is `Hello` (length <see langword="5"/>) but the expected <paramref name="size"/> is <see langword="8"/> it will increase the <see cref="Position"/> by the remaining <see langword="3"/>.
    /// </remarks>
    /// <param name="size">The CString size</param>
    /// <param name="adjustPosition">Automatically adjust the <see cref="Position" /> to the remaining unread count of <paramref name="size" />.</param>
    /// <returns>The CString with the fixed size.</returns>
    string? ReadSizedCString(int size, bool adjustPosition = true);

    /// <summary>
    /// Reads a size-prefixed string from the current stream and returns the data as a string.
    /// </summary>
    /// <returns>The size-prefixed string, or <see langword="null"/> if the end of the input stream is reached.</returns>
    string? ReadString();

    /// <summary>
    /// Reads a line of characters from the current stream and returns the data as a string.
    /// </summary>
    /// <remarks>
    /// Supported:
    /// <list type="bullet">
    ///     <item>CR LF</item>
    ///     <item>LF</item>
    /// </list>
    /// </remarks>
    /// <returns>The next line from the input stream, or <see langword="null"/> if the end of the input stream is reached.</returns>
    string? ReadLine();

    /// <summary>
    /// Read <see cref="Sha1"/> hash.
    /// </summary>
    /// <returns>The <see cref="Sha1"/> hash.</returns>
    Sha1 ReadSha1();

    /// <summary>
    /// Read padding with the given alignment.
    /// </summary>
    /// <param name="alignment">The padding alignment.</param>
    void ReadAlignment(byte alignment);

    long ReadLong(EndianType endian = EndianType.LITTLE);

    ulong ReadULong(EndianType endian = EndianType.LITTLE);

    uint ReadUInt32(EndianType endian = EndianType.LITTLE);

    ushort ReadUInt16(EndianType endian = EndianType.LITTLE);

    int ReadInt32(EndianType endian = EndianType.LITTLE);

    short ReadInt16(EndianType endian = EndianType.LITTLE);

    Guid ReadGuid(EndianType endian = EndianType.LITTLE);

    float ReadFloat(EndianType endian = EndianType.LITTLE);

    double ReadDouble(EndianType endian = EndianType.LITTLE);

    /// <summary>
    /// Reads the complete stream starting from the current position into a byte array.
    /// </summary>
    /// <param name="maxBufferSize">The max buffer size.</param>
    /// <returns>The rest of the stream as byte array.</returns>
    byte[] ReadUntilEnd(int maxBufferSize = 16 * 1024);

    /// <summary>
    /// Reads a <see cref="bool"/> value from the current stream and advances the current <see cref="Position"/> of the stream by one <see cref="byte"/>.
    /// </summary>
    /// <returns><see langword="true"/> if the byte is nonzero; otherwise, <see langword="false"/>.</returns>
    bool ReadBool();

    /// <summary>
    /// Reads in a 64-bit integer in compressed format.
    /// </summary>
    /// <remarks>
    /// <see href="https://en.wikipedia.org/wiki/LEB128">More info.</see>
    /// </remarks>
    /// <returns>A 64-bit integer in compressed format.</returns>
    long Read7BitEncodedLong();

    /// <summary>
    /// Reads in a 32-bit integer in compressed format.
    /// </summary>
    /// <remarks>
    /// <see href="https://en.wikipedia.org/wiki/LEB128">More info.</see>
    /// </remarks>
    /// <returns>A 32-bit integer in compressed format.</returns>
    int Read7BitEncodedInt();

    /// <summary>
    /// Enables a mode that only a specific area will be viewed.
    /// </summary>
    /// <param name="region">The viewed region range.</param>
    void EnableRegionView(RegionRange region);

    /// <summary>
    /// Ensures that the current <see cref="Buffer"/> size is at least as much as the given <paramref name="bufferSize"/>.
    /// <para>When the buffer is smaller, a new empty buffer will be initialized.</para>
    /// </summary>
    /// <param name="bufferSize">The minimum buffer size.</param>
    void EnsureBufferSize(int bufferSize);

    /// <summary>
    /// Disables the region view mode.
    /// </summary>
    void DisableRegionView();

    /// <summary>
    /// Determines whether the reader can read any new data.
    /// </summary>
    bool CanReadData { get; }

    /// <summary>
    /// The stream's length.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// The stream's position.
    /// </summary>
    long Position { get; set; }

    /// <summary>
    /// Returns the actual current position without any adjustments from the <see cref="IsRegionViewEnabled"/> mode.
    /// </summary>
    long ActualPosition { get; }

    /// <summary>
    /// Returns the actual current length without any adjustments from the <see cref="IsRegionViewEnabled"/> mode.
    /// </summary>
    long ActualLength { get; }

    /// <summary>
    /// Determines whether the current reader is in a view.
    /// </summary>
    bool IsRegionViewEnabled { get; }

    /// <summary>
    /// The stream view.
    /// </summary>
    RegionRange RegionView { get; }

    /// <summary>
    /// Returns the internal buffer of the reader.
    /// </summary>
    byte[] Buffer { get; }

    /// <summary>
    /// Returns whether the reader is dispoed or not.
    /// </summary>
    bool Disposed { get; }

    /// <summary>
    /// The encoding that will be used.
    /// </summary>
    Encoding Encoding { get; }

    /// <summary>
    /// The current analyzer that is used.
    /// </summary>
    Analyzer? Analyzer { get; set; }
}
