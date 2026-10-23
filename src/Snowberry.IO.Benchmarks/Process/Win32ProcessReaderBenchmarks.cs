using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Text;
using Microsoft.Win32.SafeHandles;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Common;
using Snowberry.IO.Extensions;
using Snowberry.IO.Reader;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Threading;

namespace Snowberry.IO.Benchmarks.Process;

/// <summary>
/// Measures reading process memory with <see cref="Win32ProcessReader"/> against calling <c>ReadProcessMemory</c> directly, in the shape
/// the library declares it and in a blittable shape, and against reading the same bytes within the process.
/// </summary>
/// <remarks>
/// <para>The memory holds a 64 KiB block of random bytes, 1 MiB of code-like bytes with a signature, null-terminated strings with
/// pointers to them, and a length-prefixed string. <see cref="ProcessTarget.Self"/> reads a pinned array of the benchmark process;
/// <see cref="ProcessTarget.Child"/> reads a copy that is written into a <c>cmd.exe</c> child process, which waits on its redirected
/// standard input, exits when the benchmark process does and is killed in the global cleanup.</para>
/// <para>The baselines of the string reads know the byte length of the string, so they are a lower bound that needs no terminator search.</para>
/// <para>Runs on Windows only; on other operating systems the benchmarks are skipped.</para>
/// </remarks>
[OperatingSystemsFilter(allowed: true, OS.Windows)]
#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows6.0.6000")]
#endif
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public unsafe class Win32ProcessReaderBenchmarks
{
    private const int c_BlockSize = 64 * 1024;
    private const int c_BufferSize = c_ValueCount * sizeof(int);
    private const int c_LongStringLength = 4096;
    private const int c_PointerOffset = c_ScanOffset + c_ScanSize;

    private const int c_ScanOffset = c_BlockSize;
    private const int c_ScanSize = 1024 * 1024;
    private const int c_ShortStringLength = 16;
    private const int c_StringsOffset = c_PointerOffset + 64;
    private const int c_ValueCount = 1024;

    private static readonly SignaturePattern s_Pattern = new("48 8B 05 ?? ?? ?? ?? 59");

    private readonly byte[] _destination = new byte[c_BlockSize];
    private readonly byte[] _scanBuffer = new byte[c_ScanSize];
    private long _baseAddress;
    private System.Diagnostics.Process? _child;
    private SafeFileHandle _handle = null!;

    private byte[] _image = [];
    private GCHandle _imageHandle;
    private string _longString = string.Empty;
    private int _longStringByteLength;
    private int _prefixedOffset;
    private HANDLE _rawHandle;
    private Win32ProcessReader _reader = null!;
    private string _shortString = string.Empty;
    private int _shortStringByteLength;
    private byte[] _stringBuffer = [];

    private static int Align(int offset)
    {
        return (offset + 15) & ~15;
    }

    private static System.Diagnostics.Process StartChild()
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        return System.Diagnostics.Process.Start(startInfo) ?? throw new InvalidOperationException("The child process could not be started.");
    }

    /// <summary>
    /// Closes the reader and the handle, and unpins the array or kills the child process.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _reader?.Dispose();
        _handle?.Dispose();

        if (_imageHandle.IsAllocated)
            _imageHandle.Free();

        if (_child != null)
        {
            try
            {
                if (!_child.HasExited)
                    _child.Kill();

                _ = _child.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // The child process exited on its own.
            }

            _child.Dispose();
            _child = null;
        }
    }

    /// <summary>
    /// Copies the 64 KiB block within the benchmark process with <see cref="Buffer.BlockCopy"/>.
    /// </summary>
    /// <returns>The first byte of the copy.</returns>
    [Benchmark]
    [BenchmarkCategory("Block")]
    public byte CopyBlock()
    {
        Buffer.BlockCopy(_image, 0, _destination, 0, c_BlockSize);
        return _destination[0];
    }

    /// <summary>
    /// Reads 32-bit integers from the array within the benchmark process with <see cref="BinaryPrimitives.ReadInt32LittleEndian(ReadOnlySpan{byte})"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long CopyInt32()
    {
        ReadOnlySpan<byte> source = _image;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += BinaryPrimitives.ReadInt32LittleEndian(source[(i * sizeof(int))..]);

        return sum;
    }

    /// <summary>
    /// Reads the 64 KiB block with <see cref="BaseEndianReader.ReadExactly(byte[], int, int)"/>.
    /// </summary>
    /// <returns>The first byte of the copy.</returns>
    [Benchmark]
    [BenchmarkCategory("Block")]
    public byte ReadBlock()
    {
        _reader.Position = _baseAddress;
        _reader.ReadExactly(_destination, 0, c_BlockSize);
        return _destination[0];
    }

    /// <summary>
    /// Reads the pointer to a null-terminated string and the string with <see cref="Win32ProcessReader.ReadCString"/>.
    /// </summary>
    /// <param name="length">The number of characters of the string: 16 or 4096.</param>
    /// <returns>The read string.</returns>
    [Benchmark]
    [BenchmarkCategory("CString")]
    [Arguments(c_ShortStringLength)]
    [Arguments(c_LongStringLength)]
    public string ReadCString(int length)
    {
        _reader.Position = GetPointerAddress(length);
        return _reader.ReadCString();
    }

    /// <summary>
    /// Reads 4 KiB with <see cref="BaseEndianReader.ReadInInternalBuffer(int, int)"/> and converts 32-bit integers from it with
    /// <see cref="ReaderExtensions.ReadInt32At"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("InternalBuffer")]
    public long ReadInInternalBuffer()
    {
        _reader.Position = _baseAddress;
        _ = _reader.ReadInInternalBuffer(c_BufferSize, 0);
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt32At(EndianType.LITTLE, i * sizeof(int));

        return sum;
    }

    /// <summary>
    /// Reads 32-bit integers with the inherited <see cref="BinaryReader.ReadInt32"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long ReadInt32()
    {
        _reader.Position = _baseAddress;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt32();

        return sum;
    }

    /// <summary>
    /// Reads 32-bit integers with <see cref="BaseEndianReader.ReadInt32(EndianType)"/> in little-endian byte order.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long ReadInt32Endian()
    {
        _reader.Position = _baseAddress;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += _reader.ReadInt32(EndianType.LITTLE);

        return sum;
    }

    /// <summary>
    /// Reads a length-prefixed 4096-character string with the inherited <see cref="BinaryReader.ReadString"/>.
    /// </summary>
    /// <returns>The read string.</returns>
    [Benchmark]
    [BenchmarkCategory("String")]
    public string ReadString()
    {
        _reader.Position = _baseAddress + _prefixedOffset;
        return _reader.ReadString();
    }

    /// <summary>
    /// Reads the 64 KiB block with one blittable <c>ReadProcessMemory</c> call.
    /// </summary>
    /// <returns>The first byte of the copy.</returns>
    [Benchmark]
    [BenchmarkCategory("Block")]
    public byte RpmBlittableBlock()
    {
        nuint read;

        fixed (byte* destination = _destination)
            _ = PInvoke.ReadProcessMemory(_rawHandle, (void*)_baseAddress, destination, c_BlockSize, &read);

        return _destination[0];
    }

    /// <summary>
    /// Reads 32-bit integers with one blittable <c>ReadProcessMemory</c> call each.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long RpmBlittableInt32()
    {
        long sum = 0;
        int value = 0;
        nuint read;

        for (int i = 0; i < c_ValueCount; i++)
        {
            _ = PInvoke.ReadProcessMemory(_rawHandle, (void*)(_baseAddress + (i * sizeof(int))), (byte*)&value, sizeof(int), &read);
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Reads the 64 KiB block with one <c>ReadProcessMemory</c> call in the shape the library declares it.
    /// </summary>
    /// <returns>The first byte of the copy.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Block")]
    public byte RpmBlock()
    {
        fixed (byte* destination = _destination)
            _ = PInvoke.ReadProcessMemory(_handle, (void*)_baseAddress, new Span<byte>(destination, c_BlockSize), out _);

        return _destination[0];
    }

    /// <summary>
    /// Reads 4 KiB with one <c>ReadProcessMemory</c> call in the shape the library declares it and converts 32-bit integers from it with
    /// <see cref="BinaryPrimitives.ReadInt32LittleEndian(ReadOnlySpan{byte})"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("InternalBuffer")]
    public long RpmBufferInt32()
    {
        fixed (byte* destination = _destination)
            _ = PInvoke.ReadProcessMemory(_handle, (void*)_baseAddress, new Span<byte>(destination, c_BufferSize), out _);

        ReadOnlySpan<byte> buffer = _destination;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
            sum += BinaryPrimitives.ReadInt32LittleEndian(buffer[(i * sizeof(int))..]);

        return sum;
    }

    /// <summary>
    /// Reads the pointer to a null-terminated string and the string with two <c>ReadProcessMemory</c> calls in the shape the library declares
    /// it, and decodes it with <see cref="Encoding.GetString(byte[], int, int)"/>.
    /// </summary>
    /// <param name="length">The number of characters of the string: 16 or 4096.</param>
    /// <returns>The read string.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("CString")]
    [Arguments(c_ShortStringLength)]
    [Arguments(c_LongStringLength)]
    public string RpmCString(int length)
    {
        int byteLength = length == c_ShortStringLength ? _shortStringByteLength : _longStringByteLength;
        long pointer = 0;

        _ = PInvoke.ReadProcessMemory(_handle, (void*)GetPointerAddress(length), new Span<byte>(&pointer, IntPtr.Size), out _);

        fixed (byte* buffer = _stringBuffer)
            _ = PInvoke.ReadProcessMemory(_handle, (void*)pointer, new Span<byte>(buffer, byteLength + 1), out _);

        return Encoding.UTF8.GetString(_stringBuffer, 0, byteLength);
    }

    /// <summary>
    /// Reads 32-bit integers with one <c>ReadProcessMemory</c> call each, in the shape the library declares it.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long RpmInt32()
    {
        long sum = 0;
        int value = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            _ = PInvoke.ReadProcessMemory(_handle, (void*)(_baseAddress + (i * sizeof(int))), new Span<byte>((byte*)&value, sizeof(int)), out _);
            sum += value;
        }

        return sum;
    }

    /// <summary>
    /// Reads the 1 MiB of code-like bytes with one <c>ReadProcessMemory</c> call in the shape the library declares it and counts the
    /// signature in them with <see cref="SignaturePattern.Count"/>.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Scan")]
    public int RpmScan()
    {
        fixed (byte* buffer = _scanBuffer)
            _ = PInvoke.ReadProcessMemory(_handle, (void*)(_baseAddress + c_ScanOffset), new Span<byte>(buffer, c_ScanSize), out _);

        return s_Pattern.Count(_scanBuffer);
    }

    /// <summary>
    /// Reads the 7-bit encoded length and the bytes of a 4096-character string with two <c>ReadProcessMemory</c> calls in the shape the
    /// library declares it, and decodes it with <see cref="Encoding.GetString(byte[], int, int)"/>.
    /// </summary>
    /// <returns>The read string.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("String")]
    public string RpmString()
    {
        long address = _baseAddress + _prefixedOffset;

        fixed (byte* buffer = _stringBuffer)
        {
            _ = PInvoke.ReadProcessMemory(_handle, (void*)address, new Span<byte>(buffer, 5), out _);

            int byteLength = 0;
            int prefixLength = 0;
            byte value;

            do
            {
                value = _stringBuffer[prefixLength];
                byteLength |= (value & 0x7F) << (7 * prefixLength);
                prefixLength++;
            }
            while (value >= 0x80);

            _ = PInvoke.ReadProcessMemory(_handle, (void*)(address + prefixLength), new Span<byte>(buffer, byteLength), out _);
            return Encoding.UTF8.GetString(_stringBuffer, 0, byteLength);
        }
    }

    /// <summary>
    /// Scans the 1 MiB of code-like bytes for the signature with <see cref="ReaderExtensions.ScanSignatures"/>.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    [BenchmarkCategory("Scan")]
    public int ScanSignatures()
    {
        long start = _baseAddress + c_ScanOffset;
        _reader.Position = start;
        return _reader.ScanSignatures(s_Pattern.Text, int.MaxValue, start + c_ScanSize - s_Pattern.Length).Count;
    }

    /// <summary>
    /// Places the data in the memory of the target process, opens a reader and a handle for it and checks that every method reads the data.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">The operating system is not Windows.</exception>
    /// <exception cref="Win32Exception">The child process could not be prepared or the process could not be opened.</exception>
    /// <exception cref="InvalidOperationException">A method does not read the data.</exception>
    [GlobalSetup]
    public void Setup()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException($"{nameof(Win32ProcessReader)} requires Windows.");

        CreateImage();

        int processId;

        if (Target == ProcessTarget.Self)
        {
            _imageHandle = GCHandle.Alloc(_image, GCHandleType.Pinned);
            _baseAddress = _imageHandle.AddrOfPinnedObject().ToInt64();
            WritePointers();

            using var current = System.Diagnostics.Process.GetCurrentProcess();
            processId = current.Id;
        }
        else
        {
            _child = StartChild();
            processId = _child.Id;

            nint childHandle = _child.Handle;
            _baseAddress = (nint)PInvoke.VirtualAllocEx((HANDLE)childHandle, null, (nuint)_image.Length, VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT | VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE, PAGE_PROTECTION_FLAGS.PAGE_READWRITE);

            if (_baseAddress == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            WritePointers();

            fixed (byte* image = _image)
            {
                nuint written;

                if (!PInvoke.WriteProcessMemory((HANDLE)childHandle, (void*)_baseAddress, image, (nuint)_image.Length, &written) || written != (nuint)_image.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        _reader = new Win32ProcessReader(processId, _baseAddress);
        _reader.EnsureBufferSize(c_BufferSize);
        _handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ | PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);

        if (_handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        _rawHandle = (HANDLE)_handle.DangerousGetHandle();
        Verify();
    }

    /// <summary>
    /// Reads 32-bit integers with <see cref="BaseEndianReader.TryReadInt32(out int)"/>.
    /// </summary>
    /// <returns>The sum of the read values.</returns>
    [Benchmark(OperationsPerInvoke = c_ValueCount)]
    [BenchmarkCategory("Int32")]
    public long TryReadInt32()
    {
        _reader.Position = _baseAddress;
        long sum = 0;

        for (int i = 0; i < c_ValueCount; i++)
        {
            if (_reader.TryReadInt32(out int value))
                sum += value;
        }

        return sum;
    }

    private void CreateImage()
    {
        var encoding = Encoding.UTF8;
        _shortString = BenchmarkData.RandomText(c_ShortStringLength, seed: c_ShortStringLength);
        _longString = BenchmarkData.RandomText(c_LongStringLength, seed: c_LongStringLength);

        byte[] shortBytes = encoding.GetBytes(_shortString);
        byte[] longBytes = encoding.GetBytes(_longString);
        byte[] prefixed;

        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, encoding, leaveOpen: true))
                writer.Write(_longString);

            prefixed = stream.ToArray();
        }

        _shortStringByteLength = shortBytes.Length;
        _longStringByteLength = longBytes.Length;
        _stringBuffer = new byte[Math.Max(longBytes.Length + 1, prefixed.Length)];

        int longOffset = Align(c_StringsOffset + shortBytes.Length + 1);
        _prefixedOffset = Align(longOffset + longBytes.Length + 1);
        _image = new byte[_prefixedOffset + prefixed.Length];

        BenchmarkData.RandomBytes(c_BlockSize).CopyTo(_image, 0);
        byte[] code = BenchmarkData.CodeBytes(c_ScanSize);

        foreach (int offset in new[] { 16, c_ScanSize / 2, c_ScanSize - s_Pattern.Length })
            s_Pattern.WriteTo(code, offset);

        code.CopyTo(_image, c_ScanOffset);
        shortBytes.CopyTo(_image, c_StringsOffset);
        longBytes.CopyTo(_image, longOffset);
        prefixed.CopyTo(_image, _prefixedOffset);
    }

    private long GetPointerAddress(int length)
    {
        return _baseAddress + c_PointerOffset + (length == c_ShortStringLength ? 0 : sizeof(long));
    }

    private void Verify()
    {
        long int32 = CopyInt32();

        if (RpmInt32() != int32 || RpmBlittableInt32() != int32 || ReadInt32() != int32 || ReadInt32Endian() != int32 || TryReadInt32() != int32
            || RpmBufferInt32() != int32 || ReadInInternalBuffer() != int32)
        {
            throw new InvalidOperationException($"A 32-bit integer read did not read the data ({Target}).");
        }

        foreach (var readBlock in new Func<byte>[] { RpmBlock, RpmBlittableBlock, ReadBlock })
        {
            Array.Clear(_destination, 0, _destination.Length);
            _ = readBlock();

            if (!_destination.AsSpan().SequenceEqual(_image.AsSpan(0, c_BlockSize)))
                throw new InvalidOperationException($"A block read did not read the data ({Target}).");
        }

        if (RpmCString(c_ShortStringLength) != _shortString || ReadCString(c_ShortStringLength) != _shortString
            || RpmCString(c_LongStringLength) != _longString || ReadCString(c_LongStringLength) != _longString
            || RpmString() != _longString || ReadString() != _longString)
        {
            throw new InvalidOperationException($"A string read did not read the data ({Target}).");
        }

        int matches = RpmScan();

        if (matches < 3 || ScanSignatures() != matches)
            throw new InvalidOperationException($"The signature scans did not find the same matches ({Target}).");
    }

    private void WritePointer(int offset, long address)
    {
        if (IntPtr.Size == sizeof(long))
            BinaryPrimitives.WriteInt64LittleEndian(_image.AsSpan(offset), address);
        else
            BinaryPrimitives.WriteInt32LittleEndian(_image.AsSpan(offset), (int)address);
    }

    private void WritePointers()
    {
        int longOffset = Align(c_StringsOffset + _shortStringByteLength + 1);
        WritePointer(c_PointerOffset, _baseAddress + c_StringsOffset);
        WritePointer(c_PointerOffset + sizeof(long), _baseAddress + longOffset);
    }

    /// <summary>
    /// Gets or sets the process whose memory is read.
    /// </summary>
    [Params(ProcessTarget.Self, ProcessTarget.Child)]
    public ProcessTarget Target { get; set; }

    /// <summary>
    /// The processes whose memory is read.
    /// </summary>
    public enum ProcessTarget
    {
        /// <summary>
        /// The benchmark process itself.
        /// </summary>
        Self,

        /// <summary>
        /// A <c>cmd.exe</c> child process.
        /// </summary>
        Child
    }
}