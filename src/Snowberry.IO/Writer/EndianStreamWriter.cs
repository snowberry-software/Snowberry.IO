using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Writer.Interfaces;
using Snowberry.IO.Utils;

namespace Snowberry.IO.Writer;

/// <summary>
/// <see cref="BinaryWriter"/> extended to support different endian types (<see cref="EndianType"/>).
/// </summary>
public class EndianStreamWriter : BinaryWriter, IEndianWriter
{
    /// <summary>
    /// A reusable buffer of zero <see langword="byte"/>s used by <see cref="WritePadding(byte)"/> and <see cref="WriteSizedCString(string, int)"/> to avoid allocating a new buffer for every padding write.
    /// </summary>
    /// <remarks>256 bytes is sufficient for <see cref="WritePadding(byte)"/> since <see langword="byte"/> alignment is always smaller than 256; larger padding amounts are written in chunks.</remarks>
    private static readonly byte[] s_Zeros = new byte[256];

    /// <summary>
    /// The size of <see cref="_scratch"/>, in <see langword="byte"/>s.
    /// </summary>
    private const int c_ScratchSize = 256;

    /// <summary>
    /// The largest number of <see langword="byte"/>s that are passed to the stream in one write through a rented buffer.
    /// </summary>
    /// <remarks>
    /// <see cref="WriteSizedCString(string, int)"/> encodes and pads sizes up to this in one buffer; on .NET Standard 2.0,
    /// <see cref="Write(ReadOnlySpan{byte})"/> copies longer spans in chunks of this size.
    /// </remarks>
    private const int c_MaxSinglePassByteCount = 64 * 1024;

    /// <summary>
    /// The longest text, in <see langword="char"/>s, that is encoded in one pass into a buffer of <see cref="Encoding.GetMaxByteCount(int)"/> <see langword="byte"/>s.
    /// </summary>
    /// <remarks>Longer texts are measured with <see cref="Encoding.GetByteCount(string)"/> first, so that no buffer is rented for the worst case of a large text.</remarks>
    private const int c_MaxSinglePassLength = c_MaxSinglePassByteCount / 3;

#if NETSTANDARD2_0
    /// <summary>
    /// The largest size, in <see langword="byte"/>s, of a 32-bit integer in the 7-bit encoded format.
    /// </summary>
    private const int c_MaxLengthPrefixSize = 5;
#endif

    /// <summary>
    /// The encoding used to write text to the stream.
    /// </summary>
    protected Encoding _encoding;

    /// <summary>
    /// The null terminator encoded with <see cref="_encoding"/>, as wide as a single null character in that encoding, created on the first <see cref="WriteCString(string)"/> call.
    /// </summary>
    private byte[]? _nullTerminator;

    /// <summary>
    /// <see cref="Environment.NewLine"/> encoded with <see cref="_encoding"/>, created on the first <see cref="WriteLine(string)"/> call.
    /// </summary>
    private byte[]? _newLine;

    /// <summary>
    /// A buffer that text and values are encoded into before they are passed to the stream in one write, created on first use.
    /// </summary>
    private byte[]? _scratch;

    /// <summary>
    /// Creates a new <see cref="EndianStreamWriter" /> instance.
    /// </summary>
    /// <remarks>
    /// Uses UTF-8 without a byte order mark as encoding on every target framework, for both the <see cref="IEndianWriter"/>
    /// string methods and the inherited <see cref="BinaryWriter"/> methods such as <see cref="BinaryWriter.Write(string)"/>.
    /// </remarks>
    /// <param name="stream">The target stream.</param>
    /// <param name="keepStreamOpen">Keep the stream open after using the writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> is not writable.</exception>
    public EndianStreamWriter(Stream stream, bool keepStreamOpen) : this(stream, keepStreamOpen, EncodingDefaults.s_Utf8)
    {
    }

