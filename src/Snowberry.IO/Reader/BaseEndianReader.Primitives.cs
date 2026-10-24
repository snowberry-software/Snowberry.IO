using System.Diagnostics;
using System.Runtime.CompilerServices;
using Snowberry.IO.Common;
using Snowberry.IO.Reader.Streams;
#if NET5_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <inheritdoc/>
    public Sha1 ReadSha1()
    {
        return new(ReadScratch(Sha1.c_StructSize));
    }

    /// <inheritdoc/>
    public long ReadInt64(EndianType endian)
    {
        return ReadScratchInt64(endian);
    }

    /// <inheritdoc/>
    public ulong ReadUInt64(EndianType endian)
    {
        return (ulong)ReadScratchInt64(endian);
    }

    /// <inheritdoc/>
    public uint ReadUInt32(EndianType endian)
    {
        return (uint)ReadScratchInt32(endian);
    }

    /// <inheritdoc/>
    public ushort ReadUInt16(EndianType endian)
    {
        return (ushort)ReadScratchInt16(endian);
    }

    /// <inheritdoc/>
    public int ReadInt32(EndianType endian)
    {
        return ReadScratchInt32(endian);
    }

    /// <inheritdoc/>
    public short ReadInt16(EndianType endian)
    {
        return ReadScratchInt16(endian);
    }

    /// <inheritdoc/>
    public Guid ReadGuid()
    {
        return ReadGuid(EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public Guid ReadGuid(EndianType endian)
    {
        return BinaryEndianConverter.ToGuid(ReadScratch(16), 0, endian);
    }

    /// <inheritdoc/>
    public float ReadSingle(EndianType endian)
    {
        return Int32BitsToSingle(ReadScratchInt32(endian));
    }

    /// <inheritdoc/>
    public double ReadDouble(EndianType endian)
    {
        return BitConverter.Int64BitsToDouble(ReadScratchInt64(endian));
    }

#if NET5_0_OR_GREATER
    /// <inheritdoc/>
    public Half ReadHalf(EndianType endian)
    {
        return BitConverter.Int16BitsToHalf(ReadScratchInt16(endian));
    }
#endif

    // The fixed-size reads of BinaryReader share the scratch path of the endian overloads instead of the base implementations, which
    // read through the reader stream even when no analyzer and no region view is active. Like the base implementations, they read
    // little-endian, consume the bytes that are available when the end of the stream is reached and then throw.

    /// <inheritdoc/>
    public override bool ReadBoolean()
    {
        return ReadByteOrThrow() != 0;
    }

    /// <inheritdoc/>
    public override byte ReadByte()
    {
        return (byte)ReadByteOrThrow();
    }

    /// <inheritdoc/>
    public override sbyte ReadSByte()
    {
        return (sbyte)ReadByteOrThrow();
    }

    /// <inheritdoc/>
    public override short ReadInt16()
    {
        return ReadScratchInt16(EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public override ushort ReadUInt16()
    {
        return (ushort)ReadScratchInt16(EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public override int ReadInt32()
    {
        return ReadScratchInt32(EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public override uint ReadUInt32()
    {
        return (uint)ReadScratchInt32(EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public override long ReadInt64()
    {
        return ReadScratchInt64(EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public override ulong ReadUInt64()
    {
        return (ulong)ReadScratchInt64(EndianType.LITTLE);
    }

    /// <inheritdoc/>
    public override float ReadSingle()
    {
        return Int32BitsToSingle(ReadScratchInt32(EndianType.LITTLE));
    }

    /// <inheritdoc/>
    public override double ReadDouble()
    {
        return BitConverter.Int64BitsToDouble(ReadScratchInt64(EndianType.LITTLE));
    }

#if NET5_0_OR_GREATER
    /// <inheritdoc/>
    public override Half ReadHalf()
    {
        return BitConverter.Int16BitsToHalf(ReadScratchInt16(EndianType.LITTLE));
    }
#endif

    /// <summary>
    /// Reads a single byte, like <see cref="ReadByteSafe"/>, but throws at the end of the stream.
    /// </summary>
    /// <returns>The byte that was read.</returns>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadByteOrThrow()
    {
        int value = ReadByteSafe();

        if (value < 0)
            ThrowEndOfStreamException();

        return value;
    }

    /// <summary>
    /// Reads a 16-bit integer in the <paramref name="endian"/> byte order through the scratch buffer.
    /// </summary>
    /// <param name="endian">The byte order of the data.</param>
    /// <returns>The value that was read.</returns>
    /// <exception cref="EndOfStreamException">The end of the stream is reached before 2 bytes are read.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private short ReadScratchInt16(EndianType endian)
    {
        return DecodeInt16(FillScratch(sizeof(short)), endian);
    }

    /// <summary>
    /// Decodes a 16-bit integer in the <paramref name="endian"/> byte order from the start of <paramref name="b"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="b"/> is the scratch buffer, which always holds the value, so on .NET 5 and later the value is read from a span of
    /// constant length over the array data, without null and length checks.
    /// </remarks>
    /// <param name="b">The buffer that holds the bytes, at least 2 bytes long.</param>
    /// <param name="endian">The byte order of the data.</param>
    /// <returns>The decoded value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short DecodeInt16(byte[] b, EndianType endian)
    {
        Debug.Assert(b.Length >= sizeof(short));

#if NET5_0_OR_GREATER
        return BinaryEndianConverter.ToInt16(MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(b), sizeof(short)), endian);
#else
        return BinaryEndianConverter.ToInt16(b, endian);
#endif
    }

    /// <summary>
    /// Reads a 32-bit integer in the <paramref name="endian"/> byte order through the scratch buffer.
    /// </summary>
    /// <param name="endian">The byte order of the data.</param>
    /// <returns>The value that was read.</returns>
    /// <exception cref="EndOfStreamException">The end of the stream is reached before 4 bytes are read.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ReadScratchInt32(EndianType endian)
    {
        return DecodeInt32(FillScratch(sizeof(int)), endian);
    }

    /// <summary>
    /// Decodes a 32-bit integer in the <paramref name="endian"/> byte order from the start of <paramref name="b"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="b"/> is the scratch buffer, which always holds the value, so on .NET 5 and later the value is read from a span of
    /// constant length over the array data, without null and length checks.
    /// </remarks>
    /// <param name="b">The buffer that holds the bytes, at least 4 bytes long.</param>
    /// <param name="endian">The byte order of the data.</param>
    /// <returns>The decoded value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeInt32(byte[] b, EndianType endian)
    {
        Debug.Assert(b.Length >= sizeof(int));

#if NET5_0_OR_GREATER
        return BinaryEndianConverter.ToInt32(MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(b), sizeof(int)), endian);
#else
        return BinaryEndianConverter.ToInt32(b, endian);
#endif
    }

    /// <summary>
    /// Reads a 64-bit integer in the <paramref name="endian"/> byte order through the scratch buffer.
    /// </summary>
    /// <param name="endian">The byte order of the data.</param>
    /// <returns>The value that was read.</returns>
    /// <exception cref="EndOfStreamException">The end of the stream is reached before 8 bytes are read.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long ReadScratchInt64(EndianType endian)
    {
        return DecodeInt64(FillScratch(sizeof(long)), endian);
    }

    /// <summary>
    /// Decodes a 64-bit integer in the <paramref name="endian"/> byte order from the start of <paramref name="b"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="b"/> is the scratch buffer, which always holds the value, so on .NET 5 and later the value is read from a span of
    /// constant length over the array data, without null and length checks.
    /// </remarks>
    /// <param name="b">The buffer that holds the bytes, at least 8 bytes long.</param>
    /// <param name="endian">The byte order of the data.</param>
    /// <returns>The decoded value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long DecodeInt64(byte[] b, EndianType endian)
    {
        Debug.Assert(b.Length >= sizeof(long));

#if NET5_0_OR_GREATER
        return BinaryEndianConverter.ToInt64(MemoryMarshal.CreateReadOnlySpan(ref MemoryMarshal.GetArrayDataReference(b), sizeof(long)), endian);
#else
        return BinaryEndianConverter.ToInt64(b, endian);
#endif
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes into the scratch buffer and returns it.
    /// </summary>
    /// <remarks>
    /// The fixed-size reads share this path instead of the inherited <see cref="BinaryReader"/> members and their <c>ReadExactly</c>
    /// loop. Like those members, it consumes the bytes that were available when the end of the stream is reached, and every read is
    /// reported to the <see cref="Analyzer"/>. While neither an <see cref="Analyzer"/> nor a region view is active, the first read goes
    /// straight to <see cref="_direct"/>. The common case of one complete read skips the loop in
    /// <see cref="ReaderStream.FillReported"/>.
    /// </remarks>
    /// <param name="count">The number of bytes to read, at most the size of the scratch buffer.</param>
    /// <returns>The scratch buffer, whose first <paramref name="count"/> bytes were read; valid until the next read.</returns>
    /// <exception cref="EndOfStreamException">The end of the stream is reached before <paramref name="count"/> bytes are read.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[] FillScratch(int count)
    {
        ThrowIfDisposed();

        byte[] buffer = _scratch;
        var direct = _direct;
        int read = direct is null ? _readerStream.ReadUnreported(buffer, 0, count) : direct.Read(buffer, 0, count);

        if (read != count)
            FillScratchRest(buffer, read, count);
        else if (_analyzer is not null)
            ReportScratchRead(buffer, count, count);

        return buffer;
    }

    /// <summary>
    /// Completes <see cref="FillScratch"/> after a first read that returned fewer than <paramref name="count"/> bytes: the bytes of the
    /// first read are reported, and the rest is read through the reporting <see cref="ReaderStream.FillReported"/>.
    /// </summary>
    /// <param name="buffer">The scratch buffer.</param>
    /// <param name="read">The number of bytes the first read returned.</param>
    /// <param name="count">The number of bytes to read in total.</param>
    /// <exception cref="EndOfStreamException">The end of the stream is reached before <paramref name="count"/> bytes are read.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FillScratchRest(byte[] buffer, int read, int count)
    {
        if (_analyzer is not null)
            ReportScratchRead(buffer, count, read);

        _readerStream.FillReported(buffer, read, count);
    }

    /// <summary>
    /// Reports the first read of <see cref="FillScratch"/> to the <see cref="Analyzer"/>, as the reader stream reports its reads.
    /// </summary>
    /// <remarks>
    /// The reader calls the analyzer itself instead of reading through the reporting stream, which saves a call per value. The .NET
    /// Standard 2.0 build keeps the span out of the reads that inline this method, since a portable span makes them zero their
    /// stack frame.
    /// </remarks>
    /// <param name="buffer">The scratch buffer.</param>
    /// <param name="count">The number of bytes that were requested.</param>
    /// <param name="read">The number of bytes that were read.</param>
#if NETSTANDARD2_0
    [MethodImpl(MethodImplOptions.NoInlining)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    private void ReportScratchRead(byte[] buffer, int count, int read)
    {
        _analyzer!.AnalyzeReadBytes(this, buffer.AsSpan(0, count), read);
    }

    /// <summary>
    /// Reinterprets the bits of <paramref name="value"/> as a single-precision floating-point number.
    /// </summary>
    /// <param name="value">The bits to reinterpret.</param>
    /// <returns>The single-precision floating-point number with the bits of <paramref name="value"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe float Int32BitsToSingle(int value)
    {
        return *(float*)&value;
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes into the scratch buffer.
    /// </summary>
    /// <param name="count">The number of bytes to read, at most the size of the scratch buffer.</param>
    /// <returns>The bytes that were read; valid until the next read.</returns>
    /// <exception cref="EndOfStreamException">The end of the stream is reached before <paramref name="count"/> bytes are read.</exception>
    /// <exception cref="ObjectDisposedException">The reader is disposed.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> ReadScratch(int count)
    {
        return new ReadOnlySpan<byte>(FillScratch(count), 0, count);
    }
}