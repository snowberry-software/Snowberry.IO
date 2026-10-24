using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Snowberry.IO.Common;

namespace Snowberry.IO.SingleFile.Format.Hosts;

/// <summary>
/// Writes the ad-hoc code signature of a macOS executable that the .NET SDK writes: an embedded signature with a version 0x20400 code
/// directory of SHA-256 page hashes, an empty requirements blob and an empty CMS wrapper.
/// </summary>
/// <remarks>
/// <para>Like <c>codesign</c> on macOS 26 and later and the current .NET SDK, Arm64 executables are hashed in 16 KiB pages and others in 4 KiB
/// pages; the .NET 10 SDK uses 4 KiB pages for all executables, and Arm64 Macs accept both.</para>
/// <para>Code signature data is big-endian. See
/// https://github.com/dotnet/runtime/tree/main/src/installer/managed/Microsoft.NET.HostModel/MachO/BinaryFormat/Blobs and
/// https://github.com/apple-oss-distributions/Security/blob/main/OSX/libsecurity_codesigning/lib/codedirectory.h.</para>
/// </remarks>
internal static class MachOCodeSignature
{
    /// <summary>
    /// The size in bytes of a page hash.
    /// </summary>
    public const int c_HashSize = 32;

    private const uint c_CmsWrapperMagic = 0xFADE0B01;
    private const uint c_CmsWrapperSlot = 0x10000;
    private const uint c_CodeDirectoryMagic = 0xFADE0C02;
    private const uint c_CodeDirectorySlot = 0;
    private const int c_CodeDirectoryHeaderSize = 88;
    private const uint c_CodeDirectoryVersion = 0x20400;
    private const uint c_EmbeddedSignatureMagic = 0xFADE0CC0;
    private const uint c_RequirementsMagic = 0xFADE0C01;
    private const uint c_RequirementsSlot = 2;
    private const int c_SuperBlobHeaderSize = 12;
    private const int c_BlobIndexSize = 8;
    private const int c_SpecialSlotCount = 2;

    // An empty requirements super blob: magic, length and a blob count of zero.
    private static readonly byte[] s_EmptyRequirements = [0xFA, 0xDE, 0x0C, 0x01, 0, 0, 0, 12, 0, 0, 0, 0];

    // An empty CMS wrapper blob: magic and length.
    private static readonly byte[] s_EmptyCmsWrapper = [0xFA, 0xDE, 0x0B, 0x01, 0, 0, 0, 8];

    /// <summary>
    /// Gets the length of the signature of an image whose signed content ends at <paramref name="codeLimit"/>.
    /// </summary>
    /// <param name="codeLimit">The offset where the signature starts; everything before it is hashed.</param>
    /// <param name="identifier">The signing identifier.</param>
    /// <param name="pageSize">The size in bytes of the hashed pages: 4096 or 16384.</param>
    /// <returns>The length of the signature in bytes.</returns>
    public static long GetLength(long codeLimit, string identifier, int pageSize)
    {
        return c_SuperBlobHeaderSize + (3 * c_BlobIndexSize) + GetCodeDirectoryLength(codeLimit, identifier, pageSize) + s_EmptyRequirements.Length + s_EmptyCmsWrapper.Length;
    }

    /// <summary>
    /// Gets the number of pages hashed for an image whose signed content ends at <paramref name="codeLimit"/>.
    /// </summary>
    /// <param name="codeLimit">The offset where the signature starts.</param>
    /// <param name="pageSize">The size in bytes of the hashed pages.</param>
    /// <returns>The number of pages, the last of which may be partial.</returns>
    public static long GetPageCount(long codeLimit, int pageSize)
    {
        return (codeLimit + pageSize - 1) / pageSize;
    }

    /// <summary>
    /// Reads the signing identifier from the code directory of an embedded signature.
    /// </summary>
    /// <param name="signature">The start of the embedded signature.</param>
    /// <returns>The identifier, or <see langword="null"/> if <paramref name="signature"/> does not contain a code directory with one.</returns>
    public static string? ReadIdentifier(ReadOnlySpan<byte> signature)
    {
        if (signature.Length < c_SuperBlobHeaderSize || BinaryEndianConverter.ToUInt32(signature, EndianType.BIG) != c_EmbeddedSignatureMagic)
            return null;

        uint count = BinaryEndianConverter.ToUInt32(signature, 8, EndianType.BIG);

        for (long i = 0; i < count; i++)
        {
            long index = c_SuperBlobHeaderSize + (i * c_BlobIndexSize);

            if (index > signature.Length - c_BlobIndexSize)
                return null;

            if (BinaryEndianConverter.ToUInt32(signature, (int)index, EndianType.BIG) != c_CodeDirectorySlot)
                continue;

            uint directory = BinaryEndianConverter.ToUInt32(signature, (int)index + 4, EndianType.BIG);

            if (directory > signature.Length - c_CodeDirectoryHeaderSize
                || BinaryEndianConverter.ToUInt32(signature, (int)directory, EndianType.BIG) != c_CodeDirectoryMagic)
            {
                return null;
            }

            uint identifier = BinaryEndianConverter.ToUInt32(signature, (int)directory + 20, EndianType.BIG);

            if (identifier >= signature.Length - directory)
                return null;

            var bytes = signature[(int)(directory + identifier)..];
            int terminator = bytes.IndexOf((byte)0);
            return terminator > 0 ? Encoding.UTF8.GetString(bytes[..terminator]) : null;
        }

        return null;
    }