    /// <summary>
    /// Creates a new <see cref="EndianStreamWriter" /> instance with different options.
    /// </summary>
    /// <param name="stream">The target stream.</param>
    /// <param name="keepStreamOpen">Keep the stream open after using the writer.</param>
    /// <param name="encoding">The encoding to use.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream"/> or <paramref name="encoding"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="stream"/> is not writable.</exception>
    public EndianStreamWriter(Stream stream, bool keepStreamOpen, Encoding encoding) : base(stream, encoding, keepStreamOpen)
    {
        _encoding = encoding;
    }

    /// <inheritdoc/>
    public IEndianWriter Write(Guid value, EndianType endian = EndianType.LITTLE)
    {
#if NET8_0_OR_GREATER
        Span<byte> data = stackalloc byte[16];
        value.TryWriteBytes(data, endian == EndianType.BIG, out _);
        OutStream.Write(data);
        return this;
#elif NETSTANDARD2_0
        WriteGuid(value, endian);
        return this;
#else
        Span<byte> data = stackalloc byte[16];
        value.TryWriteBytes(data);

        if (endian == EndianType.BIG)
            ReverseGuidByteOrder(data);

        OutStream.Write(data);
        return this;
#endif
    }

#if NETSTANDARD2_0
    /// <summary>
    /// Writes <paramref name="value"/> through <see cref="Scratch"/> in one write, in the layout of <see cref="Guid.ToByteArray()"/> or,
    /// for <see cref="EndianType.BIG"/>, with the first three fields in big-endian order.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="endian">The <see langword="byte"/> order of the first three fields.</param>
    private unsafe void WriteGuid(Guid value, EndianType endian)
    {
        // A Guid holds Data1 (int), Data2 and Data3 (short) followed by the 8 bytes of Data4. Reading the first three fields
        // as integers and shifting their bytes out gives the same result on little-endian and big-endian hosts.
        byte* guid = (byte*)&value;
        int a = *(int*)guid;
        short b = *(short*)(guid + 4);
        short c = *(short*)(guid + 6);
        byte[] data = Scratch;

        if (endian == EndianType.BIG)
        {
            data[0] = (byte)(a >> 24);
            data[1] = (byte)(a >> 16);
            data[2] = (byte)(a >> 8);
            data[3] = (byte)a;
            data[4] = (byte)(b >> 8);
            data[5] = (byte)b;
            data[6] = (byte)(c >> 8);
            data[7] = (byte)c;
        }
        else
        {
            data[0] = (byte)a;
            data[1] = (byte)(a >> 8);
            data[2] = (byte)(a >> 16);
            data[3] = (byte)(a >> 24);
            data[4] = (byte)b;
            data[5] = (byte)(b >> 8);
            data[6] = (byte)c;
            data[7] = (byte)(c >> 8);
        }

        for (int i = 8; i < 16; i++)
            data[i] = guid[i];

        OutStream.Write(data, 0, 16);
    }
#else
    /// <summary>
    /// Reorders the first 8 bytes of a <see cref="Guid"/>'s standard (little-endian) byte layout, produced by <see cref="Guid.ToByteArray()"/> or <c>Guid.TryWriteBytes(Span&lt;byte&gt;)</c>, into big-endian / RFC 4122 network byte order.
    /// </summary>
    /// <remarks>The last 8 bytes (<c>Data4</c>) are already order-invariant and are left untouched.</remarks>
    /// <param name="data">The 16-byte buffer containing the <see cref="Guid"/>'s standard byte layout.</param>
    private static void ReverseGuidByteOrder(Span<byte> data)
    {
        (data[0], data[3]) = (data[3], data[0]);
        (data[1], data[2]) = (data[2], data[1]);
        (data[4], data[5]) = (data[5], data[4]);
        (data[6], data[7]) = (data[7], data[6]);
    }
#endif

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(double value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            Write(BitConverter.DoubleToInt64Bits(value), EndianType.BIG);
            return this;
        }

        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(float value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
#if NETSTANDARD2_0
            Write(SingleToInt32Bits(value), EndianType.BIG);
#else
            Write(BitConverter.SingleToInt32Bits(value), EndianType.BIG);
#endif
            return this;
        }

