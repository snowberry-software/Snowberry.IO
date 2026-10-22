using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Snowberry.IO.Utils;

/// <summary>
/// P/Invoke declarations and constants for the Win32 process and memory APIs.
/// </summary>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
internal static unsafe class Win32Helper
{
    // https://learn.microsoft.com/windows/win32/api/memoryapi/nf-memoryapi-virtualallocex
    /// <summary>
    /// Allocation type that commits memory pages, and the <see cref="MemoryBasicInformation.State"/> of committed pages.
    /// </summary>
    public const uint c_MemCommit = 0x00001000;

    /// <summary>
    /// The <see cref="MemoryBasicInformation.State"/> of address space that is not allocated.
    /// </summary>
    public const uint c_MemFree = 0x00010000;

    /// <summary>
    /// Free type that releases reserved pages.
    /// </summary>
    public const uint c_MemRelease = 0x00008000;

    /// <summary>
    /// Allocation type that reserves a range of address space.
    /// </summary>
    public const uint c_MemReserve = 0x00002000;

    /// <summary>
    /// Memory protection modifier that turns pages into guard pages, which cannot be read with <see cref="ReadProcessMemory"/>.
    /// </summary>
    public const uint c_PageGuard = 0x100;

    // https://learn.microsoft.com/windows/win32/memory/memory-protection-constants
    /// <summary>
    /// Memory protection that disables all access.
    /// </summary>
    public const uint c_PageNoAccess = 0x01;

    /// <summary>
    /// Memory protection that allows read and write access.
    /// </summary>
    public const uint c_PageReadWrite = 0x04;

    /// <summary>
    /// The size of a memory page in bytes.
    /// </summary>
    public const int c_PageSize = 4096;

    /// <summary>
    /// Wait result that indicates the waited object is signaled.
    /// </summary>
    public const uint c_WaitObject0 = 0x00000000;

    private const string c_Kernel32 = "kernel32.dll";

    /// <summary>
    /// Closes an open object handle (Win32 <c>CloseHandle</c>).
    /// </summary>
    /// <param name="hObject">The handle to close.</param>
    /// <returns><see langword="true"/> if the handle was closed; otherwise, <see langword="false"/>.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Creates a thread that runs in another process (Win32 <c>CreateRemoteThread</c>).
    /// </summary>
    /// <param name="hProcess">The process handle.</param>
    /// <param name="lpThreadAttributes">The security attributes, or zero for the default.</param>
    /// <param name="dwStackSize">The initial stack size in bytes, or zero for the default.</param>
    /// <param name="lpStartAddress">The address of the thread start routine in the target process.</param>
    /// <param name="lpParameter">The argument passed to the start routine.</param>
    /// <param name="dwCreationFlags">The creation flags.</param>
    /// <param name="lpThreadId">A pointer that receives the thread identifier, or zero to ignore it.</param>
    /// <returns>A handle to the new thread, which is invalid if the call failed.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    public static extern ThreadSafeHandle CreateRemoteThread(SafeHandle hProcess, nint lpThreadAttributes, nuint dwStackSize, nint lpStartAddress, nint lpParameter, uint dwCreationFlags, nint lpThreadId);

    /// <summary>
    /// Retrieves the exit code of a thread (Win32 <c>GetExitCodeThread</c>).
    /// </summary>
    /// <param name="hThread">The thread handle.</param>
    /// <param name="lpExitCode">When this method returns, the thread exit code.</param>
    /// <returns><see langword="true"/> if the exit code was retrieved; otherwise, <see langword="false"/>.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeThread(SafeHandle hThread, out uint lpExitCode);

