using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Extensions;
using Snowberry.IO.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for <see cref="Win32ProcessReader"/>, which read the memory of the test process itself.
/// </summary>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows6.0.6000")]
#endif
public class Win32ProcessReaderTests
{
    private const uint c_ProcessVmRead = 0x0010;
    private const string c_SignaturePattern = "DE AD BE EF 11 22 33 44";

    private static readonly bool s_IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static readonly byte[] s_Signature = [0xDE, 0xAD, 0xBE, 0xEF, 0x11, 0x22, 0x33, 0x44];

    /// <summary>
    /// Creates a text of <paramref name="length"/> characters that mixes ASCII, characters with zero bytes in their UTF-16 and UTF-32
    /// code units, and surrogate pairs that are never split.
    /// </summary>
    /// <param name="length">The number of characters.</param>
    /// <returns>The text.</returns>
    private static string CreateText(int length)
    {
        const string pattern = "ab\u0100\u00FC\u6F22c\U0001F600d";
        var builder = new StringBuilder(length);

        while (builder.Length < length)
        {
            char next = pattern[builder.Length % pattern.Length];

            // A high surrogate must not end the text without its low surrogate.
            builder.Append(char.IsHighSurrogate(next) && builder.Length == length - 1 ? 'e' : next);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns the bytes of a pointer to <paramref name="address"/> in the current process.
    /// </summary>
    /// <param name="address">The address the pointer points to.</param>
    /// <returns>The 4 or 8 bytes of the pointer.</returns>
    private static byte[] PointerBytes(long address)
    {
        return IntPtr.Size == 8 ? BitConverter.GetBytes(address) : BitConverter.GetBytes((int)address);
    }

    /// <summary>
    /// Makes <paramref name="reader"/> decode strings with <paramref name="encoding"/>.
    /// </summary>
    /// <remarks>
    /// The public constructors always decode UTF-8; the encoding is swapped so <see cref="Win32ProcessReader.ReadCString"/> is
    /// covered for code units of 2 and 4 bytes as well.
    /// </remarks>
    /// <param name="reader">The reader.</param>
    /// <param name="encoding">The encoding.</param>
    private static void UseEncoding(Win32ProcessReader reader, Encoding encoding)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

        typeof(BaseEndianReader).GetField("_encoding", flags)!.SetValue(reader, encoding);
        typeof(BaseEndianReader).GetField("_2BytesPerChar", flags)!.SetValue(reader, encoding is UnicodeEncoding);
        typeof(BaseEndianReader).GetField("_4bytesPerChar", flags)!.SetValue(reader, encoding is UTF32Encoding);
    }

    [Fact]
    public void BaseReaderStream_Win32ProcessReader_IsReaderStream()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var reader = new Win32ProcessReader(CurrentProcessId, 0);

        BinaryReaderAssertions.AssertReadsThroughReaderStream(reader);
    }

    [Fact]
    public void BaseStream_OwnProcess_ReadsProcessMemoryAtReaderPosition()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var buffer = new PinnedBuffer([1, 2, 3, 4, 5, 6, 7, 8]);
        using var reader = new Win32ProcessReader(CurrentProcessId, buffer.Address);
        reader.ReadInt16();

        var stream = reader.BaseStream;

