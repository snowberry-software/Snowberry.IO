using System.Runtime.Versioning;
using System.Text;
using Snowberry.IO.Common.Reader;
using static Snowberry.IO.Utils.Win32Helper;

namespace Snowberry.IO.Reader;

/// <summary>
/// Used for reading memory from a windows process.
/// </summary>
/// <remarks>String operations may not work (except <see cref="IEndianReader.ReadCString"/>).</remarks>
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public partial class Win32ProcessReader : BaseEndianReader
{
    protected long _position;
    private readonly IntPtr _processHandle;

    /// <summary>
    /// Creates a new reader for the process.
    /// </summary>
    /// <param name="pid">The id of the process.</param>
    /// <param name="startPosition">The default position that will be used for <see cref="Position"/>.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    public Win32ProcessReader(int pid, long startPosition, Analyzer? analyzer = null, uint accessFlags = (uint)ProcessAccess.PROCESS_VM_READ) : base(analyzer)
    {
        _processHandle = OpenProcess(accessFlags, false, pid);
        _position = startPosition;
        analyzer?.Initialize(this);
    }
    /// <summary>
    /// Creates a new reader for the process.
    /// </summary>
    /// <param name="handle">The handle of the process.</param>
    /// <param name="startPosition">The default position that will be used for <see cref="Position"/>.</param>
    /// <param name="analyzer">The optional analyzer instance.</param>
    public Win32ProcessReader(IntPtr handle, long startPosition, Analyzer? analyzer = null) : base(analyzer)
    {
        _processHandle = handle;
        _position = startPosition;
        analyzer?.Initialize(this);
    }

    /// <inheritdoc/>
    protected override int InternalReadBytes(byte[] inBuffer, int offset, int byteCount)
    {
        return InternalReadBytes(inBuffer.AsSpan()[offset..byteCount]);
    }

    /// <inheritdoc/>
    /// <exception cref="NotImplementedException"></exception>
    public override void CopyTo(Stream destination)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    /// <exception cref="NotImplementedException"></exception>
    public override void CopyTo(Stream destination, int length, int bufferSize = 81920)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Inject dynamic library.
    /// </summary>
    /// <param name="filePath">The full path of the dynamic library to inject.</param>
    /// <returns>Whether the dynamic library got successfully injected.</returns>
    public bool InjectDLL(string filePath)
    {
        _ = filePath ?? throw new ArgumentNullException(nameof(filePath));

        filePath = Path.GetFullPath(filePath);

        nint loadLibraryFunction = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");

        if (loadLibraryFunction == IntPtr.Zero)
            return false;

        // https://docs.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualallocex
        const int MEM_COMMIT = 0x00001000;
        const int MEM_RESERVE = 0x00002000;

        // https://docs.microsoft.com/en-us/windows/win32/memory/memory-protection-constants
        const int PAGE_EXECUTE_READWRITE = 0x40;

        // Allocate region to store the DLL name
        nint libNameAddress = VirtualAllocEx(_processHandle, IntPtr.Zero, (uint)filePath.Length, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);

        // Check if bytes were allocated
        if (libNameAddress == IntPtr.Zero)
            return false;

        // Get file path as byte array
        byte[] dllFullPath = Encoding.ASCII.GetBytes(filePath);

        // Write DLL full path
        if (!WriteProcessMemory(_processHandle, libNameAddress, dllFullPath, (uint)dllFullPath.Length, out _))
            return false;

        // Create thread that will call LoadLibraryA with the library name address as argument
        return CreateRemoteThread(_processHandle, IntPtr.Zero, IntPtr.Zero, loadLibraryFunction, libNameAddress, 0, IntPtr.Zero) != IntPtr.Zero;
    }

    /// <inheritdoc />
    public override string ReadCString()
    {
        long stringPointerPos = ReadLong();
        long oldPosition = Position;

        Position = stringPointerPos;

        base.ReadCString();

        Position = oldPosition;
        return _stringBuilder.ToString();
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        GC.SuppressFinalize(this);

        _ = CloseHandle(_processHandle);
    }

    /// <inheritdoc/>
    /// <remarks>Will always return <see langword="0"/>.</remarks>
    public override long Length => 0;

    /// <inheritdoc/>
    public override bool CanReadData => _processHandle != IntPtr.Zero;

    /// <inheritdoc/>
    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    /// <inheritdoc/>
    public override long ActualPosition => _position;

    /// <inheritdoc/>
    /// <remarks>Will always return <see langword="0"/>.</remarks>
    public override long ActualLength => 0;
}
