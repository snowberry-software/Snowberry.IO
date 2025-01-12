using System.Runtime.CompilerServices;
using System.Text;
using Snowberry.IO.Common;

namespace Snowberry.IO.Reader;

public partial class BaseEndianReader
{
    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public byte ReadByte()
    {
        ThrowIfDisposed();

        if (ReadInInternalBuffer(1, 0) == 0)
            ThrowEndOfStreamException();

        return _buffer[0];
    }

    /// <inheritdoc/>
    public sbyte ReadSByte()
    {
        return (sbyte)ReadByte();
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public int ReadByteSafe()
    {
        int read = ReadInInternalBuffer(1, 0);

        if (read == 0)
            return -1;

        return _buffer[0];
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public int Read(byte[] buffer, int offset, int byteCount)
    {
        return Read(buffer.AsSpan()[offset..byteCount]);
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public void ReadExactly(byte[] buffer, int offset, int byteCount)
    {
        ReadExactly(buffer.AsSpan()[offset..byteCount]);
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public int ReadInInternalBuffer(int byteCount, int offset)
    {
        return Read(Buffer, offset, byteCount);
    }

    /// <inheritdoc/>
    public byte[] ReadBytes(int count)
    {
        if (count == 0)
            return Array.Empty<byte>();

        byte[] result = new byte[count];

        int numRead = 0;

        do
        {
            int read = Read(result, numRead, count);

            if (read == 0)
                break;

            numRead += read;
            count -= read;
        } while (count > 0);

        return result;
    }

    /// <inheritdoc/>
    public string ReadString()
    {
        // NOTE(VNC):
        //
        // Implementation is based on the original BinaryReader BCL type.
        //
        // https://source.dot.net/#System.Private.CoreLib/BinaryReader.cs
        //
        // Licensed to the .NET Foundation under one or more agreements.
        // The .NET Foundation licenses this file to you under the MIT license.

        // Length of the string in bytes, not chars.
        int stringLength = Read7BitEncodedInt();

        if (stringLength < 0)
            throw new IOException("String length can't be less than 0.");

        if (stringLength == 0)
            return string.Empty;

        int position = 0;
        int n;
        int readLength;

        var sb = new StringBuilder();

        Span<byte> charBytes = stackalloc byte[MaxCharBytesSize];
        _charBuffer ??= new char[_maxCharsSize];

        do
        {
            readLength = ((stringLength - position) > MaxCharBytesSize) ? MaxCharBytesSize : (stringLength - position);

            n = Read(charBytes[..readLength]);

            if (n == 0)
                ThrowEndOfStreamException();

            if (position == 0 && n == stringLength)
#if NETSTANDARD2_0
                return _encoding.GetString(charBytes[..n].ToArray());
#else
                return _encoding.GetString(charBytes[..n]);
#endif

#if NETSTANDARD2_0
            int charsRead = _decoder.GetChars(charBytes[..n].ToArray(), 0, n, _charBuffer, 0);
#else
            int charsRead = _decoder.GetChars(charBytes[..n], _charBuffer, flush: false);
#endif

            sb.Append(_charBuffer, 0, charsRead);
            position += n;

        } while (position < stringLength);

        return sb.ToString();
    }

    /// <inheritdoc/>
    public string? ReadLine()
    {
        if (!CanReadData)
            return null;

        _stringBuilder.Clear();

        {
            // NOTE(VNC): Character value...
            int value = 0;

            var decoder = _encoding.GetDecoder();
            Span<byte> singleByteSpan = stackalloc byte[1];
            Span<char> decodedCharSpan = stackalloc char[_maxCharsSize];

            //if (!_encoding.IsSingleByte)
            //decoder = _encoding.GetDecoder();

            while (value is not '\r' and not '\n')
            {
                value = ReadByteSafe();

                if (value == -1)
                    break;

                if (value == '\r' || value == '\n' || !CanReadData)
                    break;

                singleByteSpan[0] = (byte)value;
                AppendCharacters(decoder, singleByteSpan, decodedCharSpan, out _);
            }

            // CR LF
            if (value == '\r' && CanReadData)
                ReadByte();
        }

        return _stringBuilder.ToString();
    }

    /// <inheritdoc/>
    public virtual string ReadCString()
    {
        if (!CanReadData)
            ThrowEndOfStreamException();

        _stringBuilder.Clear();

        {
            var decoder = _encoding.GetDecoder();
            Span<byte> singleByteSpan = stackalloc byte[1];
            Span<char> decodedCharSpan = stackalloc char[_maxCharsSize];

            while (true)
            {
                int value = ReadByteSafe();

                if (value is -1 or 0)
                    break;

                singleByteSpan[0] = (byte)value;
                AppendCharacters(decoder, singleByteSpan, decodedCharSpan, out _);
            }
        }

        return _stringBuilder.ToString();
    }

    /// <inheritdoc/>
    public string ReadSizedCString(int size, bool adjustPosition = true)
    {
        _stringBuilder.Clear();

        var decoder = _encoding.GetDecoder();

        Span<char> decodedCharSpan = stackalloc char[_maxCharsSize];
        Span<byte> charBytes = stackalloc byte[MaxCharBytesSize];

        int readLength;
        int n;
        int startSize = size;

        do
        {

            readLength = size > MaxCharBytesSize ? MaxCharBytesSize : size;
            n = Read(charBytes[..readLength]);

            if (n == 0)
                ThrowEndOfStreamException();

            size -= n;

            int endIndex = charBytes.IndexOf<byte>(0);
            AppendCharacters(decoder, endIndex > 0 ? charBytes[..endIndex] : charBytes, decodedCharSpan, out int decodedCharCount);

            if (size > 0 && endIndex != -1)
            {
                if (adjustPosition)
                    Position += size;

                break;
            }

        } while (size > 0);

        return _stringBuilder.ToString();
    }

    private void AppendCharacters(Decoder decoder, Span<byte> bytes, Span<char> chars, out int decodedCharCount)
    {
        _ = decoder ?? throw new ArgumentNullException(nameof(decoder));

#if NETSTANDARD2_0
        char[] charBuffer = new char[chars.Length];
        decodedCharCount = decoder.GetChars(bytes.ToArray(), 0, bytes.Length, charBuffer, 0);

        if (decodedCharCount != 0)
            _stringBuilder.Append(charBuffer.AsSpan()[..decodedCharCount].ToArray());
#else
        decodedCharCount = decoder.GetChars(bytes, chars, false);

        if (decodedCharCount != 0)
            _stringBuilder.Append(chars[..decodedCharCount]);
#endif
    }

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public void ReadAlignment(byte alignment)
    {
        long position = Position;
        BinaryUtils.ApplyAlignment(ref position, alignment);
        Position = position;
    }

    /// <inheritdoc/>
    public byte[] ReadUntilEnd(int maxBufferSize = 16 * 1024)
    {
        byte[] buffer = new byte[maxBufferSize];

        using var ms = new MemoryStream();

        int read;
        while ((read = Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, read);
        }

        return ms.ToArray();
    }

    /// <inheritdoc/>
    public bool ReadBool()
    {
        return ReadByte() > 0;
    }

    /// <inheritdoc/>
    public virtual long Read7BitEncodedLong()
    {
        long output = 0;
        int shiftVariable = 0;

        while (true)
        {
            int b = ReadByte();

            output |= (long)(b & 0x7F) << shiftVariable;

            if (b >> 0x7 == 0)
                break;

            shiftVariable += 0x7;
        }

        return output;
    }

    /// <inheritdoc/>
    public virtual int Read7BitEncodedInt()
    {
        int output = 0;
        int shiftVariable = 0;

        while (true)
        {
            int b = ReadByte();

            output |= (b & 0x7F) << shiftVariable;

            if (b >> 0x7 == 0)
                break;

            shiftVariable += 0x7;
        }

        return output;
    }
}
