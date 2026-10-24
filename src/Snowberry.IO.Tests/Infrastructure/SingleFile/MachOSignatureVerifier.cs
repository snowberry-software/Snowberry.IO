using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// The load commands and ad-hoc signature that <see cref="MachOSignatureVerifier"/> read from a saved macOS executable.
/// </summary>
/// <param name="CommandCount">The number of load commands.</param>
/// <param name="StringTableEnd">The end of the string table.</param>
/// <param name="SignatureOffset">The offset of the code signature, or <c>-1</c> if the executable is not signed.</param>
/// <param name="Identifier">The signing identifier, or <see langword="null"/> if the executable is not signed.</param>
/// <param name="PageSize">The size of the hashed pages, or <c>0</c> if the executable is not signed.</param>
internal sealed record MachOSignatureInfo(int CommandCount, long StringTableEnd, long SignatureOffset, string? Identifier, int PageSize);

/// <summary>
/// Validates a saved macOS executable independently of the library: the <c>__LINKEDIT</c> segment ends at the end of the file, and an
/// ad-hoc signature, if present, has the structure the .NET SDK writes and correct SHA-256 hashes of every page before it.
/// </summary>
internal static class MachOSignatureVerifier
{
    private static readonly byte[] s_EmptyRequirements = [0xFA, 0xDE, 0x0C, 0x01, 0, 0, 0, 12, 0, 0, 0, 0];

    /// <summary>
    /// Validates <paramref name="file"/>.
    /// </summary>
    /// <param name="file">The saved executable.</param>
    /// <returns>What was read.</returns>
    /// <exception cref="InvalidDataException">The executable is not valid.</exception>
    public static MachOSignatureInfo Verify(byte[] file)
    {
        var span = file.AsSpan();
        Check(BinaryPrimitives.ReadUInt32LittleEndian(span) == 0xFEEDFACF, "Mach-O magic");

        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
        int commandsSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);
        long linkEditOffset = -1, linkEditSize = 0, linkEditVirtualSize = 0, stringTableEnd = -1, signatureOffset = -1, signatureSize = 0;
        int position = 32;

