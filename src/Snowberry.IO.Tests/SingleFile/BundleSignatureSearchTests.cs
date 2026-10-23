using System.Buffers.Binary;
using AwesomeAssertions;
using Snowberry.IO.SingleFile;
using Snowberry.IO.SingleFile.Format.Hosts;
using Snowberry.IO.Tests.Infrastructure.SingleFile;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.BundleTestData;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticBundleBuilder;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for locating the bundle signature, which is searched in the writable data of the application host first.
/// </summary>
public class BundleSignatureSearchTests
{
    // IMAGE_SCN_CNT_CODE | IMAGE_SCN_MEM_EXECUTE | IMAGE_SCN_MEM_READ and IMAGE_SCN_CNT_INITIALIZED_DATA | IMAGE_SCN_MEM_READ | IMAGE_SCN_MEM_WRITE.
    private const uint c_CodeSection = 0x60000020;
    private const int c_DataLength = 64 * 1024;
    private const int c_DataOffset = 6 * 1024 * 1024;
    private const uint c_DataSection = 0xC0000040;
    private const int c_HostLength = 8 * 1024 * 1024;
    private const int c_SignatureOffset = c_DataOffset + 30_000;

    private static readonly BundleFile[] s_Files =
    [
        new("app.dll", RandomData(5000, seed: 1), BundleFileType.Assembly),
        new("lib/native.dll", s_Compressible, BundleFileType.NativeBinary, Compress: true)
    ];

    /// <summary>
    /// Opens <paramref name="bytes"/>, checks the entries and returns the number of bytes read while opening.
    /// </summary>
    private static long AssertOpens(byte[] bytes)
    {
        var stream = new CountingReadStream(bytes);
        using var bundle = SingleFileBundle.Open(stream);
        long read = stream.BytesRead;

        bundle.GetEntry("app.dll").ReadAllBytes().Should().Equal(s_Files[0].Data);
        bundle.GetEntry("lib/native.dll").ReadAllBytes().Should().Equal(s_Compressible);
        return read;
    }

    /// <summary>
    /// Creates an ELF host of <see cref="c_HostLength"/> bytes with a read-only and a writable loadable segment and no section headers.
    /// </summary>
    private static byte[] ElfHost(bool is64Bit, bool bigEndian, int programHeaderOffset)
    {
        byte[] host = new byte[c_HostLength];
        var span = host.AsSpan();
        span[0] = 0x7F;
        span[1] = (byte)'E';
        span[2] = (byte)'L';
        span[3] = (byte)'F';
        span[4] = (byte)(is64Bit ? 2 : 1);
        span[5] = (byte)(bigEndian ? 2 : 1);
        WriteUInt16(span[18..], 0x3E, bigEndian);

        int entrySize = is64Bit ? 56 : 32;

        if (is64Bit)
        {
            WriteUInt64(span[0x20..], (ulong)programHeaderOffset, bigEndian);
            WriteUInt16(span[0x36..], (ushort)entrySize, bigEndian);
            WriteUInt16(span[0x38..], 2, bigEndian);
        }
        else
        {
            WriteUInt32(span[0x1C..], (uint)programHeaderOffset, bigEndian);
            WriteUInt16(span[0x2A..], (ushort)entrySize, bigEndian);
            WriteUInt16(span[0x2C..], 2, bigEndian);
        }

        if (programHeaderOffset + (2 * entrySize) > host.Length)
            return host;

        // PF_R | PF_X for the code, PF_R | PF_W for the data.
        WriteSegment(span[programHeaderOffset..], 0, c_DataOffset, flags: 5);
        WriteSegment(span[(programHeaderOffset + entrySize)..], c_DataOffset, c_DataLength, flags: 6);
        return host;

        void WriteSegment(Span<byte> entry, long offset, long length, uint flags)
        {
            WriteUInt32(entry, 1, bigEndian); // PT_LOAD

            if (is64Bit)
            {
                WriteUInt32(entry[4..], flags, bigEndian);
                WriteUInt64(entry[8..], (ulong)offset, bigEndian);
                WriteUInt64(entry[32..], (ulong)length, bigEndian);
                WriteUInt64(entry[40..], (ulong)length, bigEndian);
            }
            else
            {
                WriteUInt32(entry[4..], (uint)offset, bigEndian);
                WriteUInt32(entry[16..], (uint)length, bigEndian);
                WriteUInt32(entry[20..], (uint)length, bigEndian);
                WriteUInt32(entry[24..], flags, bigEndian);
            }
        }
    }