        base.Write(value);
        return this;
    }

#if NET5_0_OR_GREATER
    // Half converts implicitly from byte and sbyte. The lower priority keeps a Write(byte, EndianType) call bound to
    // Write(short, EndianType) instead of making it ambiguous with this overload.

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if NET9_0_OR_GREATER
    [OverloadResolutionPriority(-1)]
#endif
    public IEndianWriter Write(Half value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(BitConverter.HalfToInt16Bits(value)));
            return this;
        }

        base.Write(value);
        return this;
    }
#endif

#if NETSTANDARD2_0
    /// <summary>
    /// Converts the specified single-precision floating-point number to a 32-bit signed integer
    /// by reinterpreting its bit pattern.
    /// </summary>
    /// <param name="value">The single-precision floating-point number to convert.</param>
    /// <returns>A 32-bit signed integer with the same bit representation as <paramref name="value"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe int SingleToInt32Bits(float value)
    {
        return *(int*)&value;
    }
#endif

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(ulong value, EndianType endian = EndianType.LITTLE)
    {
        Write((long)value, endian);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(int value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(value));
            return this;
        }

        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(uint value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(value));
            return this;
        }

        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(long value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(value));
            return this;
        }

        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(short value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(value));
            return this;
        }

        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(ushort value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            base.Write(BinaryPrimitives.ReverseEndianness(value));
            return this;
        }

        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(Sha1 value)
    {
#if NETSTANDARD2_0
        byte[] data = Scratch;
        value.TryWriteBytes(data);

        OutStream.Write(data, 0, Sha1.c_StructSize);
#else
        Span<byte> data = stackalloc byte[Sha1.c_StructSize];
        value.TryWriteBytes(data);

        OutStream.Write(data);
#endif
        return this;
    }

    /// <inheritdoc/>
    public IEndianWriter WriteSizedCString(string text, int size)
    {
        _ = text ?? throw new ArgumentNullException(nameof(text));

        if (size < text.Length)
            throw new ArgumentOutOfRangeException(nameof(size), size, "The size must be greater than or equal to the text length.");

        if (size <= c_MaxSinglePassByteCount)
        {
#if NET8_0_OR_GREATER
            int bufferSize = size;
#else
            int bufferSize = Math.Max(_encoding.GetMaxByteCount(text.Length), size);
#endif

            if (bufferSize <= c_ScratchSize)
            {
                WriteSizedText(text, size, Scratch);
                return this;
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(bufferSize);

            try
            {
                WriteSizedText(text, size, rented);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }

            return this;
        }

#if NETSTANDARD2_0
        int byteCount = _encoding.GetByteCount(text);
#else
        int byteCount = _encoding.GetByteCount(text.AsSpan());
#endif

        if (byteCount > size)
            ThrowEncodedTextLongerThanSize(byteCount, size);

        WriteEncodedText(text, byteCount);

        int missingLength = size - byteCount;

        // NOTE(VNC): There is nothing left that has to be padded.
        if (missingLength < 1)
            return this;

        WriteZeroBytes(missingLength);
        return this;
    }

    /// <summary>
    /// Encodes <paramref name="text"/> into <paramref name="buffer"/>, fills the rest of the <paramref name="size"/> <see langword="byte"/>s with zeros
    /// and writes them to the stream in one write.
    /// </summary>
    /// <param name="text">The text to encode and write.</param>
    /// <param name="size">The number of <see langword="byte"/>s to write.</param>
    /// <param name="buffer">
    /// A buffer of at least <paramref name="size"/> <see langword="byte"/>s; before .NET 8, also of at least <see cref="Encoding.GetMaxByteCount(int)"/>
    /// <see langword="byte"/>s for <paramref name="text"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="text"/> encodes to more than <paramref name="size"/> <see langword="byte"/>s; nothing is written then.</exception>
    private void WriteSizedText(string text, int size, byte[] buffer)
    {
#if NET8_0_OR_GREATER
        if (!_encoding.TryGetBytes(text, buffer.AsSpan(0, size), out int byteCount))
            ThrowEncodedTextLongerThanSize(_encoding.GetByteCount(text), size);
#else
        int byteCount = _encoding.GetBytes(text, 0, text.Length, buffer, 0);

        if (byteCount > size)
            ThrowEncodedTextLongerThanSize(byteCount, size);
#endif

        Array.Clear(buffer, byteCount, size - byteCount);

        if (size > 0)
            OutStream.Write(buffer, 0, size);
    }

    /// <summary>
    /// Throws the <see cref="ArgumentOutOfRangeException"/> of <see cref="WriteSizedCString(string, int)"/> for a text that encodes to more than the size.
    /// </summary>
    /// <param name="byteCount">The number of <see langword="byte"/>s that the text encodes to.</param>
    /// <param name="size">The size passed to <see cref="WriteSizedCString(string, int)"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowEncodedTextLongerThanSize(int byteCount, int size)
    {
        throw new ArgumentOutOfRangeException("text", $"The byte count of the text `{byteCount}` is greater than the specified size `{size}`.\nCheck the data of the text or the encoding that is used.");
    }

    /// <inheritdoc/>
    public IEndianWriter WriteLine(string text)
    {
        WriteText(text ?? ThrowTextIsNull(), NewLine);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter WritePadding(byte alignment)
    {
        long padding = BinaryUtils.CalculatePadding(OutStream.Position, alignment);
        WriteZeroBytes((int)padding);

        return this;
    }

    /// <summary>
    /// Writes <paramref name="count"/> zero <see langword="byte"/>s to the stream, reusing <see cref="s_Zeros"/> instead of allocating a new buffer.
    /// </summary>
    /// <param name="count">The number of zero <see langword="byte"/>s to write.</param>
    private void WriteZeroBytes(int count)
    {
        while (count > 0)
        {
            int chunk = Math.Min(count, s_Zeros.Length);

            OutStream.Write(s_Zeros, 0, chunk);
            count -= chunk;
        }
    }

    /// <summary>
    /// Encodes <paramref name="text"/> using <see cref="Encoding"/> into a buffer rented from <see cref="ArrayPool{T}.Shared"/> and writes the encoded bytes to the stream.
    /// </summary>
    /// <param name="text">The text to encode and write.</param>
    /// <param name="byteCount">The number of bytes that <paramref name="text"/> encodes to, as returned by <see cref="Encoding.GetByteCount(string)"/>.</param>
    private void WriteEncodedText(string text, int byteCount)
    {
        if (byteCount == 0)
            return;

        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            int written = _encoding.GetBytes(text, 0, text.Length, rented, 0);

            OutStream.Write(rented, 0, written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Encodes <paramref name="text"/> using <see cref="Encoding"/>, appends <paramref name="suffix"/> and writes both to the stream in one write.
    /// </summary>
    /// <remarks>
    /// The text is encoded in one pass into <see cref="_scratch"/> or a buffer rented from <see cref="ArrayPool{T}.Shared"/> that holds
    /// <see cref="Encoding.GetMaxByteCount(int)"/> <see langword="byte"/>s. Texts longer than <see cref="c_MaxSinglePassLength"/> are measured
    /// first and written separately from <paramref name="suffix"/>. Nothing is written when the text and the suffix encode to no <see langword="byte"/>s.
    /// </remarks>
    /// <param name="text">The text to encode and write.</param>
    /// <param name="suffix">The <see langword="byte"/>s written after the encoded text, or <see langword="null"/> for none.</param>
    private void WriteText(string text, byte[]? suffix)
    {
        if (text.Length > c_MaxSinglePassLength)
        {
#if NETSTANDARD2_0
            WriteEncodedText(text, _encoding.GetByteCount(text));
#else
            WriteEncodedText(text, _encoding.GetByteCount(text.AsSpan()));
#endif

            if (suffix is not null)
                OutStream.Write(suffix, 0, suffix.Length);

            return;
        }

        int maxByteCount = _encoding.GetMaxByteCount(text.Length) + (suffix is null ? 0 : suffix.Length);

        if (maxByteCount <= c_ScratchSize)
        {
            WriteText(text, suffix, Scratch);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(maxByteCount);

        try
        {
            WriteText(text, suffix, rented);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Encodes <paramref name="text"/> into <paramref name="buffer"/>, appends <paramref name="suffix"/> and writes both to the stream in one write.
    /// </summary>
    /// <param name="text">The text to encode and write.</param>
    /// <param name="suffix">The <see langword="byte"/>s written after the encoded text, or <see langword="null"/> for none.</param>
    /// <param name="buffer">A buffer of at least <see cref="Encoding.GetMaxByteCount(int)"/> <see langword="byte"/>s for <paramref name="text"/> plus the length of <paramref name="suffix"/>.</param>
    private void WriteText(string text, byte[]? suffix, byte[] buffer)
    {
        int byteCount = _encoding.GetBytes(text, 0, text.Length, buffer, 0);

        if (suffix is not null)
        {
            for (int i = 0; i < suffix.Length; i++)
                buffer[byteCount + i] = suffix[i];

            byteCount += suffix.Length;
        }

        if (byteCount > 0)
            OutStream.Write(buffer, 0, byteCount);
    }

    /// <inheritdoc/>
    public IEndianWriter WriteStringCharacters(string text)
    {
        WriteText(text ?? ThrowTextIsNull(), null);
        return this;
    }

    /// <inheritdoc/>
    public IEndianWriter WriteCString(string text)
    {
        WriteText(text ?? ThrowTextIsNull(), NullTerminator);
        return this;
    }

    /// <summary>
    /// Throws the <see cref="ArgumentNullException"/> for a <see langword="null"/> <c>text</c> argument.
    /// </summary>
    /// <remarks>
    /// Keeps the parameter name literal and the <see langword="throw"/> out of the small text methods, so that .NET Framework can inline them
    /// into callers in other assemblies.
    /// </remarks>
    /// <returns>Never returns; the return type lets the call stand on the right of <c>??</c>.</returns>
    /// <exception cref="ArgumentNullException">Always thrown.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string ThrowTextIsNull()
    {
        throw new ArgumentNullException("text");
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEndianWriter IEndianWriter.Write(string value)
    {
#if NETSTANDARD2_0
        WriteLengthPrefixedString(value);
#else
        base.Write(value);
#endif
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEndianWriter IEndianWriter.Write(byte value)
    {
        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEndianWriter IEndianWriter.Write(sbyte value)
    {
        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    IEndianWriter IEndianWriter.Write7BitEncodedInt(int value)
    {
        Write7BitEncodedInt(value);
        return this;
    }

    /// <inheritdoc/>
    IEndianWriter IEndianWriter.Write7BitEncodedInt64(long value)
    {
        Write7BitEncodedInt64(value);
        return this;
    }

    /// <inheritdoc/>
    IEndianWriter IEndianWriter.Write(byte[] buffer)
    {
        base.Write(buffer);
        return this;
    }

    /// <inheritdoc/>
    IEndianWriter IEndianWriter.Write(bool value)
    {
        base.Write(value);
        return this;
    }

    // NOTE(VNC):
    //
    // C# overload resolution prefers any applicable method declared on the derived type over the base type's methods.
    // Without these forwarders, Write(byte), Write(sbyte) and Write(char) bind to the endian-aware Write(short/ushort, EndianType)
    // overloads when called on an EndianStreamWriter, because their arguments convert implicitly. Every other inherited
    // BinaryWriter.Write overload has no implicit conversion to a derived overload and binds to BinaryWriter as expected.
    //

    /// <inheritdoc cref="BinaryWriter.Write(byte)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Write(byte value)
    {
        base.Write(value);
    }

    /// <inheritdoc cref="BinaryWriter.Write(sbyte)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Write(sbyte value)
    {
        base.Write(value);
    }

    /// <inheritdoc cref="BinaryWriter.Write(char)"/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Write(char ch)
    {
        base.Write(ch);
    }

#if NETSTANDARD2_0
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Write7BitEncodedInt(int value)
    {
        Write7BitEncodedUInt64((uint)value);
    }

    /// <summary>
    /// Writes a 64-bit signed integer in a compressed 7-bit encoded format, starting with the least significant bits.
    /// </summary>
    /// <param name="value">The value to write.</param>
    private void Write7BitEncodedInt64(long value)
    {
        Write7BitEncodedUInt64((ulong)value);
    }

    /// <summary>
    /// Writes <paramref name="value"/> 7 bits at a time, starting with the least significant bits, with one <see cref="Stream.WriteByte(byte)"/> call
    /// for values up to <c>0x7F</c> and otherwise one write of all encoded <see langword="byte"/>s through <see cref="Scratch"/>.
    /// </summary>
    /// <param name="value">The value to write.</param>
    private void Write7BitEncodedUInt64(ulong value)
    {
        // Licensed to the .NET Foundation under one or more agreements.
        // The .NET Foundation licenses this file to you under the MIT license.
        //
        // https://github.com/dotnet/corert/blob/master/src/System.Private.CoreLib/shared/System/IO/BinaryWriter.cs
        //

        if (value <= 0x7Fu)
        {
            OutStream.WriteByte((byte)value);
            return;
        }

        byte[] buffer = Scratch;
        int count = 0;

        // The high bit of each byte, when on, tells the reader to continue reading more bytes.
        while (value > 0x7Fu)
        {
            buffer[count++] = (byte)((uint)value | ~0x7Fu);
            value >>= 7;
        }

        buffer[count++] = (byte)value;
        OutStream.Write(buffer, 0, count);
    }
#elif NETSTANDARD2_0_OR_GREATER
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public new void Write7BitEncodedInt(int value)
    {
        base.Write7BitEncodedInt(value);
    }

    private void Write7BitEncodedInt64(long value)
    {
        // Licensed to the .NET Foundation under one or more agreements.
        // The .NET Foundation licenses this file to you under the MIT license.
        //
        // https://github.com/dotnet/corert/blob/master/src/System.Private.CoreLib/shared/System/IO/BinaryWriter.cs
        //

        ulong uValue = (ulong)value;

        // Write out an int 7 bits at a time. The high bit of the byte,
        // when on, tells reader to continue reading more bytes.
        //
        // Using the constants 0x7F and ~0x7F below offers smaller
        // codegen than using the constant 0x80.

        while (uValue > 0x7Fu)
        {
            Write((byte)((uint)uValue | ~0x7Fu));
            uValue >>= 7;
        }

        Write((byte)uValue);
    }
#endif

#if NETSTANDARD2_0
    // The span overload below is declared on EndianStreamWriter, so C# overload resolution would bind Write(byte[]) calls to it
    // through the implicit array to span conversion. The Write(byte[]) forwarder keeps them on BinaryWriter.Write(byte[]).
    // Write(byte[], int, int) needs no forwarder, because no overload declared here takes three arguments.

    /// <inheritdoc cref="BinaryWriter.Write(byte[])"/>
    public new void Write(byte[] buffer)
    {
        // The cast keeps the call virtual, so that an override of BinaryWriter.Write(byte[]) still runs.
        ((BinaryWriter)this).Write(buffer);
    }

    /// <summary>
    /// Writes a span of <see langword="byte"/>s to the current stream and advances the current position by the length of the span.
    /// </summary>
    /// <remarks>
    /// Streams have no span overload on .NET Standard 2.0, so the <see langword="byte"/>s are copied into a buffer and passed to the stream in
    /// one write per 64 KiB.
    /// </remarks>
    /// <param name="buffer">The span of <see langword="byte"/>s to write.</param>
    public void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length <= c_ScratchSize)
        {
            byte[] scratch = Scratch;
            buffer.CopyTo(scratch);

            OutStream.Write(scratch, 0, buffer.Length);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length, c_MaxSinglePassByteCount));

        try
        {
            while (buffer.Length > 0)
            {
                int chunk = Math.Min(buffer.Length, rented.Length);
                buffer[..chunk].CopyTo(rented);

                OutStream.Write(rented, 0, chunk);
                buffer = buffer[chunk..];
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
#else
    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        OutStream.Write(buffer);
    }
#endif

#if NETSTANDARD2_0
    /// <inheritdoc/>
    public override void Write(string value)
    {
        WriteLengthPrefixedString(value);
    }

    /// <summary>
    /// Writes <paramref name="value"/> like <see cref="BinaryWriter.Write(string)"/>, prefixed with its encoded length as a 7-bit encoded integer.
    /// </summary>
    /// <remarks>
    /// The text is encoded once, into <see cref="_scratch"/> or a buffer rented from <see cref="ArrayPool{T}.Shared"/>, and the length prefix is placed
    /// right in front of it, so that both are passed to the stream in one write. Texts longer than <see cref="c_MaxSinglePassLength"/> are written by
    /// <see cref="BinaryWriter.Write(string)"/>.
    /// </remarks>
    /// <param name="value">The text to write.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value"/> is <see langword="null"/>.</exception>
    private void WriteLengthPrefixedString(string value)
    {
        _ = value ?? throw new ArgumentNullException(nameof(value));

        if (value.Length > c_MaxSinglePassLength)
        {
            base.Write(value);
            return;
        }

        int maxByteCount = c_MaxLengthPrefixSize + _encoding.GetMaxByteCount(value.Length);

        if (maxByteCount <= c_ScratchSize)
        {
            WriteLengthPrefixedString(value, Scratch);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(maxByteCount);

        try
        {
            WriteLengthPrefixedString(value, rented);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Encodes <paramref name="value"/> into <paramref name="buffer"/> after <see cref="c_MaxLengthPrefixSize"/> <see langword="byte"/>s, writes the
    /// 7-bit encoded length right in front of the encoded text and passes both to the stream in one write.
    /// </summary>
    /// <param name="value">The text to write.</param>
    /// <param name="buffer">A buffer of at least <see cref="c_MaxLengthPrefixSize"/> plus <see cref="Encoding.GetMaxByteCount(int)"/> <see langword="byte"/>s for <paramref name="value"/>.</param>
    private void WriteLengthPrefixedString(string value, byte[] buffer)
    {
        int byteCount = _encoding.GetBytes(value, 0, value.Length, buffer, c_MaxLengthPrefixSize);

        int prefixSize = 1;

        for (uint remaining = (uint)byteCount; remaining > 0x7Fu; remaining >>= 7)
            prefixSize++;

        int start = c_MaxLengthPrefixSize - prefixSize;
        uint length = (uint)byteCount;

        for (int i = start; i < c_MaxLengthPrefixSize - 1; i++)
        {
            buffer[i] = (byte)(length | ~0x7Fu);
            length >>= 7;
        }

        buffer[c_MaxLengthPrefixSize - 1] = (byte)length;

        OutStream.Write(buffer, start, prefixSize + byteCount);
    }

    /// <inheritdoc/>
    public override void Write(char[] chars)
    {
        // BinaryWriter throws for a null array and writes long arrays without the worst-case buffer.
        if (chars is null || chars.Length > c_MaxSinglePassLength)
        {
            base.Write(chars);
            return;
        }

        WriteChars(chars, 0, chars.Length);
    }

    /// <inheritdoc/>
    public override void Write(char[] chars, int index, int count)
    {
        // BinaryWriter throws for invalid arguments and writes long ranges without the worst-case buffer.
        if (chars is null || index < 0 || count < 0 || chars.Length - index < count || count > c_MaxSinglePassLength)
        {
            base.Write(chars, index, count);
            return;
        }

        WriteChars(chars, index, count);
    }

    /// <summary>
    /// Encodes <paramref name="count"/> characters of <paramref name="chars"/> from <paramref name="index"/> into <see cref="_scratch"/> or a buffer
    /// rented from <see cref="ArrayPool{T}.Shared"/> and passes the encoded <see langword="byte"/>s to the stream in one write.
    /// </summary>
    /// <param name="chars">The characters to encode and write.</param>
    /// <param name="index">The index of the first character to write.</param>
    /// <param name="count">The number of characters to write, at most <see cref="c_MaxSinglePassLength"/>.</param>
    private void WriteChars(char[] chars, int index, int count)
    {
        int maxByteCount = _encoding.GetMaxByteCount(count);

        if (maxByteCount <= c_ScratchSize)
        {
            WriteChars(chars, index, count, Scratch);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(maxByteCount);

        try
        {
            WriteChars(chars, index, count, rented);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Encodes <paramref name="count"/> characters of <paramref name="chars"/> from <paramref name="index"/> into <paramref name="buffer"/> and passes
    /// the encoded <see langword="byte"/>s to the stream in one write.
    /// </summary>
    /// <param name="chars">The characters to encode and write.</param>
    /// <param name="index">The index of the first character to write.</param>
    /// <param name="count">The number of characters to write.</param>
    /// <param name="buffer">A buffer of at least <see cref="Encoding.GetMaxByteCount(int)"/> <see langword="byte"/>s for <paramref name="count"/>.</param>
    private void WriteChars(char[] chars, int index, int count, byte[] buffer)
    {
        int byteCount = _encoding.GetBytes(chars, index, count, buffer, 0);

        OutStream.Write(buffer, 0, byteCount);
    }
#endif

    /// <summary>
    /// Gets <see cref="_scratch"/>, creating it on first use.
    /// </summary>
    /// <remarks>The allocation is kept in <see cref="CreateScratch"/>, so that the getter stays small enough to be inlined everywhere.</remarks>
    private byte[] Scratch => _scratch ?? CreateScratch();

    /// <summary>
    /// Gets <see cref="_nullTerminator"/>, creating it on first use.
    /// </summary>
    private byte[] NullTerminator => _nullTerminator ??= _encoding.GetBytes("\0");

    /// <summary>
    /// Gets <see cref="_newLine"/>, creating it on first use.
    /// </summary>
    private byte[] NewLine => _newLine ??= _encoding.GetBytes(Environment.NewLine);

    /// <summary>
    /// Creates <see cref="_scratch"/>.
    /// </summary>
    /// <returns>The new buffer.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private byte[] CreateScratch()
    {
        return _scratch = new byte[c_ScratchSize];
    }

    /// <inheritdoc/>
    public long Position
    {
        get => OutStream.Position;
        set => OutStream.Position = value;
    }

    /// <inheritdoc/>
    public long Length => OutStream.Length;

    /// <summary>
    /// Gets the underlying stream of the writer.
    /// </summary>
    /// <remarks>Unlike <see cref="BinaryWriter.BaseStream"/>, this does not flush the stream; the writer passes every write straight to the stream and holds no buffered data.</remarks>
    public override Stream BaseStream => OutStream;

    /// <inheritdoc/>
    public Encoding Encoding => _encoding;
}