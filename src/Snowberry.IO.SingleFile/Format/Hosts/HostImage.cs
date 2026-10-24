using System.Reflection.PortableExecutable;
using Snowberry.IO.Common;

namespace Snowberry.IO.SingleFile.Format.Hosts;

/// <summary>
/// What the bundle reader and writer need to know about the application host that precedes the bundle.
/// </summary>
/// <param name="Kind">The executable format of the host.</param>
/// <param name="AssemblyAlignment">The assembly alignment the .NET SDK uses for this host.</param>
/// <param name="ImageEnd">The end of the host image in the file, or <c>0</c> if it could not be determined.</param>
/// <param name="CheckSumOffset">The file offset of the PE checksum, or <c>-1</c>.</param>
/// <param name="SecurityDirectoryOffset">The file offset of the PE security (Authenticode) data directory entry, or <c>-1</c>.</param>
/// <param name="SecurityOffset">The file offset of the Authenticode certificate table, or <c>0</c> if the host is not signed.</param>
internal sealed record HostImage(HostKind Kind, int AssemblyAlignment, long ImageEnd, long CheckSumOffset, long SecurityDirectoryOffset, long SecurityOffset)
{
    /// <summary>
    /// The number of bytes at the start of the file that are parsed.
    /// </summary>
    public const int c_HeaderLength = 64 * 1024;

    private const int c_DefaultAlignment = 4096;

