using System.Runtime.CompilerServices;
using System.Text;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Writer.Interfaces;

namespace Snowberry.IO.Writer;

/// <summary>
/// <see cref="BinaryWriter"/> extended to support different endian types (<see cref="EndianType"/>).
/// </summary>
public class EndianStreamWriter : BinaryWriter, IEndianWriter
{
    protected Encoding _encoding;

    /// <summary>
    /// Creates a new <see cref="EndianStreamWriter" /> instance.
    /// </summary>
    /// <remarks>
    /// Uses the <see cref="Encoding.Default"/> as encoding.
    /// </remarks>
    /// <param name="stream">The target stream.</param>
    /// <param name="keepStreamOpen">Keep the stream open after using the writer.</param>
    public EndianStreamWriter(Stream stream, bool keepStreamOpen) : base(stream, Encoding.Default, keepStreamOpen)
    {
        _encoding = Encoding.Default;
    }

    /// <summary>
    /// Creates a new <see cref="EndianStreamWriter" /> instance with different options.
    /// </summary>
    /// <param name="stream">The target stream.</param>
    /// <param name="keepStreamOpen">Keep the stream open after using the writer.</param>
    /// <param name="encoding">The encoding to use.</param>
    public EndianStreamWriter(Stream stream, bool keepStreamOpen, Encoding encoding) : base(stream, encoding, keepStreamOpen)
    {
        _encoding = encoding;
    }

    /// <inheritdoc/>
    public unsafe IEndianWriter Write(Guid value, EndianType endian = EndianType.LITTLE)
    {
        byte[] guidBytes = value.ToByteArray();

        if (endian == EndianType.BIG)
        {
            Span<byte> data = stackalloc byte[16];

            data[0] = guidBytes[3];
            data[1] = guidBytes[2];
            data[2] = guidBytes[1];
            data[3] = guidBytes[0];
            data[4] = guidBytes[5];
            data[5] = guidBytes[4];
            data[6] = guidBytes[7];
            data[7] = guidBytes[6];

            data[8] = guidBytes[8];
            data[9] = guidBytes[9];
            data[10] = guidBytes[10];
            data[11] = guidBytes[11];
            data[12] = guidBytes[12];
            data[13] = guidBytes[13];
            data[14] = guidBytes[14];
            data[15] = guidBytes[15];

            base.Write(data);
            return this;
        }

        base.Write(guidBytes, 0, 16);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe IEndianWriter Write(double value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            Write(*(long*)&value, EndianType.BIG);
            return this;
        }

        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe IEndianWriter Write(float value, EndianType endian = EndianType.LITTLE)
    {
        if (endian == EndianType.BIG)
        {
            Write(*(int*)&value, EndianType.BIG);
            return this;
        }

        base.Write(value);
        return this;
    }

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
            base.Write(((value & 0xFF) << 24) | ((value & 0xFF00) << 0x8) | ((value >> 0x8) & 0xFF00) | ((value >> 24) & 0xFF));
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
            base.Write(((value & 0xFF) << 24) | ((value & 0xFF00) << 0x8) | ((value >> 0x8) & 0xFF00) | ((value >> 24) & 0xFF));
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
            base.Write(((value & byte.MaxValue) << 56)
                  | ((value & 0xFF00) << 40)
                  | ((value & 0xFF0000) << 24)
                  | ((value & 0xFF000000) << 8)
                  | ((value >> 8) & 0xFF000000)
                  | ((value >> 24) & 0xFF0000)
                  | ((value >> 40) & 0xFF00)
                  | ((value >> 56) & byte.MaxValue));

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
            base.Write((short)(ushort)(((value & 0xFF) << 0x8) | ((value & 0xFF00) >> 0x8)));
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
            base.Write((ushort)(((value & 0xFF) << 0x8) | ((value & 0xFF00) >> 0x8)));
            return this;
        }

        base.Write(value);
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter Write(Sha1 value)
    {
        base.Write(value.GetHashBuffer(), 0, Sha1.StructSize);
        return this;
    }

    /// <inheritdoc/>
    public IEndianWriter WriteSizedCString(string text, int size)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (size < text.Length)
            throw new ArgumentOutOfRangeException($"{nameof(size)}", "The size must be greater than the text length.");

        int byteCount = _encoding.GetByteCount(text.AsSpan());

        if (byteCount > size)
            throw new IOException($"The byte count of the text `{byteCount}` is greater than the specified size `{size}`.\nCheck the data of the text or the encoding that is used.");

        WriteStringCharacters(text);

        int missingLength = size - byteCount;

        // NOTE(VNC): There is nothing left that has to be padded. 
        if (missingLength < 1)
            return this;

        // NOTE(VNC): Add remaining padding.
        base.Write(new byte[missingLength]);
        return this;
    }

    /// <inheritdoc/>
    public IEndianWriter WriteLine(string text)
    {
        WriteStringCharacters(text);
        base.Write(Environment.NewLine.AsSpan());
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEndianWriter WritePadding(byte alignment)
    {
        long padding = BinaryUtils.CalculatePadding(BaseStream.Position, alignment);
        base.Write(new byte[padding]);

        return this;
    }

    /// <inheritdoc/>
    public IEndianWriter WriteStringCharacters(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        base.Write(text.AsSpan());

        return this;
    }

    /// <inheritdoc/>
    public IEndianWriter WriteCString(string text)
    {
        WriteStringCharacters(text);
        base.Write((byte)0);

        return this;
    }

    // NOTE(VNC):
    // 
    // It will always use the `Write(short, EndianType)` method when using the `EndianStreamWriter` type directly to write a single byte,
    // even when directly casting the parameter to a byte.
    // This seems to fix the issue.
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

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    IEndianWriter IEndianWriter.Write(string value)
    {
        base.Write(value);
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

    /// <inheritdoc/>
    public long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    /// <inheritdoc/>
    public long Length => BaseStream.Length;

    /// <inheritdoc/>
    public Encoding Encoding => _encoding;
}
