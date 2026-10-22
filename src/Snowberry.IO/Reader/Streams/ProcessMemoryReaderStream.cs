using static Snowberry.IO.Utils.Win32Helper;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Snowberry.IO.Reader.Streams;

/// <summary>
/// The <see cref="ReaderStream"/> of a <see cref="Win32ProcessReader"/>, which reads the memory of a process with <c>ReadProcessMemory</c>.
/// </summary>
/// <remarks>
/// Without a region view, <see cref="Stream.Position"/> is a virtual address in the target process and <see cref="Stream.Length"/> is <c>0</c>.
/// </remarks>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
internal sealed class ProcessMemoryReaderStream : ReaderStream
{
    private readonly ProcessSafeHandle _handle;
    private long _address;

    /// <summary>
    /// Creates a stream over the memory of the process behind <paramref name="handle"/>.
    /// </summary>
    /// <param name="handle">The process handle; this stream does not close it.</param>
    /// <param name="startAddress">The initial virtual address.</param>
    public ProcessMemoryReaderStream(ProcessSafeHandle handle, long startAddress)
    {
        _handle = handle;
        _address = startAddress;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var span = buffer.AsSpan(offset, count);
        int read = ReadUnreported(span);

        _analyzer?.AnalyzeReadBytes(Owner, span, read);
        return read;
    }

#if !NETSTANDARD2_0
    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        int read = ReadUnreported(buffer);

        _analyzer?.AnalyzeReadBytes(Owner, buffer, read);
        return read;
    }
