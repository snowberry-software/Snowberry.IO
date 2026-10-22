using System;
#if NET9_0_OR_GREATER
using System.Buffers;
#endif
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
#if !NET9_0_OR_GREATER
using System.Globalization;
#endif
using System.Runtime.CompilerServices;
#if !NETSTANDARD2_0
using System.Runtime.InteropServices;
#endif

namespace Snowberry.IO.Common;

/// <summary>
/// Represents a SHA-1 hash.
/// </summary>
/// <remarks>
/// The 20-byte digest is stored as five components A to E. Each component is the little-endian reading of the matching 4-byte slice
/// of the digest bytes (A is bytes 0 to 3, B is bytes 4 to 7, and so on), not the SHA-1 state words H0 to H4.
/// </remarks>
public struct Sha1 : IEquatable<Sha1>
{
    // Reference: https://en.wikipedia.org/wiki/SHA-1

    /// <summary>
    /// The size of the SHA-1 hash in bytes.
    /// </summary>
    public const int c_StructSize = 20;

    /// <summary>
    /// A <see cref="Sha1"/> instance with all components set to zero.
    /// </summary>
    public static readonly Sha1 s_Zero = new(null as byte[]);

    private uint _a, _b, _c, _d, _e;

    /// <summary>
    /// Initializes a new instance of the <see cref="Sha1"/> struct using the given buffer.
    /// </summary>
    /// <remarks>The <paramref name="buffer"/> must have a length of exactly 20 bytes when specified. When <paramref name="buffer"/> is <see langword="null"/>, all components are set to zero (see <see cref="s_Zero"/>).</remarks>
    /// <param name="buffer">The optional pre-defined buffer containing the SHA-1 hash.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="buffer"/> is not <see langword="null"/> and its length is not exactly 20 bytes.</exception>
    public Sha1(byte[]? buffer)
    {
        if (buffer == null)
        {
            _a = _b = _c = _d = _e = 0;
            return;
        }

        From(buffer.AsSpan());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Sha1"/> struct using the given components.
    /// </summary>
    /// <remarks>Each component is the little-endian reading of a 4-byte slice of the digest, see <see cref="Sha1"/>.</remarks>
    /// <param name="a">The little-endian reading of digest bytes 0 to 3.</param>
    /// <param name="b">The little-endian reading of digest bytes 4 to 7.</param>
    /// <param name="c">The little-endian reading of digest bytes 8 to 11.</param>
    /// <param name="d">The little-endian reading of digest bytes 12 to 15.</param>
    /// <param name="e">The little-endian reading of digest bytes 16 to 19.</param>
    public Sha1(uint a, uint b, uint c, uint d, uint e)
    {
        _a = a;
        _b = b;
        _c = c;
        _d = d;
        _e = e;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Sha1"/> struct using the given span data.
    /// </summary>
    /// <remarks>The <paramref name="data"/> must have a length of exactly 20 bytes.</remarks>
    /// <param name="data">The span data containing the SHA-1 hash.</param>
    /// <exception cref="ArgumentException">Thrown when the length of <paramref name="data"/> is not exactly 20 bytes.</exception>
    public Sha1(ReadOnlySpan<byte> data)
    {
        From(data);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Sha1"/> struct using the given hash string.
    /// </summary>
    /// <remarks>The <paramref name="hash"/> must have a length of 40 characters, each pair of characters being parsed as a hexadecimal <see langword="byte"/>.</remarks>
    /// <param name="hash">The hash string.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hash"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the length of <paramref name="hash"/> is not 40 characters.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="hash"/> contains characters that are not valid hexadecimal digits.</exception>
    public Sha1(string hash)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(hash);
#else
        _ = hash ?? throw new ArgumentNullException(nameof(hash));
#endif

        if (hash.Length != 40)
            throw new ArgumentException($"{nameof(Sha1)} must have a length of 40 characters.", nameof(hash));

        Span<byte> bytes = stackalloc byte[c_StructSize];

#if NET9_0_OR_GREATER
        if (Convert.FromHexString(hash, bytes, out _, out _) != OperationStatus.Done)
            throw new FormatException($"{nameof(Sha1)} must only contain hexadecimal digits.");
#else
        for (int i = 0; i < c_StructSize; i++)
        {
            int high = HexDigitValue(hash[i * 2]);
            int low = HexDigitValue(hash[(i * 2) + 1]);

            // NOTE(VNC): A pair that is not two hex digits goes to byte.Parse, so it throws the same FormatException and still accepts
            // what NumberStyles.AllowHexSpecifier accepts beyond them (a digit followed by a NUL).
            bytes[i] = (high | low) >= 0 ? (byte)((high << 4) | low) : ParseHexPair(hash, i * 2);
        }
#endif

        From(bytes);
    }

    /// <summary>
    /// Compares two <see cref="Sha1"/> instances for equality.
    /// </summary>
    /// <param name="a">The left <see cref="Sha1"/> instance.</param>
    /// <param name="b">The right <see cref="Sha1"/> instance.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Sha1 a, Sha1 b)
    {
        // NOTE(VNC): Comparing the fields here instead of calling Equals saves .NET Framework another copy of b.
        return a._a == b._a && a._b == b._b && a._c == b._c && a._d == b._d && a._e == b._e;
    }

    /// <summary>
    /// Compares two <see cref="Sha1"/> instances for inequality.
    /// </summary>
    /// <param name="a">The left <see cref="Sha1"/> instance.</param>
    /// <param name="b">The right <see cref="Sha1"/> instance.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Sha1 a, Sha1 b)
    {
        // NOTE(VNC): Comparing the fields here instead of calling == saves .NET Framework another copy of both operands.
        return a._a != b._a || a._b != b._b || a._c != b._c || a._d != b._d || a._e != b._e;
    }

    /// <summary>
    /// Gets the message of the exception for span data of <paramref name="length"/> bytes, which is not exactly <see cref="c_StructSize"/>.
    /// </summary>
    /// <param name="length">The length, in bytes, of the span data.</param>
    /// <returns>The message for data that is too short or too long.</returns>
    private static string GetInvalidLengthMessage(int length)
    {
        return length < c_StructSize
            ? $"{nameof(Sha1)} must be a minimum of {c_StructSize} bytes in length"
            : $"{nameof(Sha1)} cannot be more than {c_StructSize} bytes in length";
    }

    /// <summary>
    /// Throws the exception for span data of <paramref name="length"/> bytes, which is not exactly <see cref="c_StructSize"/>.
    /// </summary>
    /// <remarks>
    /// Building the message in <see cref="From"/> would give it a large stack frame and keep it from inlining. This method stays small, so
    /// the JIT sees that it always throws and treats the call as not returning. It takes the length instead of the span, which .NET Framework
    /// would copy into a temporary that it zeroes on every call.
    /// </remarks>
    /// <param name="length">The length, in bytes, of the span data.</param>
    /// <exception cref="ArgumentException">Always thrown, for the <c>data</c> parameter of <see cref="From"/>.</exception>
    [SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly", Justification = "Thrown for the data parameter of From, which passes only its length.")]
    private static void ThrowInvalidLength(int length)
    {
        throw new ArgumentException(GetInvalidLengthMessage(length), "data");
    }

#if NETSTANDARD2_0
    /// <summary>
    /// Converts <paramref name="value"/> from host byte order to little-endian byte order.
    /// </summary>
    /// <param name="value">The value in host byte order.</param>
    /// <returns><paramref name="value"/> in little-endian byte order.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ToLittleEndian(uint value)
    {
        return HostByteOrder.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value);
    }
#endif

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Equals(Sha1 other)
    {
        return _a == other._a && _b == other._b && _c == other._c && _d == other._d && _e == other._e;
    }

    /// <inheritdoc />
    public override readonly bool Equals(object? obj)
    {
        return obj is Sha1 other && other.Equals(this);
    }

    /// <summary>
    /// Populates the <see cref="Sha1"/> instance using the given span data.
    /// </summary>
    /// <param name="data">The span data containing the SHA-1 hash.</param>
    /// <exception cref="ArgumentException">Thrown when the length of <paramref name="data"/> is not exactly 20 bytes.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void From(ReadOnlySpan<byte> data)
    {
        if (data.Length != c_StructSize)
            ThrowInvalidLength(data.Length);

        // NOTE(VNC): The five components are stored in digest order, so on a little-endian host the memory of a Sha1 is the digest
        // and is read in one go.
#if NETSTANDARD2_0
        this = Unsafe.ReadUnaligned<Sha1>(ref Unsafe.AsRef(in data[0]));

        if (!HostByteOrder.IsLittleEndian)
#else
        this = MemoryMarshal.Read<Sha1>(data);

        if (!BitConverter.IsLittleEndian)
#endif
        {
            _a = BinaryPrimitives.ReverseEndianness(_a);
            _b = BinaryPrimitives.ReverseEndianness(_b);
            _c = BinaryPrimitives.ReverseEndianness(_c);
            _d = BinaryPrimitives.ReverseEndianness(_d);
            _e = BinaryPrimitives.ReverseEndianness(_e);
        }
    }

    /// <summary>
    /// Returns the <see cref="Sha1"/> value as a byte array.
    /// </summary>
    /// <returns>A byte array containing the SHA-1 hash.</returns>
    public readonly byte[] GetHashBuffer()
    {
        byte[] buffer = new byte[c_StructSize];
        TryWriteBytes(buffer);

        return buffer;
    }

    /// <inheritdoc />
    public override readonly int GetHashCode()
    {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        return HashCode.Combine(_a, _b, _c, _d, _e);
#else
        return 0x2383 ^ _a.GetHashCode() ^ _b.GetHashCode() ^ _c.GetHashCode() ^ _d.GetHashCode() ^ _e.GetHashCode();
#endif
    }

    /// <inheritdoc/>
    public override readonly string ToString()
    {
        Span<byte> buffer = stackalloc byte[c_StructSize];
        TryWriteBytes(buffer);

#if NET5_0_OR_GREATER
        return Convert.ToHexString(buffer);
#else
        return ToHexStringFallback(buffer);
#endif
    }

    /// <summary>
    /// Attempts to write the 20 digest bytes, each component in little-endian byte order, to <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The destination span that the hash bytes are written to.</param>
    /// <returns><see langword="true"/> if <paramref name="destination"/> was long enough to receive the hash and the write succeeded; otherwise, <see langword="false"/>.</returns>
    public readonly bool TryWriteBytes(Span<byte> destination)
    {
#if NETSTANDARD2_0
        if (destination.Length < c_StructSize)
            return false;

        // NOTE(VNC): The portable BinaryPrimitives copy the span on each write, and a Sha1 copy is zeroed on every call on
        // .NET Framework, so the components are written one by one through the first element.
        ref byte bytes = ref destination[0];
        Unsafe.WriteUnaligned(ref bytes, ToLittleEndian(_a));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref bytes, 4), ToLittleEndian(_b));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref bytes, 8), ToLittleEndian(_c));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref bytes, 12), ToLittleEndian(_d));
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref bytes, 16), ToLittleEndian(_e));

        return true;
