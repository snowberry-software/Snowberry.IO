using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using Microsoft.Win32.SafeHandles;

namespace Snowberry.IO.Benchmarks.Process;

/// <summary>
/// The Win32 functions that the process reader baselines call directly.
/// </summary>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
internal static unsafe class NativeMethods
{
    /// <summary>
    /// The allocation type that commits memory pages.
    /// </summary>
    public const uint c_MemCommit = 0x00001000;

    /// <summary>
    /// The allocation type that reserves a range of address space.
    /// </summary>
    public const uint c_MemReserve = 0x00002000;

    /// <summary>
    /// The memory protection that allows read and write access.
    /// </summary>
    public const uint c_PageReadWrite = 0x04;

    /// <summary>
    /// The access right to query limited information about a process.
    /// </summary>
    public const uint c_ProcessQueryLimitedInformation = 0x1000;

    /// <summary>
    /// The access right to read the memory of a process.
    /// </summary>
    public const uint c_ProcessVmRead = 0x0010;

    private const string c_Kernel32 = "kernel32.dll";

    /// <summary>
    /// Opens an existing process (Win32 <c>OpenProcess</c>).
    /// </summary>
    /// <param name="dwDesiredAccess">The requested access rights.</param>
    /// <param name="bInheritHandle">Whether child processes inherit the returned handle.</param>
    /// <param name="dwProcessId">The identifier of the process to open.</param>
    /// <returns>A handle to the process, which is invalid if the call failed.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    public static extern ProcessHandle OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    /// <summary>
    /// Reads memory from a process (Win32 <c>ReadProcessMemory</c>), declared like <see cref="IO.Reader.Win32ProcessReader"/>
    /// declares it: a <see cref="SafeHandle"/>, an <see langword="out"/> parameter and a marshalled <c>BOOL</c> result.
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
    /// Reads memory from a process (Win32 <c>ReadProcessMemory</c>), declared with blittable parameters only, so the call needs no
    /// marshalling.
    /// </summary>
    /// <param name="hProcess">The raw process handle.</param>
    /// <param name="lpBaseAddress">The address in the process to read from.</param>
    /// <param name="lpBuffer">The buffer that receives the data.</param>
    /// <param name="nSize">The number of bytes to read.</param>
    /// <param name="lpNumberOfBytesRead">The variable that receives the number of bytes read.</param>
    /// <returns>A nonzero value if the read succeeded; otherwise, zero.</returns>
    [DllImport(c_Kernel32, EntryPoint = "ReadProcessMemory")]
    public static extern int ReadProcessMemoryBlittable(nint hProcess, nint lpBaseAddress, byte* lpBuffer, nuint nSize, nuint* lpNumberOfBytesRead);

    /// <summary>
    /// Allocates memory in a process (Win32 <c>VirtualAllocEx</c>).
    /// </summary>
    /// <param name="hProcess">The raw process handle.</param>
    /// <param name="lpAddress">The requested address, or zero to let the system choose.</param>
    /// <param name="dwSize">The number of bytes to allocate.</param>
    /// <param name="flAllocationType">The allocation type.</param>
    /// <param name="flProtect">The memory protection.</param>
    /// <returns>The address of the allocated memory, or zero if the call failed.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    public static extern nint VirtualAllocEx(nint hProcess, nint lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    /// <summary>
    /// Writes memory in a process (Win32 <c>WriteProcessMemory</c>).
    /// </summary>
    /// <param name="hProcess">The raw process handle.</param>
    /// <param name="lpBaseAddress">The address in the process to write to.</param>
    /// <param name="lpBuffer">The data to write.</param>
    /// <param name="nSize">The number of bytes to write.</param>
    /// <param name="lpNumberOfBytesWritten">When this method returns, the number of bytes written.</param>
    /// <returns><see langword="true"/> if the write succeeded; otherwise, <see langword="false"/>.</returns>
    [DllImport(c_Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte* lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);

    [DllImport(c_Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    /// <summary>
    /// A process handle that is closed when it is released.
    /// </summary>
    public sealed class ProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessHandle"/> class, which the interop marshaller fills in.
        /// </summary>
        public ProcessHandle() : base(ownsHandle: true)
        {
        }

        /// <inheritdoc/>
        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }
}