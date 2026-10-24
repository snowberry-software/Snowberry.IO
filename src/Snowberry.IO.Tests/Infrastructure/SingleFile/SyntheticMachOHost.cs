using System.Buffers.Binary;
using System.Text;

namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// Builds 64-bit Mach-O application host templates with the load commands the .NET SDK bundler requires: <c>__PAGEZERO</c>, <c>__TEXT</c>
/// with one section, a writable <c>__DATA</c> segment that contains the bundle marker, <c>__LINKEDIT</c>, <c>LC_SYMTAB</c> and an optional
/// <c>LC_CODE_SIGNATURE</c>.
/// </summary>
internal static class SyntheticMachOHost
{
    /// <summary>
    /// The CPU type of Arm64 Mach-O images.
    /// </summary>
    public const uint c_CpuTypeArm64 = 0x0100000C;

    /// <summary>
    /// The CPU type of x86-64 Mach-O images.
    /// </summary>
    public const uint c_CpuTypeX64 = 0x01000007;

    /// <summary>
    /// The offset of the bundle marker: the manifest offset, followed by the bundle signature.
    /// </summary>
    public const int c_MarkerOffset = 0x2100;

    /// <summary>
    /// The file offset of the <c>__LINKEDIT</c> segment.
    /// </summary>
    public const int c_LinkEditOffset = 0x3000;

    /// <summary>
    /// The end of the string table, where the signature of a signed host starts.
    /// </summary>
    public const int c_StringTableEnd = c_StringTableOffset + c_StringTableSize;

    private const int c_DataOffset = 0x2000;
    private const int c_SectionOffset = 0x1000;
    private const int c_StringTableOffset = c_LinkEditOffset + 0x20;
    private const int c_StringTableSize = 0x30;
    private const int c_SegmentSize = 72;
    private const int c_SectionSize = 80;

    /// <summary>
    /// Builds a host template.
    /// </summary>
    /// <param name="cpuType">The CPU type, <see cref="c_CpuTypeArm64"/> or <see cref="c_CpuTypeX64"/>.</param>
    /// <param name="signingIdentifier">The identifier of a minimal code signature, or <see langword="null"/> for an unsigned host.</param>
    /// <param name="firstSectionOffset">
    /// The file offset of the <c>__TEXT</c> section, which limits how far the load commands may grow; the default leaves plenty of room.
    /// </param>
    /// <returns>The host bytes.</returns>
    public static byte[] Build(uint cpuType = c_CpuTypeArm64, string? signingIdentifier = null, int firstSectionOffset = c_SectionOffset)
    {
        byte[] signature = signingIdentifier == null ? [] : CreateSignature(signingIdentifier);
        int signatureOffset = c_StringTableEnd;
        byte[] host = new byte[signatureOffset + signature.Length];
        var span = host.AsSpan();

        int commandCount = signingIdentifier == null ? 5 : 6;
        int commandsSize = (4 * c_SegmentSize) + c_SectionSize + 24 + (signingIdentifier == null ? 0 : 16);

        BinaryPrimitives.WriteUInt32LittleEndian(span, 0xFEEDFACF);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], cpuType);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)commandCount);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)commandsSize);

        int position = 32;
        position += WriteSegment(span[position..], "__PAGEZERO", 0, 0, protection: 0, sections: 0);
        int textSegment = position;
        position += WriteSegment(span[position..], "__TEXT", 0, c_DataOffset, protection: 5, sections: 1);
        WriteSection(span[(textSegment + c_SegmentSize)..], firstSectionOffset);
        position += WriteSegment(span[position..], "__DATA", c_DataOffset, c_LinkEditOffset - c_DataOffset, protection: 3, sections: 0);
        position += WriteSegment(span[position..], "__LINKEDIT", c_LinkEditOffset, host.Length - c_LinkEditOffset, protection: 1, sections: 0);

        BinaryPrimitives.WriteUInt32LittleEndian(span[position..], 0x02);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 4)..], 24);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 8)..], c_LinkEditOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 12)..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 16)..], c_StringTableOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 20)..], c_StringTableSize);
        position += 24;

        if (signingIdentifier != null)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[position..], 0x1D);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 4)..], 16);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 8)..], (uint)signatureOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 12)..], (uint)signature.Length);
            signature.CopyTo(span[signatureOffset..]);
        }

        // Code, symbols and strings with recognizable content; the strings must not end with the padding a bundle may follow.
        span[firstSectionOffset..(firstSectionOffset + 0x100)].Fill(0xC3);
        span[c_LinkEditOffset..c_StringTableOffset].Fill(0x5A);
        Encoding.ASCII.GetBytes("\0_main\0_dotnet_bundle\0__mh_execute_header\0").CopyTo(span[c_StringTableOffset..]);
        SyntheticBundleBuilder.s_BundleSignature.CopyTo(host, c_MarkerOffset + 8);
        return host;
    }

    // An embedded signature with only a code directory that holds the identifier, enough to read the identifier back.
    private static byte[] CreateSignature(string identifier)
    {
        byte[] identifierBytes = Encoding.UTF8.GetBytes(identifier + "\0");
        int directoryLength = 88 + identifierBytes.Length;
        byte[] signature = new byte[20 + directoryLength];
        var span = signature.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, 0xFADE0CC0);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)signature.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 1);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], 20);

        var directory = span[20..];
        BinaryPrimitives.WriteUInt32BigEndian(directory, 0xFADE0C02);
        BinaryPrimitives.WriteUInt32BigEndian(directory[4..], (uint)directoryLength);
        BinaryPrimitives.WriteUInt32BigEndian(directory[8..], 0x20400);
        BinaryPrimitives.WriteUInt32BigEndian(directory[20..], 88);
        identifierBytes.CopyTo(directory[88..]);
        return signature;
    }

    private static void WriteSection(Span<byte> section, int offset)
    {
        Encoding.ASCII.GetBytes("__text").CopyTo(section);
        Encoding.ASCII.GetBytes("__TEXT").CopyTo(section[16..]);
        BinaryPrimitives.WriteUInt64LittleEndian(section[40..], 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(section[48..], (uint)offset);
    }

    private static int WriteSegment(Span<byte> command, string name, long offset, long length, uint protection, uint sections)
    {
        int size = c_SegmentSize + ((int)sections * c_SectionSize);
        BinaryPrimitives.WriteUInt32LittleEndian(command, 0x19);
        BinaryPrimitives.WriteUInt32LittleEndian(command[4..], (uint)size);
        Encoding.ASCII.GetBytes(name).CopyTo(command[8..]);
        BinaryPrimitives.WriteUInt64LittleEndian(command[32..], (ulong)((length + 0xFFF) & ~0xFFF));
        BinaryPrimitives.WriteUInt64LittleEndian(command[40..], (ulong)offset);
        BinaryPrimitives.WriteUInt64LittleEndian(command[48..], (ulong)length);
        BinaryPrimitives.WriteUInt32LittleEndian(command[56..], protection);
        BinaryPrimitives.WriteUInt32LittleEndian(command[60..], protection);
        BinaryPrimitives.WriteUInt32LittleEndian(command[64..], sections);
        return size;
    }
}