    /// <summary>
    /// Parses the start of the file.
    /// </summary>
    /// <remarks>
    /// The assembly alignment mirrors <c>TargetInfo.AssemblyAlignment</c> of the .NET SDK: 4096 for Windows and Arm64 hosts,
    /// 16384 for LoongArch64 hosts and 64 otherwise; an unrecognized host uses 4096, which satisfies every platform.
    /// </remarks>
    /// <param name="buffer">The buffer that holds the first bytes of the host executable.</param>
    /// <param name="length">The number of bytes in <paramref name="buffer"/>.</param>
    /// <returns>The description of the host image.</returns>
    public static HostImage Parse(byte[] buffer, int length)
    {
        ReadOnlySpan<byte> header = buffer.AsSpan(0, length);

        if (header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z')
            return ParsePe(buffer, length);

        if (header.Length >= 4 && header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F')
            return ParseElf(header);

        if (header.Length >= 8)
        {
            uint magic = BinaryEndianConverter.ToUInt32(header, EndianType.LITTLE);

            // MH_MAGIC_64: cputype follows the magic.
            if (magic == 0xFEEDFACF)
            {
                const uint c_CpuTypeArm64 = 0x0100000C;
                int alignment = BinaryEndianConverter.ToUInt32(header, 4, EndianType.LITTLE) == c_CpuTypeArm64 ? 4096 : 64;
                return new HostImage(HostKind.MachO, alignment, 0, -1, -1, 0) { WritableRanges = ReadMachOWritableSegments(header), MachO = MachOImage.Parse(header) };
            }

            // FAT_MAGIC / FAT_MAGIC_64, stored big-endian.
            uint bigEndianMagic = BinaryEndianConverter.ToUInt32(header, EndianType.BIG);

            if (bigEndianMagic is 0xCAFEBABE or 0xCAFEBABF)
                return new HostImage(HostKind.UniversalMachO, c_DefaultAlignment, 0, -1, -1, 0);
        }

        return new HostImage(HostKind.Unknown, c_DefaultAlignment, 0, -1, -1, 0);
    }

    /// <summary>
    /// Orders ranges by offset and merges the ones that overlap or touch, so headers that declare the same data many times cannot make it be
    /// searched many times.
    /// </summary>
    private static List<(long Offset, long Length)> MergeRanges(List<(long Offset, long Length)> ranges)
    {
        ranges.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var merged = new List<(long Offset, long Length)>(ranges.Count);
        long mergedEnd = 0;

        foreach (var (offset, length) in ranges)
        {
            long end = length > long.MaxValue - offset ? long.MaxValue : offset + length;

            if (merged.Count > 0 && offset <= mergedEnd)
            {
                mergedEnd = Math.Max(mergedEnd, end);
                merged[^1] = (merged[^1].Offset, mergedEnd - merged[^1].Offset);
            }
            else
            {
                merged.Add((offset, end - offset));
                mergedEnd = end;
            }
        }

        return merged;
    }

    private static HostImage ParseElf(ReadOnlySpan<byte> header)
    {
        bool is64Bit = header.Length > 4 && header[4] == 2;
        var endian = header.Length > 5 && header[5] == 2 ? EndianType.BIG : EndianType.LITTLE;

        if (header.Length < (is64Bit ? 64 : 52))
            return new HostImage(HostKind.Elf, c_DefaultAlignment, 0, -1, -1, 0);

        ushort machine = BinaryEndianConverter.ToUInt16(header, 18, endian);
        int alignment = machine switch
        {
            0xB7 => 4096,   // EM_AARCH64
            0x102 => 16384, // EM_LOONGARCH
            _ => 64
        };

        // The section header table is the last part of an ELF image.
        long sectionTable = is64Bit ? (long)BinaryEndianConverter.ToUInt64(header, 0x28, endian) : BinaryEndianConverter.ToUInt32(header, 0x20, endian);
        int sectionEntrySize = BinaryEndianConverter.ToUInt16(header, is64Bit ? 0x3A : 0x2E, endian);
        int sectionCount = BinaryEndianConverter.ToUInt16(header, is64Bit ? 0x3C : 0x30, endian);
        long imageEnd = sectionTable > 0 && sectionCount > 0 ? sectionTable + ((long)sectionEntrySize * sectionCount) : 0;

        return new HostImage(HostKind.Elf, alignment, imageEnd, -1, -1, 0) { WritableRanges = ReadElfWritableSegments(header, is64Bit, endian) };
    }

    private static HostImage ParsePe(byte[] buffer, int length)
    {
        var unknownLayout = new HostImage(HostKind.PortableExecutable, 4096, 0, -1, -1, 0);

        PEHeaders headers;

        try
        {
            headers = new PEHeaders(new MemoryStream(buffer, 0, length, writable: false));
        }
        catch (BadImageFormatException)
        {
            return unknownLayout;
        }

        if (headers.PEHeader is not { } peHeader)
            return unknownLayout;

        const SectionCharacteristics c_WritableData = SectionCharacteristics.MemWrite | SectionCharacteristics.ContainsInitializedData;
        long imageEnd = 0;
        var writableRanges = new List<(long Offset, long Length)>();

        foreach (var section in headers.SectionHeaders)
        {
            if (section.SizeOfRawData <= 0)
                continue;

            imageEnd = Math.Max(imageEnd, (long)section.PointerToRawData + section.SizeOfRawData);

            // PointerToRawData is unsigned in the file, so a large value reads as negative.
            if ((section.SectionCharacteristics & c_WritableData) == c_WritableData && section.PointerToRawData >= 0)
                writableRanges.Add((section.PointerToRawData, section.SizeOfRawData));
        }

        // Field offsets in the optional header: CheckSum at 64; the data directories start at 96 (PE32) or 112 (PE32+), and the
        // security (certificate table) directory is their fifth entry.
        int optionalHeader = headers.PEHeaderStartOffset;
        int securityDirectory = optionalHeader + (peHeader.Magic == PEMagic.PE32 ? 96 : 112) + (4 * 8);

        return new HostImage(HostKind.PortableExecutable, 4096, imageEnd, optionalHeader + 64, securityDirectory, peHeader.CertificateTableDirectory.RelativeVirtualAddress)
        {
            WritableRanges = MergeRanges(writableRanges)
        };
    }

    /// <summary>
    /// Reads the loadable, writable segments from the ELF program header table, for 32-bit and 64-bit images of either byte order.
    /// </summary>
    /// <returns>The file ranges of the segments, or none if the table does not lie within <paramref name="header"/>.</returns>
    private static List<(long Offset, long Length)> ReadElfWritableSegments(ReadOnlySpan<byte> header, bool is64Bit, EndianType endian)
    {
        const uint c_LoadableSegment = 1; // PT_LOAD
        const uint c_Writable = 2;        // PF_W

        var ranges = new List<(long Offset, long Length)>();
        long table = is64Bit ? (long)BinaryEndianConverter.ToUInt64(header, 0x20, endian) : BinaryEndianConverter.ToUInt32(header, 0x1C, endian);
        int entrySize = BinaryEndianConverter.ToUInt16(header, is64Bit ? 0x36 : 0x2A, endian);
        int count = BinaryEndianConverter.ToUInt16(header, is64Bit ? 0x38 : 0x2C, endian);
        int minEntrySize = is64Bit ? 56 : 32;

        if (table <= 0 || entrySize < minEntrySize || table > header.Length - ((long)entrySize * count))
            return ranges;

        for (int i = 0; i < count; i++)
        {
            var entry = header.Slice((int)table + (i * entrySize), minEntrySize);
            uint type = BinaryEndianConverter.ToUInt32(entry, 0, endian);

            // Elf64_Phdr: p_type, p_flags, p_offset, p_vaddr, p_paddr, p_filesz; Elf32_Phdr: p_type, p_offset, p_vaddr, p_paddr, p_filesz, p_memsz, p_flags.
            uint flags = BinaryEndianConverter.ToUInt32(entry, is64Bit ? 4 : 24, endian);
            long fileOffset = is64Bit ? (long)BinaryEndianConverter.ToUInt64(entry, 8, endian) : BinaryEndianConverter.ToUInt32(entry, 4, endian);
            long fileSize = is64Bit ? (long)BinaryEndianConverter.ToUInt64(entry, 32, endian) : BinaryEndianConverter.ToUInt32(entry, 16, endian);

            if (type == c_LoadableSegment && (flags & c_Writable) != 0 && fileOffset >= 0 && fileSize > 0)
                ranges.Add((fileOffset, fileSize));
        }

        return MergeRanges(ranges);
    }

    /// <summary>
    /// Reads the segments whose initial protection allows writing from the load commands of a 64-bit Mach-O image.
    /// </summary>
    /// <returns>The file ranges of the segments that lie within the load commands in <paramref name="header"/>.</returns>
    private static List<(long Offset, long Length)> ReadMachOWritableSegments(ReadOnlySpan<byte> header)
    {
        const int c_HeaderSize = 32;
        const uint c_Segment64 = 0x19;  // LC_SEGMENT_64
        const int c_Segment64Size = 72;
        const uint c_Writable = 2;      // VM_PROT_WRITE

        var ranges = new List<(long Offset, long Length)>();

        if (header.Length < c_HeaderSize)
            return ranges;

        uint count = BinaryEndianConverter.ToUInt32(header, 16, EndianType.LITTLE);
        int position = c_HeaderSize;

        for (uint i = 0; i < count && position <= header.Length - 8; i++)
        {
            uint command = BinaryEndianConverter.ToUInt32(header, position, EndianType.LITTLE);
            uint size = BinaryEndianConverter.ToUInt32(header, position + 4, EndianType.LITTLE);

            if (size < 8 || size > header.Length - position)
                break;

            // segment_command_64: cmd, cmdsize, segname[16], vmaddr, vmsize, fileoff, filesize, maxprot, initprot.
            if (command == c_Segment64 && size >= c_Segment64Size)
            {
                long fileOffset = (long)BinaryEndianConverter.ToUInt64(header, position + 40, EndianType.LITTLE);
                long fileSize = (long)BinaryEndianConverter.ToUInt64(header, position + 48, EndianType.LITTLE);
                uint initialProtection = BinaryEndianConverter.ToUInt32(header, position + 60, EndianType.LITTLE);

                if ((initialProtection & c_Writable) != 0 && fileOffset >= 0 && fileSize > 0)
                    ranges.Add((fileOffset, fileSize));
            }

            position += (int)size;
        }

        return MergeRanges(ranges);
    }

    /// <summary>
    /// Gets the load commands of a single-architecture macOS host that saving updates, or <see langword="null"/> for other hosts and for a macOS
    /// host whose load commands could not be read.
    /// </summary>
    public MachOImage? MachO { get; init; }

    /// <summary>
    /// Gets the file ranges of the host's writable, initialized data, ordered by offset: the sections or segments that the program writes to at
    /// run time, such as the .NET host's bundle signature and manifest offset.
    /// </summary>
    /// <remarks>
    /// The ranges are read from PE section headers, ELF program headers and Mach-O segment commands within the parsed header. Overlapping and
    /// adjacent ranges are merged, so the ranges never cover a byte twice, but they are not checked against the length of the file. A host whose
    /// headers could not be read has none.
    /// </remarks>
    public IReadOnlyList<(long Offset, long Length)> WritableRanges { get; init; } = [];
}