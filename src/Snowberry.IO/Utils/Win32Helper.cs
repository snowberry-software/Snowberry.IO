using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snowberry.IO.Utils;

[SupportedOSPlatform("windows")]
internal static class Win32Helper
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualProtectEx(IntPtr hProcess, long lpAddress, UIntPtr dwSize, uint flNewProtect, ref uint lpflOldProtect);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, long lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll")]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [In][Out] byte[] lpBuffer, ulong dwSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttribute, IntPtr dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(uint dwAccess, bool inherit, int pid);

    [Flags]
    public enum ProcessAccess : uint
    {
        /// <summary>
        /// Required to read memory in a process using <see cref="ReadProcessMemory(IntPtr, long, byte[], int, ref int)" />.
        /// </summary>
        PROCESS_VM_READ = 0x0010,

        /// <summary>
        /// Required to write to memory in a process using <see cref="WriteProcessMemory(IntPtr, IntPtr, byte[], ulong, out IntPtr)" />.
        /// </summary>
        PROCESS_VM_WRITE = 0x0020,

        /// <summary>
        /// Required to perform an operation on the address space of a process.
        /// </summary>
        PROCESS_VM_OPERATION = 0x0008,

        /// <summary>
        /// Required to create a thread.
        /// </summary>
        PROCESS_CREATE_THREAD = 0x0002,

        /// <summary>
        /// Required to retrieve certain information about a process, such as its token, exit code, and priority class.
        /// </summary>
        PROCESS_QUERY_INFORMATION = 0x0400
    }
}
