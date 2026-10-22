using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Snowberry.IO.Reader;
using Snowberry.IO.SingleFile.Sources;

namespace Snowberry.IO.SingleFile.Format.Manifests;

/// <summary>
/// Reads and writes the bundle manifest.
/// </summary>
internal static class ManifestSerializer
{
    private const int c_InitialReadLength = 64 * 1024;
    private const int c_MaxInitialCapacity = 1024;
    private const int c_MaxLength = 64 * 1024 * 1024;

    // Offset + size + type + an empty length-prefixed path.
    private const int c_MinEntryLength = (sizeof(long) * 2) + 1 + 1;

    private static readonly UTF8Encoding s_Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Gets the number of bytes <see cref="Write{T}"/> writes.
    /// </summary>
    /// <typeparam name="T">The type of the items that describe the entries.</typeparam>
    /// <param name="header">The bundle header to write.</param>
    /// <param name="entries">The items that describe the manifest entries, in order.</param>
    /// <param name="select">Gets the manifest entry an item describes.</param>
    /// <returns>The length of the serialized manifest.</returns>
    /// <exception cref="EncoderFallbackException">The identifier or a path is not valid UTF-16.</exception>
    public static int GetLength<T>(BundleHeader header, IReadOnlyList<T> entries, Func<T, ManifestEntry> select)
    {
        long length = (sizeof(uint) * 2) + sizeof(int) + GetStringLength(header.Id);

        if (header.MajorVersion >= 2)
            length += (sizeof(long) * 4) + sizeof(ulong);

        int recordLength = (sizeof(long) * (header.MajorVersion >= 6 ? 3 : 2)) + 1;

        for (int i = 0; i < entries.Count; i++)
            length += recordLength + GetStringLength(select(entries[i]).RelativePath);

        return checked((int)length);
    }