        for (int i = 0; i < count; i++)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(span[position..]);
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(position + 4)..]);

            if (type == 0x19 && span.Slice(position + 8, 16).StartsWith("__LINKEDIT"u8))
            {
                linkEditVirtualSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(position + 32)..]);
                linkEditOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(position + 40)..]);
                linkEditSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(position + 48)..]);
            }
            else if (type == 0x02)
            {
                stringTableEnd = BinaryPrimitives.ReadUInt32LittleEndian(span[(position + 16)..]) + BinaryPrimitives.ReadUInt32LittleEndian(span[(position + 20)..]);
            }
            else if (type == 0x1D)
            {
                Check(i == count - 1, "LC_CODE_SIGNATURE is the last command");
                signatureOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[(position + 8)..]);
                signatureSize = BinaryPrimitives.ReadUInt32LittleEndian(span[(position + 12)..]);
            }

            position += size;
        }

        Check(position == 32 + commandsSize, "sizeofcmds matches the commands");
        Check(linkEditOffset + linkEditSize == file.Length, "__LINKEDIT ends at the end of the file");
        Check(linkEditVirtualSize == ((linkEditSize + 0xFFF) & ~0xFFF), "__LINKEDIT vmsize is its file size rounded up to 4096 bytes");

        if (signatureOffset < 0)
        {
            Check(stringTableEnd == file.Length, "the string table ends at the end of an unsigned file");
            return new MachOSignatureInfo(count, stringTableEnd, -1, null, 0);
        }

        Check(signatureOffset % 16 == 0 && signatureOffset >= stringTableEnd && signatureOffset - stringTableEnd < 16, "the signature follows the string table, aligned to 16 bytes");
        Check(signatureOffset + signatureSize == file.Length, "the signature ends at the end of the file");

        var signature = span.Slice((int)signatureOffset, (int)signatureSize);
        Check(BinaryPrimitives.ReadUInt32BigEndian(signature) == 0xFADE0CC0, "embedded signature magic");
        Check(BinaryPrimitives.ReadUInt32BigEndian(signature[4..]) == signatureSize, "embedded signature length");
        Check(BinaryPrimitives.ReadUInt32BigEndian(signature[8..]) == 3, "three blobs");
        Check(BinaryPrimitives.ReadUInt32BigEndian(signature[12..]) == 0 && BinaryPrimitives.ReadUInt32BigEndian(signature[16..]) == 36, "code directory index");

        var directory = signature[36..];
        uint directoryLength = BinaryPrimitives.ReadUInt32BigEndian(directory[4..]);
        Check(BinaryPrimitives.ReadUInt32BigEndian(directory) == 0xFADE0C02, "code directory magic");
        Check(BinaryPrimitives.ReadUInt32BigEndian(directory[8..]) == 0x20400, "code directory version");
        Check(BinaryPrimitives.ReadUInt32BigEndian(directory[12..]) == 2, "ad-hoc flag");
        Check(BinaryPrimitives.ReadUInt32BigEndian(directory[24..]) == 2, "two special slots");
        Check(BinaryPrimitives.ReadUInt32BigEndian(directory[32..]) == signatureOffset, "code limit");
        Check(directory[36] == 32 && directory[37] == 2, "SHA-256 hashes");
        Check(BinaryPrimitives.ReadUInt64BigEndian(directory[64..]) == 0, "executable segment base");
        Check(BinaryPrimitives.ReadUInt64BigEndian(directory[72..]) == (ulong)signatureOffset, "executable segment limit");
        Check(BinaryPrimitives.ReadUInt64BigEndian(directory[80..]) == 1, "main binary flag");

        int pageSize = 1 << directory[39];
        int pages = (int)((signatureOffset + pageSize - 1) / pageSize);
        Check(BinaryPrimitives.ReadUInt32BigEndian(directory[28..]) == pages, "one hash per page");

        uint identifierOffset = BinaryPrimitives.ReadUInt32BigEndian(directory[20..]);
        uint hashOffset = BinaryPrimitives.ReadUInt32BigEndian(directory[16..]);
        Check(identifierOffset == 88, "identifier follows the header");
        int terminator = directory[(int)identifierOffset..].IndexOf((byte)0);
        string identifier = Encoding.UTF8.GetString(directory.Slice((int)identifierOffset, terminator));
        Check(hashOffset == identifierOffset + terminator + 1 + 64, "hashes follow the identifier and the special slots");
        Check(directoryLength == hashOffset + (pages * 32), "code directory length");

        Check(directory.Slice((int)hashOffset - 64, 32).SequenceEqual(SHA256.HashData(s_EmptyRequirements)), "requirements hash in slot -2");
        Check(directory.Slice((int)hashOffset - 32, 32).IndexOfAnyExcept((byte)0) < 0, "empty slot -1");

        for (int page = 0; page < pages; page++)
        {
            int start = page * pageSize;
            int length = (int)Math.Min(pageSize, signatureOffset - start);
            Check(directory.Slice((int)hashOffset + (page * 32), 32).SequenceEqual(SHA256.HashData(span.Slice(start, length))), $"hash of page {page}");
        }

        uint requirements = BinaryPrimitives.ReadUInt32BigEndian(signature[24..]);
        uint cmsWrapper = BinaryPrimitives.ReadUInt32BigEndian(signature[32..]);
        Check(BinaryPrimitives.ReadUInt32BigEndian(signature[20..]) == 2 && requirements == 36 + directoryLength, "requirements index");
        Check(BinaryPrimitives.ReadUInt32BigEndian(signature[28..]) == 0x10000 && cmsWrapper == requirements + 12, "CMS wrapper index");
        Check(signature.Slice((int)requirements, 12).SequenceEqual(s_EmptyRequirements), "empty requirements");
        Check(signature[(int)cmsWrapper..].SequenceEqual(new byte[] { 0xFA, 0xDE, 0x0B, 0x01, 0, 0, 0, 8 }), "empty CMS wrapper");

        return new MachOSignatureInfo(count, stringTableEnd, signatureOffset, identifier, pageSize);
    }

    private static void Check(bool condition, string what)
    {
        if (!condition)
            throw new InvalidDataException($"Invalid macOS executable: {what}.");
    }
}