    /// <summary>
    /// Retrieves the handle of a module already loaded in the calling process (Win32 <c>GetModuleHandleW</c>).
    /// </summary>
    /// <param name="lpModuleName">The name of the module.</param>
    /// <returns>The module handle, or zero if the module is not loaded.</returns>
    [DllImport(c_Kernel32, SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    public static extern nint GetModuleHandle(string lpModuleName);

    /// <summary>
    /// Retrieves the address of an exported function from a loaded module (Win32 <c>GetProcAddress</c>).
    /// </summary>
    /// <param name="hModule">The module handle.</param>
    /// <param name="lpProcName">The name of the exported function.</param>
    /// <returns>The address of the function, or zero if it was not found.</returns>
    [DllImport(c_Kernel32, SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false)]
    public static extern nint GetProcAddress(nint hModule, string lpProcName);

    /// <summary>
    /// Determines whether a process runs under WOW64 (Win32 <c>IsWow64Process</c>).
    /// </summary>
    /// <param name="hProcess">The process handle.</param>
    /// <param name="wow64Process">When this method returns, <see langword="true"/> if the process is a 32-bit process on a 64-bit system.</param>
    /// <returns><see langword="true"/> if the call succeeded; otherwise, <see langword="false"/>.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWow64Process(SafeHandle hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    /// <summary>
    /// Opens an existing process (Win32 <c>OpenProcess</c>).
    /// </summary>
    /// <param name="dwDesiredAccess">The requested <see cref="ProcessAccess"/> rights.</param>
    /// <param name="bInheritHandle">Whether child processes inherit the returned handle.</param>
    /// <param name="dwProcessId">The identifier of the process to open.</param>
    /// <returns>A handle to the process, which is invalid if the call failed.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    public static extern ProcessSafeHandle OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    /// <summary>
    /// Reads memory from another process (Win32 <c>ReadProcessMemory</c>).
    /// </summary>
    /// <param name="hProcess">The process handle.</param>
    /// <param name="lpBaseAddress">The address in the process to read from.</param>
    /// <param name="lpBuffer">The buffer that receives the data.</param>
    /// <param name="nSize">The number of bytes to read.</param>
    /// <param name="lpNumberOfBytesRead">When this method returns, the number of bytes read.</param>
    /// <returns><see langword="true"/> if the read succeeded; otherwise, <see langword="false"/>.</returns>
    [DllImport(c_Kernel32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(SafeHandle hProcess, nint lpBaseAddress, byte* lpBuffer, nuint nSize, out nuint lpNumberOfBytesRead);

    /// <summary>
    /// Reserves or commits memory in another process (Win32 <c>VirtualAllocEx</c>).
    /// </summary>
    /// <param name="hProcess">The process handle.</param>
    /// <param name="lpAddress">The desired start address, or zero to let the system choose.</param>
    /// <param name="dwSize">The size of the region in bytes.</param>
    /// <param name="flAllocationType">The allocation type, such as <see cref="c_MemCommit"/>.</param>
    /// <param name="flProtect">The memory protection, such as <see cref="c_PageReadWrite"/>.</param>
    /// <returns>The base address of the allocated region, or zero if the call failed.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    public static extern nint VirtualAllocEx(SafeHandle hProcess, nint lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    /// <summary>
    /// Releases or decommits memory in another process (Win32 <c>VirtualFreeEx</c>).
    /// </summary>
    /// <param name="hProcess">The process handle.</param>
    /// <param name="lpAddress">The base address of the region to free.</param>
    /// <param name="dwSize">The size of the region in bytes; zero when releasing.</param>
    /// <param name="dwFreeType">The free type, such as <see cref="c_MemRelease"/>.</param>
    /// <returns><see langword="true"/> if the memory was freed; otherwise, <see langword="false"/>.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualFreeEx(SafeHandle hProcess, nint lpAddress, nuint dwSize, uint dwFreeType);

    /// <summary>
    /// Retrieves information about the region of pages that contains an address in another process (Win32 <c>VirtualQueryEx</c>).
    /// </summary>
    /// <param name="hProcess">The process handle, which needs <c>PROCESS_QUERY_INFORMATION</c> or <c>PROCESS_QUERY_LIMITED_INFORMATION</c> access.</param>
    /// <param name="lpAddress">The address in the process to query.</param>
    /// <param name="lpBuffer">When this method returns, the information about the region.</param>
    /// <param name="dwLength">The size of <paramref name="lpBuffer"/> in bytes.</param>
    /// <returns>The number of bytes written to <paramref name="lpBuffer"/>, or zero if the call failed.</returns>
    [DllImport(c_Kernel32)]
    public static extern nuint VirtualQueryEx(SafeHandle hProcess, nint lpAddress, out MemoryBasicInformation lpBuffer, nuint dwLength);

    /// <summary>
    /// Waits until an object is signaled or the timeout elapses (Win32 <c>WaitForSingleObject</c>).
    /// </summary>
    /// <param name="hHandle">The handle of the object to wait for.</param>
    /// <param name="dwMilliseconds">The timeout in milliseconds.</param>
    /// <returns>The wait result, where <see cref="c_WaitObject0"/> indicates the object was signaled.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    public static extern uint WaitForSingleObject(SafeHandle hHandle, uint dwMilliseconds);

    /// <summary>
    /// Writes memory in another process (Win32 <c>WriteProcessMemory</c>).
    /// </summary>
    /// <param name="hProcess">The process handle.</param>
    /// <param name="lpBaseAddress">The address in the process to write to.</param>
    /// <param name="lpBuffer">The buffer that contains the data.</param>
    /// <param name="nSize">The number of bytes to write.</param>
    /// <param name="lpNumberOfBytesWritten">When this method returns, the number of bytes written.</param>
    /// <returns><see langword="true"/> if the write succeeded; otherwise, <see langword="false"/>.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(SafeHandle hProcess, nint lpBaseAddress, byte* lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);

    /// <summary>
    /// Information about a region of pages with the same state and protection (Win32 <c>MEMORY_BASIC_INFORMATION</c>).
    /// </summary>
    /// <remarks>The <c>PartitionId</c> of the 64-bit layout lies in the padding after <see cref="AllocationProtect"/>, so this layout fits both bitnesses.</remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryBasicInformation
    {
        /// <summary>
        /// The base address of the allocation that contains the region.
        /// </summary>
        public nuint AllocationBase;

        /// <summary>
        /// The memory protection of the allocation when it was allocated.
        /// </summary>
        public uint AllocationProtect;

        /// <summary>
        /// The base address of the region.
        /// </summary>
        public nuint BaseAddress;

        /// <summary>
        /// The memory protection of the pages in the region, such as <see cref="c_PageReadWrite"/>.
        /// </summary>
        public uint Protect;

        /// <summary>
        /// The size of the region in bytes.
        /// </summary>
        public nuint RegionSize;

        /// <summary>
        /// The state of the pages in the region, such as <see cref="c_MemCommit"/>.
        /// </summary>
        public uint State;

        /// <summary>
        /// The type of the pages in the region.
        /// </summary>
        public uint Type;
    }

    /// <summary>
    /// Access rights for a process handle.
    /// </summary>
    [Flags]
    public enum ProcessAccess : uint
    {
        /// <summary>
        /// Required to create a thread.
        /// </summary>
        PROCESS_CREATE_THREAD = 0x0002,

        /// <summary>
        /// Required to perform an operation on the address space of a process.
        /// </summary>
        PROCESS_VM_OPERATION = 0x0008,

        /// <summary>
        /// Required to read memory in a process.
        /// </summary>
        PROCESS_VM_READ = 0x0010,

        /// <summary>
        /// Required to write to memory in a process.
        /// </summary>
        PROCESS_VM_WRITE = 0x0020,

        /// <summary>
        /// Required to retrieve certain information about a process, such as its token, exit code, and priority class.
        /// </summary>
        PROCESS_QUERY_INFORMATION = 0x0400,

        /// <summary>
        /// Required to retrieve limited information about a process, such as whether it runs under WOW64.
        /// </summary>
        PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    }

    /// <summary>
    /// A process handle whose release can be suppressed for handles owned by the caller.
    /// </summary>
    internal sealed class ProcessSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Creates an owning handle; used by the marshaller.
        /// </summary>
        public ProcessSafeHandle() : base(ownsHandle: true)
        {
        }

        /// <summary>
        /// Wraps an existing handle.
        /// </summary>
        /// <param name="existingHandle">The existing process handle.</param>
        public ProcessSafeHandle(IntPtr existingHandle) : base(ownsHandle: true)
        {
            SetHandle(existingHandle);
        }

        /// <inheritdoc/>
        protected override bool ReleaseHandle()
        {
            return !CloseOnRelease || CloseHandle(handle);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the native handle is closed when this instance is released.
        /// </summary>
        public bool CloseOnRelease { get; set; } = true;
    }

    /// <summary>
    /// An owning thread handle.
    /// </summary>
    internal sealed class ThreadSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Creates an owning handle; used by the marshaller.
        /// </summary>
        public ThreadSafeHandle() : base(ownsHandle: true)
        {
        }

        /// <inheritdoc/>
        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }
}