    /// <summary>
    /// Reads the manifest in growing chunks, so data after the manifest is read only as far as needed.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="source">The source to read the manifest from.</param>
    /// <param name="manifestOffset">The offset of the manifest within <paramref name="source"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="InvalidDataException">The manifest is malformed or has an unsupported version.</exception>
    public static async ValueTask<Manifest> ReadAsync(bool useAsync, BundleSource source, long manifestOffset, CancellationToken cancellationToken)
    {
        long available = source.Length - manifestOffset;
        int maxLength = (int)Math.Min(available, c_MaxLength);
        int length = Math.Min(maxLength, c_InitialReadLength);

        while (true)
        {
            byte[] data = ArrayPool<byte>.Shared.Rent(length);

            try
            {
                await source.ReadExactlyAsync(useAsync, manifestOffset, data.AsMemory(0, length), cancellationToken).ConfigureAwait(false);

                try
                {
                    return Parse(data, length, maxLength);
                }
                catch (EndOfStreamException) when (length < maxLength)
                {
                    length = (int)Math.Min(maxLength, (long)length * 4);
                }
                catch (Exception ex) when (ex is EndOfStreamException or FormatException or ArgumentException or IOException)
                {
                    throw new InvalidDataException("The bundle manifest is malformed.", ex);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(data);
            }
        }
    }

    /// <summary>
    /// Writes a manifest in the format of <paramref name="header"/>'s major version.
    /// </summary>
    /// <typeparam name="T">The type of the items that describe the entries.</typeparam>
    /// <param name="header">The bundle header to write.</param>
    /// <param name="entries">The items that describe the manifest entries, in order.</param>
    /// <param name="select">Gets the manifest entry an item describes.</param>
    /// <param name="destination">The buffer to write to, exactly as long as <see cref="GetLength{T}"/> returns.</param>
    /// <exception cref="EncoderFallbackException">The identifier or a path is not valid UTF-16.</exception>
    public static void Write<T>(BundleHeader header, IReadOnlyList<T> entries, Func<T, ManifestEntry> select, Span<byte> destination)
    {
        var writer = new SpanWriter(destination);
        writer.WriteUInt32(header.MajorVersion);
        writer.WriteUInt32(header.MinorVersion);
        writer.WriteUInt32((uint)entries.Count);
        writer.WriteString(header.Id);

        if (header.MajorVersion >= 2)
        {
            WriteLocation(ref writer, entries, select, BundleFileType.DepsJson);
            WriteLocation(ref writer, entries, select, BundleFileType.RuntimeConfigJson);
            writer.WriteUInt64((ulong)header.Flags);
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = select(entries[i]);
            writer.WriteInt64(entry.Offset);
            writer.WriteInt64(entry.Size);

            if (header.MajorVersion >= 6)
                writer.WriteInt64(entry.CompressedSize);

            writer.WriteByte((byte)entry.Type);
            writer.WriteString(entry.RelativePath);
        }

        if (!writer.IsFull)
            throw new ArgumentException("The destination is longer than the manifest.", nameof(destination));
    }

    /// <summary>
    /// Gets the length of a string as <see cref="BinaryWriter.Write(string)"/> writes it: the UTF-8 length as a 7-bit encoded integer, then the
    /// UTF-8 bytes.
    /// </summary>
    private static long GetStringLength(string value)
    {
        int byteCount = s_Utf8.GetByteCount(value);
        int prefixLength = 1;

        for (uint remaining = (uint)byteCount; remaining >= 0x80; remaining >>= 7)
            prefixLength++;

        return prefixLength + (long)byteCount;
    }

    /// <summary>
    /// Parses the manifest at the start of <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The buffer with the bytes read so far.</param>
    /// <param name="length">The number of bytes read into <paramref name="data"/>.</param>
    /// <param name="maxLength">The most bytes the manifest may occupy.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="EndOfStreamException">The manifest extends past the end of the bytes read.</exception>
    private static Manifest Parse(byte[] data, int length, int maxLength)
    {
        using var reader = new EndianStreamReader(new MemoryStream(data, 0, length, writable: false), null, 0, s_Utf8);

        uint majorVersion = reader.ReadUInt32();
        uint minorVersion = reader.ReadUInt32();

        if (!BundleFormat.IsSupportedMajorVersion(majorVersion))
            throw new InvalidDataException($"Bundle format version {majorVersion}.{minorVersion} is not supported; supported major versions are 1, 2 and 6.");

        int fileCount = reader.ReadInt32();
        string id = reader.ReadString();

        var flags = BundleFlags.None;

        if (majorVersion >= 2)
        {
            // The deps.json and runtimeconfig.json locations duplicate their entries and are recomputed when writing.
            reader.Position += sizeof(long) * 4;
            flags = (BundleFlags)reader.ReadUInt64();
        }

        if (fileCount < 0 || (long)fileCount * c_MinEntryLength > maxLength - reader.Position)
            throw new InvalidDataException($"The bundle manifest declares an invalid file count of {fileCount}.");

        // The count is only an upper bound until the records are read, so it does not size the list up front.
        var entries = new List<ManifestEntry>(Math.Min(fileCount, c_MaxInitialCapacity));

        for (int i = 0; i < fileCount; i++)
        {
            long offset = reader.ReadInt64();
            long size = reader.ReadInt64();
            long compressedSize = majorVersion >= 6 ? reader.ReadInt64() : 0;
            var type = (BundleFileType)reader.ReadByte();
            string relativePath = reader.ReadString();

            if (!Enum.IsDefined(type))
                throw new InvalidDataException($"The bundle entry '{relativePath}' has the unknown file type {(byte)type}.");

            entries.Add(new ManifestEntry(offset, size, compressedSize, type, relativePath));
        }

        return new Manifest(new BundleHeader(majorVersion, minorVersion, id, flags), entries);
    }

    private static void WriteLocation<T>(ref SpanWriter writer, IReadOnlyList<T> entries, Func<T, ManifestEntry> select, BundleFileType type)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = select(entries[i]);

            if (entry.Type == type)
            {
                writer.WriteInt64(entry.Offset);
                writer.WriteInt64(entry.Size);
                return;
            }
        }

        writer.WriteInt64(0);
        writer.WriteInt64(0);
    }

    /// <summary>
    /// Writes little-endian values and length-prefixed UTF-8 strings into a buffer.
    /// </summary>
    /// <param name="destination">The buffer to write to.</param>
    private ref struct SpanWriter(Span<byte> destination)
    {
        private readonly Span<byte> _destination = destination;
        private int _position;

        public void WriteByte(byte value)
        {
            _destination[_position++] = value;
        }

        public void WriteInt64(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(_destination[_position..], value);
            _position += sizeof(long);
        }

        public void WriteString(string value)
        {
            int byteCount = s_Utf8.GetByteCount(value);
            uint remaining = (uint)byteCount;

            for (; remaining >= 0x80; remaining >>= 7)
                WriteByte((byte)(remaining | 0x80));

            WriteByte((byte)remaining);
            _position += s_Utf8.GetBytes(value, _destination[_position..]);
        }

        public void WriteUInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_destination[_position..], value);
            _position += sizeof(uint);
        }

        public void WriteUInt64(ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(_destination[_position..], value);
            _position += sizeof(ulong);
        }

        /// <summary>
        /// Gets a value indicating whether the whole buffer has been written.
        /// </summary>
        public readonly bool IsFull => _position == _destination.Length;
    }
}