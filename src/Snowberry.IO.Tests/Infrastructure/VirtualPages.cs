using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Snowberry.IO.Tests.Infrastructure;

/// <summary>
/// Pages of the current process allocated with <c>VirtualAlloc</c>, some of which a test can make unreadable.
/// </summary>
/// <remarks>The pages are released when the instance is disposed.</remarks>
internal sealed class VirtualPages : IDisposable
{
    /// <summary>
    /// The size of a memory page in bytes.
    /// </summary>
    public const int c_PageSize = 4096;

    // Allocations start at multiples of the allocation granularity, so the pages of a separate allocation that lies this far
    // after the first one are separated from it by free address space.
    private const int c_AllocationGranularity = 64 * 1024;

    private const uint c_MemCommit = 0x1000;
    private const uint c_MemDecommit = 0x4000;
    private const uint c_MemRelease = 0x8000;
    private const uint c_MemReserve = 0x2000;
    private const uint c_PageGuard = 0x100;
    private const uint c_PageNoAccess = 0x01;
    private const uint c_PageReadWrite = 0x04;

    private readonly List<IntPtr> _allocations = [];

    private VirtualPages(long address, int pageCount)
    {
        Address = address;
        PageCount = pageCount;
    }

    /// <summary>
    /// Allocates <paramref name="pageCount"/> committed, readable and writable pages.
    /// </summary>
    /// <param name="pageCount">The number of pages.</param>
    /// <returns>The pages, filled with bytes that are never zero and never form a test signature.</returns>
    public static VirtualPages Allocate(int pageCount)
    {
        var address = VirtualAlloc(IntPtr.Zero, (uint)(pageCount * c_PageSize), c_MemCommit | c_MemReserve, c_PageReadWrite);

        if (address == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var pages = new VirtualPages(address.ToInt64(), pageCount);
        pages._allocations.Add(address);
        pages.Fill(0, pageCount);
        return pages;
    }

    /// <summary>
    /// Allocates a readable page, free address space after it, and a second readable page as a separate allocation.
    /// </summary>
    /// <returns>The pages, where the page at <see cref="PageAddress"/> of <c>PageCount - 1</c> is the second allocation.</returns>
    public static VirtualPages AllocateWithFreeGap()
    {
        const int gapSize = 2 * c_AllocationGranularity;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            // Reserving and releasing the whole span finds free address space; another thread may take it before both pages are placed.
            var span = VirtualAlloc(IntPtr.Zero, gapSize + c_PageSize, c_MemReserve, c_PageNoAccess);

            if (span == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            VirtualFree(span, UIntPtr.Zero, c_MemRelease);

            var first = VirtualAlloc(span, c_PageSize, c_MemCommit | c_MemReserve, c_PageReadWrite);
            var second = VirtualAlloc(span + gapSize, c_PageSize, c_MemCommit | c_MemReserve, c_PageReadWrite);

            if (first == span && second == span + gapSize)
            {
                var pages = new VirtualPages(span.ToInt64(), (gapSize / c_PageSize) + 1);
                pages._allocations.Add(first);
                pages._allocations.Add(second);
                pages.Fill(0, 1);
                pages.Fill(pages.PageCount - 1, 1);
                return pages;
            }

            if (first != IntPtr.Zero)
                VirtualFree(first, UIntPtr.Zero, c_MemRelease);

            if (second != IntPtr.Zero)
                VirtualFree(second, UIntPtr.Zero, c_MemRelease);
        }

        throw new InvalidOperationException("Could not allocate two pages with free address space between them.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    public void Dispose()
    {
        foreach (var allocation in _allocations)
            VirtualFree(allocation, UIntPtr.Zero, c_MemRelease);

        _allocations.Clear();
    }

    /// <summary>
    /// Makes the page at <paramref name="index"/> unreadable.
    /// </summary>
    /// <param name="index">The zero-based index of the page.</param>
    /// <param name="kind">How the page is made unreadable.</param>
    public void MakeUnreadable(int index, UnreadablePageKind kind)
    {
        nint page = new IntPtr(PageAddress(index));
        bool succeeded = kind switch
        {
            UnreadablePageKind.NoAccess => VirtualProtect(page, c_PageSize, c_PageNoAccess, out _),
            UnreadablePageKind.Guard => VirtualProtect(page, c_PageSize, c_PageReadWrite | c_PageGuard, out _),
            UnreadablePageKind.Reserved => VirtualFree(page, c_PageSize, c_MemDecommit),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

        if (!succeeded)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>
    /// Returns the address of the page at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The zero-based index of the page.</param>
    /// <returns>The address of the first byte of the page.</returns>
    public long PageAddress(int index)
    {
        return Address + ((long)index * c_PageSize);
    }

    /// <summary>
    /// Copies <paramref name="length"/> bytes at <paramref name="address"/> out of the pages, which must be readable.
    /// </summary>
    /// <param name="address">The address to copy from.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The bytes.</returns>
    public byte[] Read(long address, int length)
    {
        byte[] data = new byte[length];
        Marshal.Copy(new IntPtr(address), data, 0, length);
        return data;
    }

    /// <summary>
    /// Copies <paramref name="data"/> to <paramref name="address"/>, which must lie in a readable and writable page.
    /// </summary>
    /// <param name="address">The address to copy to.</param>
    /// <param name="data">The bytes to copy.</param>
    public void Write(long address, byte[] data)
    {
        Marshal.Copy(data, 0, new IntPtr(address), data.Length);
    }

    private void Fill(int firstPage, int pageCount)
    {
        byte[] data = new byte[pageCount * c_PageSize];

        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)((i % 255) + 1);

        Write(PageAddress(firstPage), data);
    }

    /// <summary>
    /// Gets the address of the first page.
    /// </summary>
    public long Address { get; }

    /// <summary>
    /// Gets the address one past the last page.
    /// </summary>
    public long End => Address + ((long)PageCount * c_PageSize);

    /// <summary>
    /// Gets the number of pages from <see cref="Address"/> to the end of the last page.
    /// </summary>
    public int PageCount { get; }
}

/// <summary>
/// How <see cref="VirtualPages.MakeUnreadable"/> makes a page unreadable.
/// </summary>
public enum UnreadablePageKind
{
    /// <summary>
    /// The page is protected with <c>PAGE_NOACCESS</c>.
    /// </summary>
    NoAccess,

    /// <summary>
    /// The page is a <c>PAGE_GUARD</c> page.
    /// </summary>
    Guard,

    /// <summary>
    /// The page is decommitted, so it is reserved but not committed.
    /// </summary>
    Reserved
}