        stream.CanRead.Should().BeTrue();
        stream.CanSeek.Should().BeTrue();
        stream.CanWrite.Should().BeFalse();
        stream.Position.Should().Be(buffer.Address + 2);
        stream.ReadByte().Should().Be(3);
        reader.Position.Should().Be(buffer.Address + 3);
    }

    [Fact]
    public void Constructor_MissingProcess_ThrowsWin32Exception()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        FluentActions.Invoking(() => new Win32ProcessReader(int.MaxValue - 1, 0)).Should().ThrowExactly<Win32Exception>();
    }

    [Fact]
    public void Constructor_ZeroHandle_ThrowsArgumentException()
    {
        FluentActions.Invoking(() => new Win32ProcessReader(IntPtr.Zero, 0)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void CopyTo_RangeIntoUnreadablePage_CopiesReadablePrefix()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        byte[] expected = pages.Read(pages.Address, VirtualPages.c_PageSize);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);
        using var destination = new MemoryStream();

        reader.CopyTo(destination, 3 * VirtualPages.c_PageSize);

        destination.ToArray().Should().Equal(expected);
        reader.Position.Should().Be(pages.PageAddress(1));
    }

    [Fact]
    public void CopyTo_WithLength_CopiesBytesAndWithoutLengthThrows()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        byte[] data = [9, 8, 7, 6, 5];
        using var buffer = new PinnedBuffer(data);
        using var reader = new Win32ProcessReader(CurrentProcessId, buffer.Address);
        using var destination = new MemoryStream();

        reader.CopyTo(destination, 5, 2);

        destination.ToArray().Should().Equal(data);
        FluentActions.Invoking(() => reader.CopyTo(destination)).Should().ThrowExactly<NotSupportedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        var reader = new Win32ProcessReader(CurrentProcessId, 0);
        reader.Dispose();

        FluentActions.Invoking(reader.Dispose).Should().NotThrow();

        reader.Disposed.Should().BeTrue();
        reader.CanReadData.Should().BeFalse();
    }

    [Fact]
    public void Dispose_HandleNotOwned_LeavesHandleOpen()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var process = Process.GetCurrentProcess();
        nint processHandle = process.Handle;

        using (new Win32ProcessReader(processHandle, 0) { CloseHandleOnDispose = false })
        {
        }

        using var buffer = new PinnedBuffer([42]);
        using var reader = new Win32ProcessReader(processHandle, buffer.Address) { CloseHandleOnDispose = false };

        reader.ReadByte().Should().Be(42);
    }

    [Fact]
    public void Dispose_ThenRead_ThrowsObjectDisposedException()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var buffer = new PinnedBuffer(new byte[8]);
        var reader = new Win32ProcessReader(CurrentProcessId, buffer.Address);
        var stream = reader.BaseStream;

        reader.Dispose();

        reader.Disposed.Should().BeTrue();
        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => stream.ReadByte()).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void Position_NegativeInsideRegionView_ThrowsAndKeepsPosition()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var buffer = new PinnedBuffer([1, 2, 3, 4, 5, 6, 7, 8]);
        using var reader = new Win32ProcessReader(CurrentProcessId, buffer.Address);
        reader.EnableRegionView(new RegionRange { StartPosition = buffer.Address + 4, Size = 4 });

        FluentActions.Invoking(() => reader.Position = -4).Should().ThrowExactly<ArgumentOutOfRangeException>();
        reader.Position.Should().Be(0);
        reader.ReadByte().Should().Be(5);
    }

    [Fact]
    public void ReadCString_NoTerminatorWithinMaximum_ThrowsInvalidDataException()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate((Win32ProcessReader.c_MaxCStringByteLength / VirtualPages.c_PageSize) + 2);
        pages.Write(pages.Address, PointerBytes(pages.PageAddress(1)));
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        FluentActions.Invoking(() => reader.ReadCString()).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void ReadCString_PointerToText_FollowsPointer()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var text = new PinnedBuffer(Encoding.UTF8.GetBytes("hello\0"));
        byte[] pointer = IntPtr.Size == 8
            ? BitConverter.GetBytes(text.Pointer.ToInt64())
            : BitConverter.GetBytes(text.Pointer.ToInt32());
        using var pointerBuffer = new PinnedBuffer(pointer);
        using var reader = new Win32ProcessReader(CurrentProcessId, pointerBuffer.Address);

        reader.ReadCString().Should().Be("hello");
        reader.Position.Should().Be(pointerBuffer.Address + IntPtr.Size);
    }

    [Fact]
    public void ReadCString_StringRunningIntoUnreadablePage_ThrowsEndOfStreamException()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        pages.Write(pages.Address, PointerBytes(pages.PageAddress(1) + 100));
        pages.MakeUnreadable(2, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        FluentActions.Invoking(() => reader.ReadCString()).Should().ThrowExactly<EndOfStreamException>();
    }

    [Theory]
    [InlineData("UTF-8", 0)]
    [InlineData("UTF-8", 3000)]
    [InlineData("UTF-16", 0)]
    [InlineData("UTF-16", 1)]
    [InlineData("UTF-16", 3000)]
    [InlineData("UTF-32", 0)]
    [InlineData("UTF-32", 1)]
    [InlineData("UTF-32", 3000)]
    public void ReadCString_StringWithCodeUnitsAcrossChunks_ReturnsString(string encodingName, int length)
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        Encoding encoding = encodingName switch
        {
            "UTF-16" => new UnicodeEncoding(false, false),
            "UTF-32" => new UTF32Encoding(false, false),
            _ => new UTF8Encoding(false)
        };

        // The code units contain zero bytes, and the odd start makes the chunks end in the middle of code units.
        string text = CreateText(length);
        byte[] bytes = encoding.GetBytes(text + "\0");
        using var pages = VirtualPages.Allocate(4);
        long stringAddress = pages.PageAddress(1) - 255;
        pages.Write(stringAddress, bytes);
        pages.Write(pages.Address, PointerBytes(stringAddress));
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);
        UseEncoding(reader, encoding);

        reader.ReadCString().Should().Be(text);
    }

    [Fact]
    public void ReadCString_TerminatorRightBeforeUnreadablePage_ReturnsString()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        string text = CreateText(300);
        byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
        using var pages = VirtualPages.Allocate(3);
        long stringAddress = pages.PageAddress(2) - bytes.Length;
        pages.Write(stringAddress, bytes);
        pages.Write(pages.Address, PointerBytes(stringAddress));
        pages.MakeUnreadable(2, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        reader.ReadCString().Should().Be(text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(5000)]
    [InlineData(70_000)]
    public void ReadCString_Utf8StringOfVariousLengths_ReturnsString(int length)
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        string text = CreateText(length);
        using var stringBuffer = new PinnedBuffer(Encoding.UTF8.GetBytes(text + "\0"));
        using var pointerBuffer = new PinnedBuffer(PointerBytes(stringBuffer.Address));
        using var reader = new Win32ProcessReader(CurrentProcessId, pointerBuffer.Address);

        reader.ReadCString().Should().Be(text);
        reader.Position.Should().Be(pointerBuffer.Address + IntPtr.Size);
    }

    [Fact]
    public void ReadInheritedMembers_OwnProcessMemory_MatchBinaryReader()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        byte[] data = WriteBinaryWriterBytes(w =>
        {
            w.Write(0x11223344);
            w.Write(-2.5);
            w.Write("Grüße");
            w.Write('Z');
            w.Write(12.75m);
            w.Write(true);
        });
        using var buffer = new PinnedBuffer(data);
        var analyzer = new RecordingAnalyzer();
        using var expected = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        using BinaryReader reader = new Win32ProcessReader(CurrentProcessId, buffer.Address, analyzer);

        reader.ReadInt32().Should().Be(expected.ReadInt32());
        reader.ReadDouble().Should().Be(expected.ReadDouble());
        reader.ReadString().Should().Be(expected.ReadString());
        reader.ReadChar().Should().Be(expected.ReadChar());
        reader.ReadDecimal().Should().Be(expected.ReadDecimal());
        reader.ReadBoolean().Should().Be(expected.ReadBoolean());

        ((Win32ProcessReader)reader).Position.Should().Be(buffer.Address + data.Length);
        analyzer.TotalBytes.Should().Be(data.Length);
    }

    [Fact]
    public void ReadInt32_OwnProcessMemory_ReturnsValuesAndAdvancesPosition()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var buffer = new PinnedBuffer([0x44, 0x33, 0x22, 0x11, 0x88, 0x77, 0x66, 0x55]);
        using var reader = new Win32ProcessReader(CurrentProcessId, buffer.Address);

        reader.ReadInt32().Should().Be(0x11223344);
        reader.ReadInt32().Should().Be(0x55667788);
        reader.Position.Should().Be(buffer.Address + 8);
    }

    [Fact]
    public void ReadInt32_RegionViewSmallerThanValue_ThrowsAndReadsOnlyRegion()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var buffer = new PinnedBuffer([1, 2, 3, 4, 5, 6, 7, 8]);
        using var reader = new Win32ProcessReader(CurrentProcessId, buffer.Address);
        reader.EnableRegionView(new RegionRange { StartPosition = buffer.Address, Size = 2 });

        // The inherited BinaryReader.ReadInt32 consumes the bytes left in the region before it throws, but never more.
        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<EndOfStreamException>();
        reader.Position.Should().Be(2);

        reader.Position = 0;
        reader.Read(new byte[8], 0, 8).Should().Be(2);
    }

    [Fact]
    public void ReadInt64_AcrossUnreadablePage_ThrowsAndConsumesReadableBytes()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        long start = pages.PageAddress(1) - 4;
        byte[] expected = pages.Read(start, 4);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        var analyzer = new RecordingAnalyzer();
        using var reader = new Win32ProcessReader(CurrentProcessId, start, analyzer);

        // Like at the end of any stream, the bytes before the unreadable page are consumed before the read throws.
        FluentActions.Invoking(() => reader.ReadInt64()).Should().ThrowExactly<EndOfStreamException>();
        reader.Position.Should().Be(pages.PageAddress(1));

        reader.Position = start;
        FluentActions.Invoking(() => reader.ReadInt64(EndianType.BIG)).Should().ThrowExactly<EndOfStreamException>();
        reader.Position.Should().Be(pages.PageAddress(1));

        analyzer.Bytes.Should().Equal(expected.Concat(expected));
    }

    [Fact]
    public void ReadPrimitives_WithAnalyzer_NotifiesEachReadOnce()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var buffer = new PinnedBuffer(new byte[16]);
        var analyzer = new RecordingAnalyzer();
        using var reader = new Win32ProcessReader(CurrentProcessId, buffer.Address, analyzer);

        reader.ReadInt32();
        reader.ReadInt64();

        analyzer.TotalBytes.Should().Be(12);
    }

    [Fact]
    public void Read_NonZeroOffset_FillsBufferFromOffset()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var buffer = new PinnedBuffer([1, 2, 3, 4]);
        using var reader = new Win32ProcessReader(CurrentProcessId, buffer.Address);
        byte[] destination = new byte[8];

        int read = reader.Read(destination, 4, 4);

        read.Should().Be(4);
        destination.Should().Equal(0, 0, 0, 0, 1, 2, 3, 4);
    }

    [Fact]
    public void Read_RangeIntoUnreadablePageWithAnalyzer_ReportsOnlyReadablePrefix()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        byte[] expected = pages.Read(pages.Address + 16, VirtualPages.c_PageSize - 16);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        var analyzer = new RecordingAnalyzer();
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address + 16, analyzer);

        reader.Read(new byte[2 * VirtualPages.c_PageSize], 0, 2 * VirtualPages.c_PageSize);
        reader.Read(new byte[16], 0, 16);

        analyzer.Bytes.Should().Equal(expected);
    }

    [Theory]
    [InlineData(UnreadablePageKind.NoAccess)]
    [InlineData(UnreadablePageKind.Guard)]
    [InlineData(UnreadablePageKind.Reserved)]
    public void Read_RangeIntoUnreadablePage_ReturnsReadablePrefix(UnreadablePageKind kind)
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        long start = pages.Address + 100;
        byte[] expected = pages.Read(start, VirtualPages.c_PageSize - 100);
        pages.MakeUnreadable(1, kind);
        using var reader = new Win32ProcessReader(CurrentProcessId, start);
        byte[] destination = new byte[3 * VirtualPages.c_PageSize];

        int read = reader.Read(destination, 0, destination.Length);

        read.Should().Be(expected.Length);
        destination.Take(read).Should().Equal(expected);
        reader.Position.Should().Be(pages.PageAddress(1));
    }

    [Fact]
    public void Read_ReadablePagesBeforeUnreadablePage_ReturnsAllReadablePages()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(4);
        byte[] expected = pages.Read(pages.Address, 3 * VirtualPages.c_PageSize);
        pages.MakeUnreadable(3, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);
        byte[] destination = new byte[4 * VirtualPages.c_PageSize];

        int read = reader.Read(destination, 0, destination.Length);

        read.Should().Be(expected.Length);
        destination.Take(read).Should().Equal(expected);
    }

    [Fact]
    public void Read_StartInUnreadablePage_ReturnsZeroAndKeepsPosition()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.PageAddress(1) + 8);

        reader.Read(new byte[2 * VirtualPages.c_PageSize], 0, 2 * VirtualPages.c_PageSize).Should().Be(0);
        reader.Position.Should().Be(pages.PageAddress(1) + 8);
    }

    [Fact]
    public void Read_WithoutQueryAccess_ReturnsReadablePrefix()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(4);
        byte[] expected = pages.Read(pages.Address + 100, (2 * VirtualPages.c_PageSize) - 100);
        pages.MakeUnreadable(2, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address + 100, accessFlags: c_ProcessVmRead);
        byte[] destination = new byte[4 * VirtualPages.c_PageSize];

        int read = reader.Read(destination, 0, destination.Length);

        read.Should().Be(expected.Length);
        destination.Take(read).Should().Equal(expected);
    }

    [Fact]
    public void ScanSignatures_FreeAddressSpaceBeforeSignature_StopsAtFreeAddressSpace()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.AllocateWithFreeGap();
        pages.Write(pages.Address + 100, s_Signature);
        pages.Write(pages.PageAddress(pages.PageCount - 1) + 100, s_Signature);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        var results = reader.ScanSignatures(c_SignaturePattern, 10, pages.End - s_Signature.Length);

        results.Should().Equal(pages.Address + 100);
        reader.Position.Should().Be(pages.PageAddress(1));
    }

    [Fact]
    public void ScanSignatures_MaxAddressInUnreadablePage_StopsAtUnreadablePage()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        pages.Write(pages.Address + 50, s_Signature);
        pages.Write(pages.PageAddress(2) + 100, s_Signature);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        var results = reader.ScanSignatures(c_SignaturePattern, 10, pages.PageAddress(1) + 100);

        results.Should().Equal(pages.Address + 50);
        reader.Position.Should().Be(pages.PageAddress(1));
    }

    [Fact]
    public void ScanSignatures_MaxAddressPastAllocationWithUnreadablePage_StopsAtEndOfAllocation()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        long signatureAddress = pages.PageAddress(2) + 100;
        pages.Write(signatureAddress, s_Signature);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        // The allocation granularity leaves free address space after the three pages.
        var results = reader.ScanSignatures(c_SignaturePattern, 10, long.MaxValue);

        results.Should().Equal(signatureAddress);
        reader.Position.Should().Be(pages.End);
    }

    [Fact]
    public void ScanSignatures_RegionViewAcrossUnreadablePage_ReturnsViewRelativeMatch()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        pages.Write(pages.PageAddress(2) + 100, s_Signature);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, 0);
        reader.EnableRegionView(new RegionRange { StartPosition = pages.Address, Size = 3 * VirtualPages.c_PageSize });

        var results = reader.ScanSignatures(c_SignaturePattern, 10, long.MaxValue);

        results.Should().Equal((2 * VirtualPages.c_PageSize) + 100);
    }

    [Fact]
    public void ScanSignatures_RegionViewEndingInUnreadablePage_ReturnsNoMatch()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        pages.Write(pages.PageAddress(2), s_Signature);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, 0);
        reader.EnableRegionView(new RegionRange { StartPosition = pages.Address, Size = 2 * VirtualPages.c_PageSize });

        var results = reader.ScanSignatures(c_SignaturePattern, 10, long.MaxValue);

        results.Should().BeEmpty();
        reader.Position.Should().Be(VirtualPages.c_PageSize);
    }

    [Fact]
    public void ScanSignatures_SeveralUnreadablePages_ReturnsMatchesOfEveryReadablePage()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(5);
        long[] signatureAddresses = [pages.Address + 50, pages.PageAddress(2) + 100, pages.End - s_Signature.Length];

        foreach (long address in signatureAddresses)
            pages.Write(address, s_Signature);

        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        pages.MakeUnreadable(3, UnreadablePageKind.Guard);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        var results = reader.ScanSignatures(c_SignaturePattern, 10, pages.End - s_Signature.Length);

        results.Should().Equal(signatureAddresses);
    }

    [Fact]
    public void ScanSignatures_SignatureSplitByUnreadablePage_ReturnsNoMatch()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        pages.Write(pages.PageAddress(1) - 4, s_Signature.Take(4).ToArray());
        pages.Write(pages.PageAddress(2), s_Signature.Skip(4).ToArray());
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        var results = reader.ScanSignatures(c_SignaturePattern, 10, pages.End - s_Signature.Length);

        results.Should().BeEmpty();
    }

    [Fact]
    public void ScanSignatures_StartInUnreadablePage_ReturnsMatchAfterIt()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        long signatureAddress = pages.PageAddress(2) + 100;
        pages.Write(signatureAddress, s_Signature);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.PageAddress(1) + 8);

        var results = reader.ScanSignatures(c_SignaturePattern, 10, pages.End - s_Signature.Length);

        results.Should().Equal(signatureAddress);
    }

    [Theory]
    [InlineData(UnreadablePageKind.NoAccess)]
    [InlineData(UnreadablePageKind.Guard)]
    [InlineData(UnreadablePageKind.Reserved)]
    public void ScanSignatures_UnreadablePageBeforeSignature_ReturnsMatchAfterIt(UnreadablePageKind kind)
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        long signatureAddress = pages.PageAddress(2) + 100;
        pages.Write(signatureAddress, s_Signature);
        pages.MakeUnreadable(1, kind);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address);

        var results = reader.ScanSignatures(c_SignaturePattern, 10, pages.End - s_Signature.Length);

        results.Should().Equal(signatureAddress);
    }

    [Fact]
    public void ScanSignatures_UnreadablePageWithAnalyzer_ReportsOnlyReadablePages()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        pages.Write(pages.PageAddress(2) + 100, s_Signature);
        byte[] expected = [.. pages.Read(pages.Address, VirtualPages.c_PageSize), .. pages.Read(pages.PageAddress(2), VirtualPages.c_PageSize)];
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        var analyzer = new RecordingAnalyzer();
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address, analyzer);

        reader.ScanSignatures(c_SignaturePattern, 10, pages.End - s_Signature.Length);

        analyzer.Bytes.Should().Equal(expected);
    }

    [Fact]
    public void ScanSignatures_WithoutQueryAccess_StopsAtUnreadablePage()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        pages.Write(pages.Address + 50, s_Signature);
        pages.Write(pages.PageAddress(2) + 100, s_Signature);
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        using var reader = new Win32ProcessReader(CurrentProcessId, pages.Address, accessFlags: c_ProcessVmRead);

        var results = reader.ScanSignatures(c_SignaturePattern, 10, pages.End - s_Signature.Length);

        results.Should().Equal(pages.Address + 50);
    }

    [Fact]
    public void TryReadInt64_AcrossUnreadablePage_ReturnsFalseAndKeepsPosition()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var pages = VirtualPages.Allocate(3);
        long start = pages.PageAddress(1) - 4;
        pages.MakeUnreadable(1, UnreadablePageKind.NoAccess);
        var analyzer = new RecordingAnalyzer();
        using var reader = new Win32ProcessReader(CurrentProcessId, start, analyzer);

        reader.TryReadInt64(out long value).Should().BeFalse();

        value.Should().Be(0);
        reader.Position.Should().Be(start);
        analyzer.TotalBytes.Should().Be(0);
    }

    [Fact]
    public void InjectDLL_SystemLibraryIntoCurrentProcess_ReturnsTrue()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        // PROCESS_CREATE_THREAD, PROCESS_VM_OPERATION and PROCESS_VM_WRITE on top of the read access.
        const uint injectAccess = Win32ProcessReader.c_DefaultAccessFlags | 0x0002 | 0x0008 | 0x0020;
        using var reader = new Win32ProcessReader(CurrentProcessId, 0, accessFlags: injectAccess);

        bool loaded = reader.InjectDLL(Path.Combine(Environment.SystemDirectory, "version.dll"));

        loaded.Should().BeTrue();
    }

    [Fact]
    public void MemoryBasicInformation_Layout_MatchesNativeStruct()
    {
        // NOTE(VNC): The struct is declared by hand because CsWin32 generates it only for a specific CPU architecture; sorting its
        // fields breaks every VirtualQueryEx call, which makes ScanSignatures loop forever over unreadable pages.
        var type = typeof(Win32ProcessReader).Assembly.GetType("Snowberry.IO.Utils.Win32Helper+MemoryBasicInformation", throwOnError: true)!;
        int p = IntPtr.Size;
        long[] expected = p == 8 ? [0, 8, 16, 24, 32, 36, 40] : [0, 4, 8, 12, 16, 20, 24];
        string[] fields = ["BaseAddress", "AllocationBase", "AllocationProtect", "RegionSize", "State", "Protect", "Type"];

        fields.Select(field => (long)Marshal.OffsetOf(type, field)).Should().Equal(expected);
        Marshal.SizeOf(type).Should().Be(p == 8 ? 48 : 28);
    }

    private static int CurrentProcessId
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return process.Id;
        }
    }
}