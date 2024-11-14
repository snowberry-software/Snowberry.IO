﻿using System.Runtime.CompilerServices;
using System.Text;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Reader;

/// <summary>
/// Base implementation of <see cref="IEndianReader"/>.
/// </summary>
public abstract partial class BaseEndianReader : IEndianReader
{
    /// <summary>
    /// The largest data type is the <see cref="Sha1"/> type.
    /// </summary>
    public const int MinBufferSize = 20;

    protected const int MaxCharBytesSize = 128;

    protected byte[] _buffer;

    protected long _viewOffset;
    protected bool _isRegionViewEnabled;

    protected readonly Decoder _decoder;
    protected readonly Encoding _encoding;
    protected char[]? _charBuffer;
    protected int _maxCharsSize;

    protected readonly bool _2BytesPerChar;

    protected StringBuilder _stringBuilder = new();

    /// <summary>
    /// Creates a new reader instance.
    /// </summary>
    /// <remarks>
    /// The <paramref name="bufferSize"/> must be at least <see cref="IEndianReader.MinBufferSize"/>, the size will automatically be adjusted if that's not the case.<para/>
    /// The <see cref="Encoding.Default"/> will be used for the <see cref="Encoding"/>.
    /// </remarks>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The size of the internal buffer.</param>
    protected BaseEndianReader(Analyzer? analyzer = null, int bufferSize = 0) : this(analyzer, bufferSize, Encoding.Default)
    {
    }

    /// <summary>
    /// <inheritdoc cref="BaseEndianReader(Analyzer?, int)"/>
    /// </summary>
    /// <remarks>
    /// The <paramref name="bufferSize"/> must be at least 20, if that's not the case it will automatically adjust the size to it.
    /// </remarks>
    /// <param name="analyzer">The optional analyzer instance.</param>
    /// <param name="bufferSize">The size of the internal buffer.</param>
    /// <param name="encoding">The encoding to use.</param>
    protected BaseEndianReader(Analyzer? analyzer, int bufferSize, Encoding encoding)
    {
        _ = encoding ?? throw new ArgumentNullException(nameof(encoding));

        if (bufferSize < MinBufferSize)
            bufferSize = MinBufferSize;

        _buffer = new byte[bufferSize];
        Analyzer = analyzer;

        _encoding = encoding;
        _decoder = encoding.GetDecoder();
        _maxCharsSize = encoding.GetMaxCharCount(MaxCharBytesSize);

        // For Encodings that always use 2 bytes per char (or more),
        // special case them here to make Read() & Peek() faster.
        _2BytesPerChar = encoding is UnicodeEncoding;
    }

    protected void ThrowIfDisposed()
    {
        if (Disposed)
            throw new ObjectDisposedException(nameof(BaseEndianReader));
    }

    /// <summary>
    /// Reads a specified number of bytes from the current stream into a buffer.
    /// </summary>
    /// <param name="inBuffer">The buffer to store the read bytes.</param>
    /// <param name="offset">The zero-based byte offset in the buffer at which to begin storing the data read from the stream.</param>
    /// <param name="byteCount">The maximum number of bytes to read from the current stream.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This can be less than the number
    /// of bytes requested if that many bytes are not currently available, or zero if the end of the stream is reached.
    /// </returns>
    /// <remarks>
    /// This method is intended to be implemented by derived classes to provide the actual logic for reading bytes.
    /// </remarks>
    protected abstract int InternalReadBytes(byte[] inBuffer, int offset, int byteCount);

    /// <inheritdoc/>
    public abstract void CopyTo(Stream destination);

    /// <inheritdoc/>
    public abstract void CopyTo(Stream destination, int length, int bufferSize = 0x14000);

    /// <inheritdoc/>
#if NETCOREAPP3_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public byte ReadByte()
    {
        ReadInInternalBuffer(1, 0);
        return _buffer[0];
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
    public virtual string? ReadCString()
    {
        if (!CanReadData)
            return null;

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
    public string? ReadSizedCString(int size, bool adjustPosition = true)
    {
        if (!CanReadData)
            return null;

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
                throw new EndOfStreamException();

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

    /// <inheritdoc/>
    public string? ReadString()
    {
        if (!CanReadData)
            return null;

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
                throw new EndOfStreamException();

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

        // 13 = '\r'
        // 10 = '\n'
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

    /// <inheritdoc/>
    public void EnsureBufferSize(int bufferSize)
    {
        if (Buffer == null || Buffer.Length < bufferSize)
            Buffer = new byte[bufferSize];
    }

    /// <inheritdoc/>
    public virtual void EnableRegionView(RegionRange region)
    {
        RegionView = region;

        _isRegionViewEnabled = true;
        Position = 0;
    }

    /// <inheritdoc/>
    public virtual void DisableRegionView()
    {
        _isRegionViewEnabled = false;
        _viewOffset = 0;
    }

    /// <summary>
    /// Returns either the position of the current view offset or the given <paramref name="position"/>.
    /// </summary>
    /// <param name="position">The position that will be used if <see cref="IsRegionViewEnabled"/> is disabled.</param>
    /// <returns>The current position to use.</returns>
    protected long GetViewOrPosition(long position)
    {
        if (IsRegionViewEnabled)
            return _viewOffset;

        return position;
    }

    /// <summary>
    /// Updates the <paramref name="position"/> value..
    /// </summary>
    /// <remarks>
    /// This will automatically adjust <paramref name="position"/> depending on <see cref="IsRegionViewEnabled"/>.
    /// </remarks>
    /// <param name="position">The position to update.</param>
    /// <param name="newPosition">The new position.</param>
    protected void SetPosition(ref long position, long newPosition)
    {
        if (IsRegionViewEnabled)
        {
            _viewOffset = newPosition;
            position = RegionView.StartPosition + newPosition;
            return;
        }

        position = newPosition;
    }

    /// <inheritdoc/>
    public abstract void Dispose();

    /// <inheritdoc/>
    public virtual bool CanReadData => Length > 0;

    /// <inheritdoc/>
    public abstract long Length { get; }

    /// <inheritdoc/>
    public abstract long Position { get; set; }

    /// <inheritdoc/>
    public virtual byte[] Buffer
    {
        get => _buffer;
        set => _buffer = value;
    }

    /// <inheritdoc/>
    public virtual bool IsRegionViewEnabled => _isRegionViewEnabled;

    /// <inheritdoc/>
    public virtual RegionRange RegionView { get; protected set; }

    /// <inheritdoc/>
    public abstract long ActualPosition { get; }

    /// <inheritdoc/>
    public abstract long ActualLength { get; }

    /// <inheritdoc/>
    public bool Disposed { get; protected set; }

    /// <inheritdoc/>
    public Encoding Encoding => _encoding;

    /// <inheritdoc/>
    public Analyzer? Analyzer { get; set; }
}
