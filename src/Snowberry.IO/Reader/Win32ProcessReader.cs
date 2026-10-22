using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Reader.Streams;
using Snowberry.IO.Utils;
using static Snowberry.IO.Utils.Win32Helper;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Snowberry.IO.Reader;

/// <summary>
/// A <see cref="BinaryReader"/> over the memory of a Windows process.
/// </summary>
/// <remarks>
/// <para><see cref="BaseEndianReader.Position"/> is a virtual address in the target process. Without a region view,
/// <see cref="BaseEndianReader.Length"/> is <c>0</c>, so string methods that depend on the length of the data do not work;
/// use <see cref="ReadCString"/> to read strings.</para>
/// <para>This type is not thread-safe.</para>
/// </remarks>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class Win32ProcessReader : BaseEndianReader
{
    /// <summary>
    /// The default access rights requested by <see cref="Win32ProcessReader(int, long, Analyzer?, uint)"/>:
    /// <c>PROCESS_VM_READ</c> and <c>PROCESS_QUERY_LIMITED_INFORMATION</c>.
    /// </summary>
    public const uint c_DefaultAccessFlags = (uint)(ProcessAccess.PROCESS_VM_READ | ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION);

    /// <summary>
    /// The maximum number of bytes <see cref="ReadCString"/> reads before it gives up looking for the terminator.
    /// </summary>
    public const int c_MaxCStringByteLength = 1024 * 1024;

    private readonly ProcessMemoryReaderStream _memory;
    private readonly ProcessSafeHandle _processHandle;
    private bool? _isTarget32Bit;

    /// <summary>
    /// Opens the process with the given id and creates a reader for it.
    /// </summary>
    /// <param name="pid">The id of the process.</param>
    /// <param name="startPosition">The initial virtual address used for <see cref="BaseEndianReader.Position"/>.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="accessFlags">
    /// The access rights passed to <c>OpenProcess</c>. Reading requires <c>PROCESS_VM_READ</c> (<c>0x0010</c>);
    /// <see cref="InjectDLL(string, int)"/> additionally requires <c>PROCESS_CREATE_THREAD</c> (<c>0x0002</c>),
    /// <c>PROCESS_VM_OPERATION</c> (<c>0x0008</c>) and <c>PROCESS_VM_WRITE</c> (<c>0x0020</c>).
    /// </param>
    /// <exception cref="Win32Exception">The process could not be opened.</exception>
    public Win32ProcessReader(int pid, long startPosition, Analyzer? analyzer = null, uint accessFlags = c_DefaultAccessFlags)
        : this(new ProcessMemoryReaderStream(OpenProcessHandle(pid, accessFlags), startPosition), analyzer)
    {
    }

    /// <summary>
    /// Creates a reader for an already opened process handle.
    /// </summary>
    /// <remarks>
    /// The handle is closed when the reader is disposed unless <see cref="CloseHandleOnDispose"/> is set to <see langword="false"/>.
    /// Set it to <see langword="false"/> when the handle is owned by someone else, for example <see cref="System.Diagnostics.Process.Handle"/>.
    /// </remarks>
    /// <param name="handle">The handle of the process.</param>
    /// <param name="startPosition">The initial virtual address used for <see cref="BaseEndianReader.Position"/>.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <exception cref="ArgumentException"><paramref name="handle"/> is <see cref="IntPtr.Zero"/>.</exception>
    public Win32ProcessReader(IntPtr handle, long startPosition, Analyzer? analyzer = null)
        : this(new ProcessMemoryReaderStream(WrapProcessHandle(handle), startPosition), analyzer)
    {
    }

    /// <summary>
    /// Creates a reader over <paramref name="memory"/>.
    /// </summary>
    /// <param name="memory">The stream over the memory of the process.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    private Win32ProcessReader(ProcessMemoryReaderStream memory, Analyzer? analyzer) : base(memory, analyzer, 0, EncodingDefaults.s_Utf8)
    {
        _memory = memory;
        _processHandle = memory.Handle;
        analyzer?.Initialize(this);
    }

    /// <summary>
    /// Finds the first null code unit in <paramref name="codeUnits"/>.
    /// </summary>
    /// <param name="codeUnits">The whole code units to search.</param>
    /// <param name="codeUnitSize">The size of a code unit in bytes: 1, 2 or 4.</param>
    /// <returns>The byte offset of the null code unit, or -1 if there is none.</returns>
    private static int IndexOfNullCodeUnit(ReadOnlySpan<byte> codeUnits, int codeUnitSize)
    {
        int index = codeUnitSize switch
        {
            1 => codeUnits.IndexOf((byte)0),
            2 => MemoryMarshal.Cast<byte, ushort>(codeUnits).IndexOf((ushort)0),
            _ => MemoryMarshal.Cast<byte, uint>(codeUnits).IndexOf(0u)
        };

        return index < 0 ? -1 : index * codeUnitSize;
    }

    /// <summary>
    /// Opens the process with the given id.
    /// </summary>
    /// <param name="pid">The id of the process.</param>
    /// <param name="accessFlags">The access rights passed to <c>OpenProcess</c>.</param>
    /// <returns>The handle of the process.</returns>
    /// <exception cref="Win32Exception">The process could not be opened.</exception>
    private static ProcessSafeHandle OpenProcessHandle(int pid, uint accessFlags)
    {
        var handle = OpenProcess(accessFlags, false, pid);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return handle;
    }

    /// <summary>
    /// Wraps an already opened process handle.
    /// </summary>
    /// <param name="handle">The handle of the process.</param>
    /// <returns>The wrapped handle, which closes <paramref name="handle"/> on release.</returns>
    /// <exception cref="ArgumentException"><paramref name="handle"/> is <see cref="IntPtr.Zero"/>.</exception>
    private static ProcessSafeHandle WrapProcessHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            throw new ArgumentException("The process handle must not be zero.", nameof(handle));

        return new ProcessSafeHandle(handle);
    }

    /// <summary>
    /// Always throws, because process memory has no end to copy to.
    /// </summary>
    /// <param name="destination">The destination stream.</param>
    /// <exception cref="NotSupportedException">Always thrown; use <see cref="CopyTo(Stream, int, int)"/> instead.</exception>
    public override void CopyTo(Stream destination)
    {
        throw new NotSupportedException($"Process memory has no end; use {nameof(CopyTo)} with a length instead.");
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or <paramref name="bufferSize"/> is not positive.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public override void CopyTo(Stream destination, int length, int bufferSize = 0x14000)
    {
        ThrowIfDisposed();

        _ = destination ?? throw new ArgumentNullException(nameof(destination));

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");

        if (bufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "Buffer size must be greater than zero.");

        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Min(bufferSize, Math.Max(length, 1)));

        try
        {
            while (length > 0)
            {
                int read = Read(buffer.AsSpan(0, Math.Min(buffer.Length, length)));

                if (read == 0)
                    break;

                destination.Write(buffer, 0, read);
                length -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }


    /// <summary>
    /// Restricts reading to the given region of the target process's address space.
    /// </summary>
    /// <remarks>
    /// Unlike stream-based readers, the region is not checked against <see cref="BaseEndianReader.ActualLength"/>, because process memory has no known length.
    /// </remarks>
    /// <param name="region">The region, where <see cref="RegionRange.StartPosition"/> is a virtual address.</param>
    /// <exception cref="ArgumentOutOfRangeException">The start or size of <paramref name="region"/> is negative.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public override void EnableRegionView(RegionRange region)
    {
        ThrowIfDisposed();

        if (region.StartPosition < 0 || region.Size < 0)
            throw new ArgumentOutOfRangeException(nameof(region), "The region start and size must be non-negative.");

        _readerStream.EnableRegionView(region);
    }

    /// <summary>
    /// Loads a dynamic library into the target process by running <c>LoadLibraryW</c> on a remote thread.
    /// </summary>
    /// <remarks>
    /// The process must have been opened with <c>PROCESS_CREATE_THREAD</c>, <c>PROCESS_VM_OPERATION</c> and <c>PROCESS_VM_WRITE</c> access,
    /// and must have the same bitness as the current process. If the remote thread does not finish within
    /// <paramref name="timeoutMilliseconds"/>, the memory holding the library path is left allocated because the thread may still use it.
    /// </remarks>
    /// <param name="filePath">The path of the dynamic library to load.</param>
    /// <param name="timeoutMilliseconds">
    /// The time to wait for the library to be loaded, or <see cref="Timeout.Infinite"/> to wait indefinitely.
    /// </param>
    /// <returns><see langword="true"/> if the library was loaded; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeoutMilliseconds"/> is negative and not <see cref="Timeout.Infinite"/>.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public unsafe bool InjectDLL(string filePath, int timeoutMilliseconds = 10_000)
    {
        ThrowIfDisposed();

        _ = filePath ?? throw new ArgumentNullException(nameof(filePath));

        if (timeoutMilliseconds < 0 && timeoutMilliseconds != Timeout.Infinite)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), "Timeout must be non-negative or Timeout.Infinite.");

        filePath = Path.GetFullPath(filePath);

        nint kernel32 = GetModuleHandle("kernel32.dll");
        if (kernel32 == 0)
            return false;

        nint loadLibraryFunction = GetProcAddress(kernel32, "LoadLibraryW");
        if (loadLibraryFunction == 0)
            return false;

        byte[] pathBytes = Encoding.Unicode.GetBytes(filePath + "\0");

        nint remotePath = VirtualAllocEx(_processHandle, 0, (nuint)pathBytes.Length, c_MemCommit | c_MemReserve, c_PageReadWrite);
        if (remotePath == 0)
            return false;

        bool releaseRemotePath = true;

        try
        {
            fixed (byte* pathPointer = pathBytes)
            {
                if (!WriteProcessMemory(_processHandle, remotePath, pathPointer, (nuint)pathBytes.Length, out nuint written) || written != (nuint)pathBytes.Length)
                    return false;
            }

            using var thread = CreateRemoteThread(_processHandle, 0, 0, loadLibraryFunction, remotePath, 0, 0);

            if (thread.IsInvalid)
                return false;

            if (WaitForSingleObject(thread, unchecked((uint)timeoutMilliseconds)) != c_WaitObject0)
            {
                releaseRemotePath = false;
                return false;
            }

            // The exit code is the low 32 bits of the module handle returned by LoadLibraryW; zero means the load failed.
            return GetExitCodeThread(thread, out uint exitCode) && exitCode != 0;
        }
        finally
        {
            if (releaseRemotePath)
                _ = VirtualFreeEx(_processHandle, remotePath, 0, c_MemRelease);
        }
    }

    /// <summary>
    /// Reads a pointer at the current <see cref="BaseEndianReader.Position"/> and returns the null-terminated string it points to.
    /// </summary>
    /// <remarks>
    /// The pointer is 4 bytes for 32-bit target processes and 8 bytes otherwise, and <see cref="BaseEndianReader.Position"/> advances past it.
    /// Detecting a 32-bit target requires <c>PROCESS_QUERY_LIMITED_INFORMATION</c> access; without it the bitness of the current process is assumed.
    /// The string is decoded with <see cref="BaseEndianReader.Encoding"/>.
    /// </remarks>
    /// <returns>The string the pointer points to.</returns>
    /// <exception cref="EndOfStreamException">The pointer or the string could not be read.</exception>
    /// <exception cref="InvalidDataException">No terminator was found within <see cref="c_MaxCStringByteLength"/> bytes.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    public override string ReadCString()
    {
        ThrowIfDisposed();

        long stringAddress = IsTarget32Bit ? ReadUInt32() : ReadInt64();
        return ReadCStringAt(stringAddress);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Skips the allocated pages that are not committed or are <c>PAGE_NOACCESS</c> or <c>PAGE_GUARD</c>, which requires
    /// <c>PROCESS_QUERY_LIMITED_INFORMATION</c> access. Address space that is not allocated ends the data.
    /// </remarks>
    internal override bool SkipUnreadableData(long maxPosition)
    {
        return _memory.SkipUnreadable(maxPosition);
    }

    /// <summary>
    /// Releases the resources of the reader and closes the process handle unless <see cref="CloseHandleOnDispose"/> is <see langword="false"/>.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (!Disposed && disposing)
            _processHandle.Dispose();

        base.Dispose(disposing);
    }

    private bool DetermineIsTarget32Bit()
    {
        if (!Environment.Is64BitOperatingSystem)
            return true;

        if (IsWow64Process(_processHandle, out bool isWow64))
            return isWow64;

        return !Environment.Is64BitProcess;
    }

    /// <summary>
    /// Reads the null-terminated string at <paramref name="address"/> without moving <see cref="BaseEndianReader.Position"/>.
    /// </summary>
    /// <remarks>
    /// The memory is read in chunks that never cross a page boundary, so an unreadable next page cannot fail an otherwise readable chunk.
    /// The bytes are not reported to the <see cref="BaseEndianReader.Analyzer"/>.
    /// </remarks>
    /// <param name="address">The virtual address of the string.</param>
    /// <returns>The string, decoded with <see cref="BaseEndianReader.Encoding"/>.</returns>
    /// <exception cref="EndOfStreamException">The string could not be read.</exception>
    /// <exception cref="InvalidDataException">No terminator was found within <see cref="c_MaxCStringByteLength"/> bytes.</exception>
    private string ReadCStringAt(long address)
    {
        int codeUnitSize = CodeUnitSize;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(256);
        int length = 0;
        int scanFrom = 0;

        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    if (buffer.Length >= c_MaxCStringByteLength)
                        throw new InvalidDataException($"No string terminator found within {c_MaxCStringByteLength} bytes.");

                    byte[] larger = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length * 2, c_MaxCStringByteLength));
                    Array.Copy(buffer, larger, length);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = larger;
                }

                // Never cross a page boundary so an unreadable next page cannot fail an otherwise readable chunk.
                long chunkAddress = address + length;
                int toPageEnd = c_PageSize - (int)(chunkAddress & (c_PageSize - 1));
                int chunkSize = Math.Min(buffer.Length - length, toPageEnd);

                int read = _memory.ReadMemory(chunkAddress, buffer.AsSpan(length, chunkSize));
                if (read == 0)
                    ThrowEndOfStreamException();

                length += read;

                // Search the code units that this chunk completed; a chunk can end in the middle of one.
                int aligned = length - (length % codeUnitSize);
                int terminator = IndexOfNullCodeUnit(buffer.AsSpan(scanFrom, aligned - scanFrom), codeUnitSize);

                if (terminator >= 0)
                    return _encoding.GetString(buffer, 0, scanFrom + terminator);

                scanFrom = aligned;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Gets a read-only, seekable stream over the memory of the target process.
    /// </summary>
    /// <remarks>
    /// This is the stream that the inherited <see cref="BinaryReader"/> reads use. It shares <see cref="BaseEndianReader.Position"/>,
    /// <see cref="BaseEndianReader.Length"/>, the region view and the <see cref="BaseEndianReader.Analyzer"/> with this reader,
    /// so reading it advances the reader. Without a region view, its position is a virtual address and its length is <c>0</c>.
    /// It is disposed together with the reader.
    /// </remarks>
    public override Stream BaseStream => base.BaseStream;

    /// <inheritdoc/>
    public override bool CanReadData => !Disposed && !_processHandle.IsInvalid && (!IsRegionViewEnabled || Position < Length);

    /// <summary>
    /// Gets or sets a value indicating whether the process handle is closed when the reader is disposed or finalized.
    /// </summary>
    /// <remarks>Defaults to <see langword="true"/>.</remarks>
    public bool CloseHandleOnDispose
    {
        get => _processHandle.CloseOnRelease;
        set => _processHandle.CloseOnRelease = value;
    }

    private bool IsTarget32Bit => _isTarget32Bit ??= DetermineIsTarget32Bit();
}