#else
        // NOTE(VNC): On a little-endian host the memory of a Sha1 is the digest, so it is written in one go.
        var value = this;

        if (!BitConverter.IsLittleEndian)
        {
            value._a = BinaryPrimitives.ReverseEndianness(value._a);
            value._b = BinaryPrimitives.ReverseEndianness(value._b);
            value._c = BinaryPrimitives.ReverseEndianness(value._c);
            value._d = BinaryPrimitives.ReverseEndianness(value._d);
            value._e = BinaryPrimitives.ReverseEndianness(value._e);
        }

#if NETSTANDARD2_1
        return MemoryMarshal.TryWrite(destination, ref value);
#else
        return MemoryMarshal.TryWrite(destination, in value);
#endif
#endif
    }

#if !NET9_0_OR_GREATER
    /// <summary>
    /// Gets the value of the hexadecimal digit <paramref name="c"/>.
    /// </summary>
    /// <param name="c">The character to convert.</param>
    /// <returns>The value from 0 to 15, or -1 when <paramref name="c"/> is not one of <c>0</c> to <c>9</c>, <c>A</c> to <c>F</c> and <c>a</c> to <c>f</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HexDigitValue(char c)
    {
        uint digit = (uint)(c - '0');

        if (digit <= 9)
            return (int)digit;

        // NOTE(VNC): Setting bit 5 maps A-F onto a-f and no other character onto them.
        uint letter = (uint)((c | 0x20) - 'a');
        return letter <= 5 ? (int)(letter + 10) : -1;
    }

    /// <summary>
    /// Parses the two characters of <paramref name="hash"/> starting at <paramref name="index"/> as a hexadecimal <see langword="byte"/>.
    /// </summary>
    /// <remarks>Only reached for a pair that is not two hex digits, so it is kept out of the parsing loop.</remarks>
    /// <param name="hash">The hash string.</param>
    /// <param name="index">The index of the first of the two characters.</param>
    /// <returns>The parsed <see langword="byte"/>.</returns>
    /// <exception cref="FormatException">Thrown when the two characters are not a hexadecimal <see langword="byte"/>.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte ParseHexPair(string hash, int index)
    {
#if NETSTANDARD2_1_OR_GREATER
        return byte.Parse(hash.AsSpan(index, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
#else
        return byte.Parse(hash.Substring(index, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
#endif
    }
#endif

#if !NET5_0_OR_GREATER
    /// <summary>
    /// Converts <paramref name="data"/> to an uppercase hexadecimal string.
    /// </summary>
    /// <remarks>The characters go into a stack buffer that the string is created from, so the string is the only allocation.</remarks>
    /// <param name="data">The bytes to convert.</param>
    /// <returns>An uppercase hexadecimal string representation of <paramref name="data"/>.</returns>
    private static unsafe string ToHexStringFallback(ReadOnlySpan<byte> data)
    {
        char* chars = stackalloc char[data.Length * 2];

        for (int i = 0; i < data.Length; i++)
        {
            uint value = data[i];
            chars[i * 2] = ToHexDigit(value >> 4);
            chars[(i * 2) + 1] = ToHexDigit(value & 0xF);
        }

        return new string(chars, 0, data.Length * 2);
    }

    /// <summary>
    /// Converts <paramref name="nibble"/> to its uppercase hexadecimal digit.
    /// </summary>
    /// <param name="nibble">The value from 0 to 15.</param>
    /// <returns>The character <c>0</c> to <c>9</c> or <c>A</c> to <c>F</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char ToHexDigit(uint nibble)
    {
        // NOTE(VNC): 9 - nibble wraps around for 10 to 15, so its top bit adds the 7 characters between '9' and 'A'.
        return (char)(nibble + '0' + (((9 - nibble) >> 31) * 7));
    }
#endif
}