    /// <summary>
    /// Writes the signature of an image whose signed content ends at <paramref name="codeLimit"/>.
    /// </summary>
    /// <param name="destination">The buffer that receives the signature; exactly <see cref="GetLength"/> bytes long.</param>
    /// <param name="codeLimit">
    /// The offset where the signature starts; everything before it is hashed. It is below 4 GiB, because the load command that locates the
    /// signature is 32 bits wide, so the 64-bit code limit stays <c>0</c> like the .NET SDK writes it.
    /// </param>
    /// <param name="identifier">The signing identifier.</param>
    /// <param name="pageSize">The size in bytes of the hashed pages: 4096 or 16384.</param>
    /// <param name="pageHashes">The SHA-256 hashes of the pages of the signed content, in order.</param>
    public static void Write(Span<byte> destination, long codeLimit, string identifier, int pageSize, ReadOnlySpan<byte> pageHashes)
    {
        int identifierLength = Encoding.UTF8.GetByteCount(identifier) + 1;
        long pageCount = GetPageCount(codeLimit, pageSize);
        uint directoryLength = (uint)GetCodeDirectoryLength(codeLimit, identifier, pageSize);
        uint directoryOffset = c_SuperBlobHeaderSize + (3 * c_BlobIndexSize);
        uint requirementsOffset = directoryOffset + directoryLength;
        uint cmsWrapperOffset = requirementsOffset + (uint)s_EmptyRequirements.Length;

        destination.Clear();

        BinaryEndianConverter.WriteUInt32(destination, c_EmbeddedSignatureMagic, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(destination, 4, (uint)destination.Length, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(destination, 8, 3, EndianType.BIG);
        WriteIndex(destination[c_SuperBlobHeaderSize..], c_CodeDirectorySlot, directoryOffset);
        WriteIndex(destination[(c_SuperBlobHeaderSize + c_BlobIndexSize)..], c_RequirementsSlot, requirementsOffset);
        WriteIndex(destination[(c_SuperBlobHeaderSize + (2 * c_BlobIndexSize))..], c_CmsWrapperSlot, cmsWrapperOffset);

        // CS_CodeDirectory: magic, length, version, flags (CS_ADHOC), hashOffset, identOffset, nSpecialSlots, nCodeSlots, codeLimit, hashSize,
        // hashType (SHA-256), platform, pageSize (log2), spare2, scatterOffset, teamOffset, spare3, codeLimit64, execSegBase, execSegLimit and
        // execSegFlags (CS_EXECSEG_MAIN_BINARY). Like the .NET SDK, the executable segment limit is the code limit.
        var directory = destination.Slice((int)directoryOffset, (int)directoryLength);
        uint hashOffset = (uint)(c_CodeDirectoryHeaderSize + identifierLength + (c_SpecialSlotCount * c_HashSize));
        BinaryEndianConverter.WriteUInt32(directory, c_CodeDirectoryMagic, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(directory, 4, directoryLength, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(directory, 8, c_CodeDirectoryVersion, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(directory, 12, 2, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(directory, 16, hashOffset, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(directory, 20, c_CodeDirectoryHeaderSize, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(directory, 24, c_SpecialSlotCount, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(directory, 28, (uint)pageCount, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(directory, 32, (uint)codeLimit, EndianType.BIG);
        directory[36] = c_HashSize;
        directory[37] = 2;
        directory[39] = (byte)BitOperations.Log2((uint)pageSize);
        BinaryEndianConverter.WriteUInt64(directory, 72, (ulong)codeLimit, EndianType.BIG);
        BinaryEndianConverter.WriteUInt64(directory, 80, 1, EndianType.BIG);
        Encoding.UTF8.GetBytes(identifier, directory[c_CodeDirectoryHeaderSize..]);

        // Special slot -2 holds the hash of the requirements and slot -1, for an Info.plist, stays empty.
        SHA256.HashData(s_EmptyRequirements, directory[(int)(hashOffset - (c_SpecialSlotCount * c_HashSize))..]);
        pageHashes.CopyTo(directory[(int)hashOffset..]);

        s_EmptyRequirements.CopyTo(destination[(int)requirementsOffset..]);
        s_EmptyCmsWrapper.CopyTo(destination[(int)cmsWrapperOffset..]);
    }

    private static long GetCodeDirectoryLength(long codeLimit, string identifier, int pageSize)
    {
        return c_CodeDirectoryHeaderSize + Encoding.UTF8.GetByteCount(identifier) + 1 + ((c_SpecialSlotCount + GetPageCount(codeLimit, pageSize)) * c_HashSize);
    }

    private static void WriteIndex(Span<byte> destination, uint slot, uint offset)
    {
        BinaryEndianConverter.WriteUInt32(destination, slot, EndianType.BIG);
        BinaryEndianConverter.WriteUInt32(destination, 4, offset, EndianType.BIG);
    }
}