#endif

    /// <inheritdoc/>
    public override int ReadByte()
    {
        Span<byte> single = stackalloc byte[1];
        int read = ReadUnreported(single);

        _analyzer?.AnalyzeReadBytes(Owner, single, read);
        return read == 0 ? -1 : single[0];
    }

    /// <summary>
    /// Reads the memory at <paramref name="address"/> into <paramref name="destination"/>.
    /// </summary>
    /// <remarks>
    /// <c>ReadProcessMemory</c> copies nothing when the range touches a page that cannot be read, such as a <c>PAGE_NOACCESS</c>,
    /// <c>PAGE_GUARD</c> or reserved page. Only then, and only for a range that spans more than one page, the readable pages before it
    /// are read again on their own.
    /// </remarks>
    /// <param name="address">The virtual address to read from.</param>
    /// <param name="destination">The buffer to fill.</param>
    /// <returns>The number of bytes read; fewer than requested when the memory past them is not readable.</returns>
    internal unsafe int ReadMemory(long address, Span<byte> destination)
    {
        if (destination.IsEmpty)
            return 0;

        // A 32-bit process cannot address memory above 4 GB.
        if (IntPtr.Size == 4 && (ulong)address > uint.MaxValue)
            return 0;

        fixed (byte* destinationPointer = destination)
        {
            int read = ReadOnce(address, destinationPointer, destination.Length);

            if (read == 0 && destination.Length > c_PageSize - (int)(address & (c_PageSize - 1)))
                read = ReadReadablePrefix(address, destinationPointer, destination.Length);

            return read;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The stream is disposed.</exception>
    internal override int ReadUnreported(Span<byte> buffer)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().FullName);

        int toRead = ClampToRegion(buffer.Length);
        int read = ReadMemory(_address, buffer[..toRead]);

        _address += read;

        if (_isRegionViewEnabled)
            _viewOffset += read;

        return read;
    }

    /// <summary>
    /// Moves the position from allocated memory that cannot be read to the start of the next readable region.
    /// </summary>
    /// <remarks>
    /// The region of pages at the position is skipped as a whole, and the regions after it are queried with <c>VirtualQueryEx</c> until
    /// one is committed and neither <c>PAGE_NOACCESS</c> nor <c>PAGE_GUARD</c>. Address space that is not allocated ends the data, so
    /// the position never moves across it, and it never moves without query access either.
    /// </remarks>
    /// <param name="maxPosition">The highest <see cref="Stream.Position"/> to move to.</param>
    /// <returns>
    /// <see langword="true"/> if the position moved to readable memory at or before <paramref name="maxPosition"/> and inside the region
    /// view, with only allocated memory before it; otherwise, <see langword="false"/>, and the position is unchanged.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The stream is disposed.</exception>
    internal bool SkipUnreadable(long maxPosition)
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().FullName);

        long address = _address;
        long maxAddress = _isRegionViewEnabled
            ? _regionView.StartPosition + Math.Min(maxPosition, _regionView.Size - 1)
            : maxPosition;

        if (address >= maxAddress || !TryQueryRegion(address, out long next, out bool isAllocated, out _) || !isAllocated)
            return false;

        while (next <= maxAddress && TryQueryRegion(next, out long regionEnd, out isAllocated, out bool isReadable) && isAllocated)
        {
            if (isReadable)
            {
                Position += next - address;
                return true;
            }

            next = regionEnd;
        }

        return false;
    }

    /// <summary>
    /// Finds where the readable memory that starts at <paramref name="address"/> ends.
    /// </summary>
    /// <param name="address">The virtual address where the readable memory starts.</param>
    /// <param name="end">The virtual address to stop at.</param>
    /// <returns>
    /// The end of the readable memory, at most <paramref name="end"/>; <paramref name="address"/> when its page cannot be read;
    /// or -1 when the memory at <paramref name="address"/> cannot be queried.
    /// </returns>
    private long QueryReadableEnd(long address, long end)
    {
        long current = address;

        while (current < end)
        {
            if (!TryQueryRegion(current, out long regionEnd, out _, out bool isReadable))
                return current == address ? -1 : current;

            if (!isReadable)
                return current;

            current = regionEnd;
        }

        return end;
    }

    /// <summary>
    /// Reads <paramref name="length"/> bytes at <paramref name="address"/> with a single <c>ReadProcessMemory</c> call.
    /// </summary>
    /// <param name="address">The virtual address to read from.</param>
    /// <param name="destination">The pinned buffer to fill.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    private unsafe int ReadOnce(long address, byte* destination, int length)
    {
        // On ERROR_PARTIAL_COPY the call fails but still reports the bytes that were copied.
        _ = ReadProcessMemory(_handle, (nint)address, destination, (nuint)length, out nuint read);
        return (int)read;
    }

    /// <summary>
    /// Reads the readable pages at the start of a range that <c>ReadProcessMemory</c> could not read as a whole.
    /// </summary>
    /// <remarks>
    /// <c>VirtualQueryEx</c> tells where the readable memory ends, so a single read follows. Without query access, or when the
    /// protection changed since the query, the range is read page by page up to the first page that cannot be read.
    /// </remarks>
    /// <param name="address">The virtual address to read from.</param>
    /// <param name="destination">The pinned buffer to fill.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>The number of bytes read; zero when the first page cannot be read.</returns>
    private unsafe int ReadReadablePrefix(long address, byte* destination, int length)
    {
        long readableEnd = QueryReadableEnd(address, address + length);

        if (readableEnd == address)
            return 0;

        if (readableEnd > address)
        {
            int read = ReadOnce(address, destination, (int)(readableEnd - address));

            if (read != 0)
                return read;
        }

        int total = 0;

        while (total < length)
        {
            long pageAddress = address + total;
            int pageLength = Math.Min(length - total, c_PageSize - (int)(pageAddress & (c_PageSize - 1)));
            int read = ReadOnce(pageAddress, destination + total, pageLength);

            total += read;

            if (read != pageLength)
                break;
        }

        return total;
    }

    /// <summary>
    /// Queries the region of pages with the same state and protection that contains <paramref name="address"/>.
    /// </summary>
    /// <param name="address">The virtual address to query.</param>
    /// <param name="regionEnd">When this method returns <see langword="true"/>, the end of the region.</param>
    /// <param name="isAllocated">When this method returns <see langword="true"/>, whether the region is reserved or committed.</param>
    /// <param name="isReadable">
    /// When this method returns <see langword="true"/>, whether the region is committed and neither <c>PAGE_NOACCESS</c> nor <c>PAGE_GUARD</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the region was queried; otherwise, <see langword="false"/>, for example when the handle lacks
    /// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> access or <paramref name="address"/> is outside the user address space.
    /// </returns>
    private unsafe bool TryQueryRegion(long address, out long regionEnd, out bool isAllocated, out bool isReadable)
    {
        // A 32-bit process cannot address memory above 4 GB.
        if ((IntPtr.Size == 4 && (ulong)address > uint.MaxValue)
            || VirtualQueryEx(_handle, (nint)address, out var info, (nuint)sizeof(MemoryBasicInformation)) == 0)
        {
            regionEnd = 0;
            isAllocated = false;
            isReadable = false;
            return false;
        }

        regionEnd = (long)info.BaseAddress + (long)info.RegionSize;
        isAllocated = info.State != c_MemFree;
        isReadable = info.State == c_MemCommit && (info.Protect & (c_PageNoAccess | c_PageGuard)) == 0;
        return true;
    }

    /// <inheritdoc/>
    public override bool CanSeek => !_disposed;

    /// <inheritdoc/>
    /// <remarks>Always <c>0</c>, because process memory has no known length.</remarks>
    internal override long ActualLength => 0;

    /// <summary>
    /// Gets the handle of the process.
    /// </summary>
    internal ProcessSafeHandle Handle => _handle;

    /// <inheritdoc/>
    protected override long SourcePosition
    {
        get => _address;
        set => _address = value;
    }

    /// <inheritdoc/>
    protected override long UnviewedLength => 0;
}