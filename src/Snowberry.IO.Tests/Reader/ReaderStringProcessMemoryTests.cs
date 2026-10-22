using System.Diagnostics;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Reader;
using Xunit;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for the string reads of <see cref="Win32ProcessReader"/> next to memory that cannot be read, in the test process itself.
/// </summary>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class ReaderStringProcessMemoryTests
{
    private const int c_PageSize = 4096;

    private static readonly bool s_IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [Fact]
    public void ReadLine_LineEndsAtUnreadablePage_ReturnsWholeLine()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var memory = new PageBeforeUnreadablePage();
        memory.Write(0, [.. Enumerable.Repeat((byte)'a', c_PageSize)]);
        using var reader = new Win32ProcessReader(CurrentProcessId, memory.Address);

        reader.ReadLine().Should().Be(new string('a', c_PageSize));
        reader.Position.Should().Be(memory.Address + c_PageSize);
    }

    [Fact]
    public void ReadSizedCString_TerminatorAtEndOfReadablePage_ReturnsText()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        using var memory = new PageBeforeUnreadablePage();
        memory.Write(0, [.. Enumerable.Repeat((byte)'a', c_PageSize - 1), 0]);
        using var reader = new Win32ProcessReader(CurrentProcessId, memory.Address);
        reader.EnableRegionView(new RegionRange { StartPosition = memory.Address, Size = 2 * c_PageSize });

        reader.ReadSizedCString(2 * c_PageSize, adjustPosition: false).Should().Be(new string('a', c_PageSize - 1));
        reader.Position.Should().Be(c_PageSize);
    }

    [Fact]
    public void ReadString_StringEndsAtUnreadablePage_ReturnsText()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        // The 7-bit encoded length 4094 takes two bytes, so the string ends exactly at the end of the page.
        using var memory = new PageBeforeUnreadablePage();
        memory.Write(0, [0xFE, 0x1F, .. Enumerable.Repeat((byte)'a', c_PageSize - 2)]);
        using var reader = new Win32ProcessReader(CurrentProcessId, memory.Address);

        reader.ReadString().Should().Be(new string('a', c_PageSize - 2));
        reader.Position.Should().Be(memory.Address + c_PageSize);
    }

    [Fact]
    public void ReadString_StringReachesIntoUnreadablePage_ThrowsAfterConsumingReadableBytes()
    {
        Assert.SkipUnless(s_IsWindows, "Windows only.");

        // The 7-bit encoded length 6000 takes two bytes; the string continues past the readable page.
        using var memory = new PageBeforeUnreadablePage();
        memory.Write(0, [0xF0, 0x2E, .. Enumerable.Repeat((byte)'a', c_PageSize - 2)]);
        using var reader = new Win32ProcessReader(CurrentProcessId, memory.Address);

        FluentActions.Invoking(reader.ReadString).Should().ThrowExactly<EndOfStreamException>();
        reader.Position.Should().Be(memory.Address + c_PageSize);
    }

    private static int CurrentProcessId
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return process.Id;
        }
    }

    /// <summary>
    /// A readable and writable page of the current process, directly followed by a page that cannot be accessed.
    /// </summary>
    private sealed class PageBeforeUnreadablePage : IDisposable
    {
        private const uint c_MemCommitReserve = 0x3000;
        private const uint c_MemRelease = 0x8000;
        private const uint c_PageNoAccess = 0x01;
        private const uint c_PageReadWrite = 0x04;

        private readonly IntPtr _base;

        public PageBeforeUnreadablePage()
        {
            _base = VirtualAlloc(IntPtr.Zero, 2 * c_PageSize, c_MemCommitReserve, c_PageReadWrite);

            if (_base == IntPtr.Zero)
                throw new InvalidOperationException("VirtualAlloc failed.");

            if (!VirtualProtect(_base + c_PageSize, c_PageSize, c_PageNoAccess, out _))
                throw new InvalidOperationException("VirtualProtect failed.");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

        public void Dispose()
        {
            _ = VirtualFree(_base, UIntPtr.Zero, c_MemRelease);
        }

        /// <summary>
        /// Copies <paramref name="data"/> into the readable page at <paramref name="offset"/>.
        /// </summary>
        public void Write(int offset, byte[] data)
        {
            Marshal.Copy(data, 0, _base + offset, data.Length);
        }

        /// <summary>
        /// Gets the address of the readable page.
        /// </summary>
        public long Address => _base.ToInt64();
    }
}