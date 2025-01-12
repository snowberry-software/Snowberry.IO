using static Snowberry.IO.Utils.Win32Helper;

namespace Snowberry.IO.Reader;

public partial class Win32ProcessReader
{
    /// <inheritdoc/>
    protected override int InternalReadBytes(Span<byte> inBuffer)
    {
        ThrowIfDisposed();

        uint lpflOldProtect = 0u;
        int read = 0;

        int byteCount = inBuffer.Length;

        // Update protection on memory region
        // https://docs.microsoft.com/en-us/windows/win32/memory/memory-protection-constants
        // -> 0x2 -> PAGE_READONLY
        VirtualProtectEx(_processHandle, _position, new((uint)byteCount), 0x2, ref lpflOldProtect);

        // Read into buffer
        byte[] tempBuffer = new byte[byteCount];
        if (!ReadProcessMemory(_processHandle, _position, tempBuffer, byteCount, ref read))
            return 0;

        // Reset memory region protection
        VirtualProtectEx(_processHandle, _position, new((uint)byteCount), lpflOldProtect, ref lpflOldProtect);

        tempBuffer.CopyTo(inBuffer);

        _position += byteCount;
        Analyzer?.AnalyzeReadBytes(this, inBuffer, byteCount);

        return byteCount;
    }
}
