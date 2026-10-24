using Snowberry.IO.Common;

namespace Snowberry.IO.SingleFile.Format.Hosts;

/// <summary>
/// The load commands of a 64-bit little-endian Mach-O executable that bundling and code signing change, like the .NET SDK's
/// <c>MachObjectFile</c> reads them.
/// </summary>
/// <remarks>
/// Bundled files are appended to the <c>__LINKEDIT</c> segment, which must be the last segment, and covered by the string table; the code
/// signature follows them. See
/// https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/MachO/MachObjectFile.cs.
/// </remarks>
/// <param name="CommandCount">The number of load commands.</param>
/// <param name="CommandsSize">The size in bytes of the load commands.</param>
/// <param name="LinkEditCommandOffset">The file offset of the <c>__LINKEDIT</c> segment command.</param>
/// <param name="LinkEditOffset">The file offset of the <c>__LINKEDIT</c> segment.</param>
/// <param name="LinkEditSize">The size in bytes of the <c>__LINKEDIT</c> segment in the file.</param>
/// <param name="SymbolTableCommandOffset">The file offset of the <c>LC_SYMTAB</c> command.</param>
/// <param name="StringTableOffset">The file offset of the string table.</param>
/// <param name="CodeSignatureCommandOffset">The file offset of the <c>LC_CODE_SIGNATURE</c> command, or <c>-1</c> if the image is not signed.</param>
/// <param name="CodeSignatureOffset">The file offset of the code signature, or <c>0</c> if the image is not signed.</param>
/// <param name="CodeSignatureSize">The size in bytes of the code signature, or <c>0</c> if the image is not signed.</param>
/// <param name="FirstSectionOffset">The file offset of the first <c>__TEXT</c> section, which load commands must not reach.</param>
/// <param name="CodePageSize">The size in bytes of the pages a code signature hashes: 16384 for Arm64 and 4096 otherwise.</param>
internal sealed record MachOImage(
    int CommandCount,
    int CommandsSize,
    long LinkEditCommandOffset,
    long LinkEditOffset,
    long LinkEditSize,
    long SymbolTableCommandOffset,
    long StringTableOffset,
    long CodeSignatureCommandOffset,
    long CodeSignatureOffset,
    long CodeSignatureSize,
    long FirstSectionOffset,
    int CodePageSize)
{
    /// <summary>
    /// The size in bytes of the 64-bit Mach-O header.
    /// </summary>
    public const int c_HeaderSize = 32;

    /// <summary>
    /// The size in bytes of an <c>LC_CODE_SIGNATURE</c> command.
    /// </summary>
    public const int c_CodeSignatureCommandSize = 16;

    /// <summary>
    /// The <c>LC_CODE_SIGNATURE</c> command type.
    /// </summary>
    public const uint c_CodeSignatureCommand = 0x1D;

    /// <summary>
    /// The alignment of segment sizes in memory.
    /// </summary>
    public const long c_SegmentAlignment = 4096;

    /// <summary>
    /// The offset of <c>filesize</c> in a <c>segment_command_64</c>.
    /// </summary>
    public const int c_SegmentFileSizeField = 48;

    /// <summary>
    /// The offset of <c>vmsize</c> in a <c>segment_command_64</c>.
    /// </summary>
    public const int c_SegmentVirtualSizeField = 32;

    /// <summary>
    /// The offset of <c>strsize</c> in a <c>symtab_command</c>.
    /// </summary>
    public const int c_StringTableSizeField = 20;

    /// <summary>
    /// The offset of <c>ncmds</c> in the Mach-O header; <c>sizeofcmds</c> follows it.
    /// </summary>
    public const int c_CommandCountField = 16;

    // The signature directly follows the string table, aligned to 16 bytes.
    private const long c_CodeSignatureAlignment = 16;

    private const uint c_CpuTypeArm64 = 0x0100000C;
    private const uint c_SymbolTableCommand = 0x02;
    private const uint c_Segment64Command = 0x19;
    private const int c_SectionSize = 80;
    private const int c_Segment64Size = 72;
    private const int c_SymbolTableCommandSize = 24;

    /// <summary>
    /// Reads the load commands from the start of a 64-bit little-endian Mach-O image.
    /// </summary>
    /// <param name="header">The start of the image, which must contain the header and all load commands.</param>
    /// <returns>
    /// The load commands, or <see langword="null"/> if they do not lie within <paramref name="header"/> or do not have the layout the .NET SDK
    /// requires: one <c>__LINKEDIT</c> segment after all other segments and after the load commands, one <c>LC_SYMTAB</c> whose tables lie in
    /// it, and an optional <c>LC_CODE_SIGNATURE</c> that is the last command and whose signature follows the string table in it.
    /// </returns>
    public static MachOImage? Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length < c_HeaderSize)
            return null;

        uint count = BinaryEndianConverter.ToUInt32(header, c_CommandCountField, EndianType.LITTLE);
        uint size = BinaryEndianConverter.ToUInt32(header, c_CommandCountField + 4, EndianType.LITTLE);

        if (size > header.Length - c_HeaderSize)
            return null;

        long linkEditCommand = -1, linkEditOffset = 0, linkEditSize = 0;
        long symbolTableCommand = -1, symbolTableOffset = 0, stringTableOffset = 0, stringTableSize = 0;
        long signatureCommand = -1, signatureOffset = 0, signatureSize = 0;
        long firstSection = long.MaxValue, lastOtherSegment = 0;
        int end = c_HeaderSize + (int)size;
        int position = c_HeaderSize;

        for (uint i = 0; i < count; i++)
        {
            if (position > end - 8)
                return null;

            var command = header[position..end];
            uint type = BinaryEndianConverter.ToUInt32(command, EndianType.LITTLE);
            uint commandSize = BinaryEndianConverter.ToUInt32(command, 4, EndianType.LITTLE);

            if (commandSize < 8 || commandSize > command.Length)
                return null;

            command = command[..(int)commandSize];

            switch (type)
            {
                case c_CodeSignatureCommand:
                    if (i + 1 != count || commandSize < c_CodeSignatureCommandSize)
                        return null;

                    signatureCommand = position;
                    signatureOffset = BinaryEndianConverter.ToUInt32(command, 8, EndianType.LITTLE);
                    signatureSize = BinaryEndianConverter.ToUInt32(command, 12, EndianType.LITTLE);
                    break;

                case c_Segment64Command when commandSize >= c_Segment64Size:
                    var name = command.Slice(8, 16);
                    long fileOffset = (long)BinaryEndianConverter.ToUInt64(command, 40, EndianType.LITTLE);
                    long fileSize = (long)BinaryEndianConverter.ToUInt64(command, c_SegmentFileSizeField, EndianType.LITTLE);

                    if (fileOffset < 0 || fileSize < 0)
                        return null;

                    if (IsSegmentName(name, "__TEXT"u8))
                    {
                        uint sections = BinaryEndianConverter.ToUInt32(command, 64, EndianType.LITTLE);

                        if (sections > (commandSize - c_Segment64Size) / c_SectionSize)
                            return null;

                        for (int s = 0; s < sections; s++)
                            firstSection = Math.Min(firstSection, BinaryEndianConverter.ToUInt32(command, c_Segment64Size + (s * c_SectionSize) + 48, EndianType.LITTLE));
                    }
                    else if (IsSegmentName(name, "__LINKEDIT"u8))
                    {
                        if (linkEditCommand >= 0)
                            return null;

                        linkEditCommand = position;
                        linkEditOffset = fileOffset;
                        linkEditSize = fileSize;
                    }
                    else
                    {
                        lastOtherSegment = Math.Max(lastOtherSegment, fileOffset);
                    }

                    break;

                case c_SymbolTableCommand when commandSize >= c_SymbolTableCommandSize:
                    if (symbolTableCommand >= 0)
                        return null;

                    symbolTableCommand = position;
                    symbolTableOffset = BinaryEndianConverter.ToUInt32(command, 8, EndianType.LITTLE);
                    stringTableOffset = BinaryEndianConverter.ToUInt32(command, 16, EndianType.LITTLE);
                    stringTableSize = BinaryEndianConverter.ToUInt32(command, c_StringTableSizeField, EndianType.LITTLE);
                    break;
            }

            position += (int)commandSize;
        }

        if (linkEditCommand < 0 || symbolTableCommand < 0 || lastOtherSegment > linkEditOffset || linkEditSize > long.MaxValue - linkEditOffset)
            return null;

        // The load commands are updated in place, so they must lie in the image before the bundle data in __LINKEDIT.
        if (end > linkEditOffset)
            return null;

        long linkEditEnd = linkEditOffset + linkEditSize;
        long stringTableEnd = stringTableOffset + stringTableSize;

        if (symbolTableOffset < linkEditOffset || stringTableEnd > linkEditEnd)
            return null;

        if (signatureCommand >= 0
            && (signatureOffset < stringTableEnd || signatureOffset - stringTableEnd > c_CodeSignatureAlignment || signatureOffset + signatureSize > linkEditEnd))
        {
            return null;
        }

        return new MachOImage(
            (int)count,
            (int)size,
            linkEditCommand,
            linkEditOffset,
            linkEditSize,
            symbolTableCommand,
            stringTableOffset,
            signatureCommand,
            signatureOffset,
            signatureSize,
            firstSection,
            BinaryEndianConverter.ToUInt32(header, 4, EndianType.LITTLE) == c_CpuTypeArm64 ? 16384 : 4096);
    }

    /// <summary>
    /// Rounds <paramref name="offset"/> up to the alignment of a code signature.
    /// </summary>
    /// <param name="offset">The offset to align.</param>
    /// <returns>The aligned offset.</returns>
    public static long AlignCodeSignature(long offset)
    {
        return (offset + c_CodeSignatureAlignment - 1) & ~(c_CodeSignatureAlignment - 1);
    }

    private static bool IsSegmentName(ReadOnlySpan<byte> field, ReadOnlySpan<byte> name)
    {
        return field.StartsWith(name) && field[name.Length..].IndexOfAnyExcept((byte)0) < 0;
    }

    /// <summary>
    /// Gets the end of the image without its code signature, where the .NET SDK starts the bundle when it removes the signature of the host.
    /// </summary>
    public long UnsignedEnd => LinkEditOffset + LinkEditSize - CodeSignatureSize;
}