    /// <summary>
    /// Creates a 64-bit Arm64 Mach-O host of <see cref="c_HostLength"/> bytes with a read-only <c>__TEXT</c> and a writable <c>__DATA</c> segment.
    /// </summary>
    private static byte[] MachOHost()
    {
        const int c_SegmentSize = 72;

        byte[] host = new byte[c_HostLength];
        var span = host.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0xFEEDFACF);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0x0100000C);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], 2 * c_SegmentSize);

        WriteSegment(span[32..], "__TEXT", 0, c_DataOffset, protection: 5);
        WriteSegment(span[(32 + c_SegmentSize)..], "__DATA", c_DataOffset, c_DataLength, protection: 3);
        return host;

        static void WriteSegment(Span<byte> command, string name, long offset, long length, uint protection)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(command, 0x19);
            BinaryPrimitives.WriteUInt32LittleEndian(command[4..], c_SegmentSize);
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(command[8..]);
            BinaryPrimitives.WriteUInt64LittleEndian(command[40..], (ulong)offset);
            BinaryPrimitives.WriteUInt64LittleEndian(command[48..], (ulong)length);
            BinaryPrimitives.WriteUInt32LittleEndian(command[56..], protection);
            BinaryPrimitives.WriteUInt32LittleEndian(command[60..], protection);
        }
    }

    /// <summary>
    /// Creates a PE32+ host of <see cref="c_HostLength"/> bytes with the given sections.
    /// </summary>
    private static byte[] PeHost((int Offset, int Length, uint Characteristics)[] sections)
    {
        const int c_PeHeader = 0x80;
        const int c_OptionalHeaderSize = 0xF0;

        byte[] host = new byte[c_HostLength];
        var span = host.AsSpan();
        span[0] = (byte)'M';
        span[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], c_PeHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(span[c_PeHeader..], 0x00004550);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(c_PeHeader + 4)..], 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(c_PeHeader + 6)..], (ushort)sections.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(c_PeHeader + 20)..], c_OptionalHeaderSize);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(c_PeHeader + 24)..], 0x20B);

        int table = c_PeHeader + 24 + c_OptionalHeaderSize;

        for (int i = 0; i < sections.Length; i++)
        {
            var header = span[(table + (i * 40))..];
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)sections[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)(0x1000 * (i + 1)));
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)sections[i].Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)sections[i].Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(header[36..], sections[i].Characteristics);
        }

        return host;
    }

    private static byte[] WithSignature(byte[] host, int offset)
    {
        s_BundleSignature.CopyTo(host, offset);
        return host;
    }

    private static void WriteUInt16(Span<byte> destination, ushort value, bool bigEndian)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
    }

    private static void WriteUInt32(Span<byte> destination, uint value, bool bigEndian)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }

    private static void WriteUInt64(Span<byte> destination, ulong value, bool bigEndian)
    {
        if (bigEndian)
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
    }

    [Fact]
    public void Open_ElfProgramHeadersBeyondParsedHeader_SearchesWholeFile()
    {
        byte[] host = ElfHost(is64Bit: true, bigEndian: false, programHeaderOffset: 1024 * 1024);

        long read = AssertOpens(BuildWithHost(WithSignature(host, c_SignatureOffset), c_SignatureOffset - 8, s_Files));

        read.Should().BeGreaterThan(c_SignatureOffset);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Open_ElfSignatureInWritableSegment_ReadsHeadersAndSegmentOnly(bool is64Bit, bool bigEndian)
    {
        byte[] host = ElfHost(is64Bit, bigEndian, programHeaderOffset: is64Bit ? 64 : 52);

        long read = AssertOpens(BuildWithHost(WithSignature(host, c_SignatureOffset), c_SignatureOffset - 8, s_Files));

        read.Should().BeLessThan(256 * 1024);
    }

    [Fact]
    public void Open_MachOSignatureInWritableSegment_ReadsHeadersAndSegmentOnly()
    {
        byte[] host = MachOHost();

        using var stream = new CountingReadStream(BuildWithHost(WithSignature(host, c_SignatureOffset), c_SignatureOffset - 8, s_Files));
        using var bundle = SingleFileBundle.Open(stream);

        bundle.GetEntry("lib/native.dll").ReadAllBytes().Should().Equal(s_Compressible);
        stream.BytesRead.Should().BeLessThan(512 * 1024);
    }

    [Fact]
    public void Open_ManyOverlappingWritableSections_SearchesEachByteOnce()
    {
        // A thousand writable sections that all declare the same 4 MiB, none of which holds the signature.
        const int c_Covered = 4 * 1024 * 1024;
        var sections = Enumerable.Repeat((0x400, c_Covered - 0x400, c_DataSection), 1000).ToArray();
        byte[] host = WithSignature(PeHost(sections), c_SignatureOffset);

        HostImage.Parse(host, HostImage.c_HeaderLength).WritableRanges.Should().Equal([(0x400L, (long)c_Covered - 0x400)]);

        long read = AssertOpens(BuildWithHost(host, c_SignatureOffset - 8, s_Files));
        read.Should().BeLessThan(c_Covered + host.Length + (1024 * 1024));
    }

    [Fact]
    public void Open_PeSignatureBeforeWritableSection_UsesSignatureInWritableSection()
    {
        // The earlier copy has a zero manifest offset, so using it would report a host without a bundle.
        byte[] host = PeHost([(0x400, c_DataOffset - 0x400, c_CodeSection), (c_DataOffset, c_DataLength, c_DataSection)]);
        s_BundleSignature.CopyTo(host, 0x10000);

        AssertOpens(BuildWithHost(WithSignature(host, c_SignatureOffset), c_SignatureOffset - 8, s_Files));
    }

    [Fact]
    public void Open_PeSignatureInWritableSection_ReadsHeadersAndSectionOnly()
    {
        byte[] host = PeHost([(0x400, c_DataOffset - 0x400, c_CodeSection), (c_DataOffset, c_DataLength, c_DataSection)]);

        long read = AssertOpens(BuildWithHost(WithSignature(host, c_SignatureOffset), c_SignatureOffset - 8, s_Files));

        read.Should().BeLessThan(256 * 1024);
    }

    [Fact]
    public void Open_PeSignatureOutsideWritableSections_SearchesWholeFile()
    {
        const int c_CodeSignature = 3 * 1024 * 1024;
        byte[] host = PeHost([(0x400, c_DataOffset - 0x400, c_CodeSection), (c_DataOffset, c_DataLength, c_DataSection)]);

        long read = AssertOpens(BuildWithHost(WithSignature(host, c_CodeSignature), c_CodeSignature - 8, s_Files));

        read.Should().BeGreaterThan(c_CodeSignature);
    }

    [Fact]
    public void Open_PeWritableSectionAtNegativeOffset_SearchesWholeFile()
    {
        // PointerToRawData is unsigned in the file; 0x80000010 reads as a negative int.
        const int c_CodeSignature = 3 * 1024 * 1024;
        byte[] host = PeHost([(0x400, c_DataOffset - 0x400, c_CodeSection), (unchecked((int)0x80000010), c_DataLength, c_DataSection)]);

        AssertOpens(BuildWithHost(WithSignature(host, c_CodeSignature), c_CodeSignature - 8, s_Files));
    }

    [Fact]
    public void Open_PeWritableSectionBeyondEndOfFile_SearchesWholeFile()
    {
        byte[] host = PeHost([(0x400, c_DataOffset - 0x400, c_CodeSection), (c_DataOffset, c_DataLength, c_DataSection), (int.MaxValue - 100, 1000, c_DataSection)]);

        AssertOpens(BuildWithHost(WithSignature(host, c_SignatureOffset), c_SignatureOffset - 8, s_Files));
    }

    [Fact]
    public void Open_SignatureAtStartOfWritableSection_ReadsManifestOffsetBeforeIt()
    {
        byte[] host = PeHost([(0x400, c_SignatureOffset - 0x400, c_CodeSection), (c_SignatureOffset, c_DataLength, c_DataSection)]);

        long read = AssertOpens(BuildWithHost(WithSignature(host, c_SignatureOffset), c_SignatureOffset - 8, s_Files));

        read.Should().BeLessThan(256 * 1024);
    }

    [Theory]
    [InlineData((1024 * 1024) - 40)] // spans the first two search chunks
    [InlineData((1024 * 1024) - 31)] // starts at the first byte carried into the second chunk
    [InlineData((1024 * 1024) - 26)] // its manifest offset spans the first two chunks
    [InlineData((2 * 1024 * 1024) + 5)]
    public void Open_SignatureNearSearchChunkBoundary_IsFound(int signatureOffset)
    {
        byte[] host = new byte[3 * 1024 * 1024];

        AssertOpens(BuildWithHost(WithSignature(host, signatureOffset), signatureOffset - 8, s_Files));
    }

    [Fact]
    public void Parse_OverlappingAndAdjacentWritableSegments_MergesThem()
    {
        byte[] host = PeHost([(0x1000, 0x1000, c_DataSection), (0x1800, 0x1000, c_DataSection), (0x2800, 0x100, c_DataSection), (0x4000, 0x10, c_DataSection)]);

        HostImage.Parse(host, HostImage.c_HeaderLength).WritableRanges.Should().Equal([(0x1000L, 0x1900L), (0x4000L, 0x10L)]);
    }
}