using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace Snowberry.IO.Utils;

/// <summary>
/// The Win32 declarations that the CsWin32 source generator (<see cref="PInvoke"/>) cannot provide.
/// </summary>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows5.0")]
#endif
internal static class Win32Helper
{
    /// <summary>
    /// The size of a memory page in bytes.
    /// </summary>
    public const int c_PageSize = 4096;

    private const string c_Kernel32 = "kernel32.dll";

    // NOTE(VNC): CsWin32 generates VirtualQueryEx and MEMORY_BASIC_INFORMATION only for a specific CPU architecture, because the
    // struct differs between x86 and x64. This library is AnyCPU, so both are declared here in a layout that fits both bitnesses.
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
    /// Information about a region of pages with the same state and protection (Win32 <c>MEMORY_BASIC_INFORMATION</c>).
    /// </summary>
    /// <remarks>
    /// The fields must stay in the order of the native struct; do not sort them. The <c>PartitionId</c> of the 64-bit layout lies in the
    /// padding after <see cref="AllocationProtect"/>, so this layout fits both bitnesses.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryBasicInformation
    {
        /// <summary>
        /// The base address of the region.
        /// </summary>
        public nuint BaseAddress;

        /// <summary>
        /// The base address of the allocation that contains the region.
        /// </summary>
        public nuint AllocationBase;

        /// <summary>
        /// The memory protection of the allocation when it was allocated.
        /// </summary>
        public PAGE_PROTECTION_FLAGS AllocationProtect;

        /// <summary>
        /// The size of the region in bytes.
        /// </summary>
        public nuint RegionSize;

        /// <summary>
        /// The state of the pages in the region, such as <see cref="VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT"/>.
        /// </summary>
        public VIRTUAL_ALLOCATION_TYPE State;

        /// <summary>
        /// The memory protection of the pages in the region, such as <see cref="PAGE_PROTECTION_FLAGS.PAGE_READWRITE"/>.
        /// </summary>
        public PAGE_PROTECTION_FLAGS Protect;

        /// <summary>
        /// The type of the pages in the region.
        /// </summary>
        public uint Type;
    }

    /// <summary>
    /// A process handle whose release can be suppressed for handles owned by the caller.
    /// </summary>
    internal sealed class ProcessSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
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
            return !CloseOnRelease || PInvoke.CloseHandle((HANDLE)handle);
        }

        /// <summary>
        /// Gets or sets a value indicating whether the native handle is closed when this instance is released.
        /// </summary>
        public bool CloseOnRelease { get; set; } = true;
    }
}
