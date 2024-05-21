using System;
using System.Globalization;
using System.Text;

namespace Snowberry.IO.Common;

/// <summary>
/// Represents a SHA-1 hash.
/// </summary>
public struct Sha1 : IEquatable<Sha1>
{
    // Reference: https://en.wikipedia.org/wiki/SHA-1

    /// <summary>
    /// The size of the SHA-1 hash in bytes.
    /// </summary>
    public const int StructSize = 20;

    /// <summary>
    /// A <see cref="Sha1"/> instance with all components set to zero.
    /// </summary>
    public static readonly Sha1 Zero = new(null as byte[]);

    private uint _a, _b, _c, _d, _e;

    /// <summary>
    /// Initializes a new instance of the <see cref="Sha1"/> struct using the given buffer.
    /// </summary>
    /// <remarks>The <paramref name="buffer"/> must have a length of at least 20 bytes when specified.</remarks>
    /// <param name="buffer">The optional pre-defined buffer containing the SHA-1 hash.</param>
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
    /// <param name="a">The A component.</param>
    /// <param name="b">The B component.</param>
    /// <param name="c">The C component.</param>
    /// <param name="d">The D component.</param>
    /// <param name="e">The E component.</param>
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
    /// <remarks>The <paramref name="data"/> must have a length of at least 20 bytes.</remarks>
    /// <param name="data">The span data containing the SHA-1 hash.</param>
    public Sha1(ReadOnlySpan<byte> data)
    {
        From(data);
    }

    /// <summary>
    /// Populates the <see cref="Sha1"/> instance using the given span data.
    /// </summary>
    /// <param name="data">The span data containing the SHA-1 hash.</param>
    /// <exception cref="ArgumentException">Thrown when the length of <paramref name="data"/> is less than 20 bytes.</exception>
    public void From(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20)
            throw new ArgumentException($"{nameof(Sha1)} must be a minimum of {StructSize} bytes in length", nameof(data));

        _a = (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
        _b = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
        _c = (uint)(data[8] | (data[9] << 8) | (data[10] << 16) | (data[11] << 24));
        _d = (uint)(data[12] | (data[13] << 8) | (data[14] << 16) | (data[15] << 24));
        _e = (uint)(data[16] | (data[17] << 8) | (data[18] << 16) | (data[19] << 24));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Sha1"/> struct using the given hash string.
    /// </summary>
    /// <remarks>The <paramref name="hash"/> must have a length of 40 characters.</remarks>
    /// <param name="hash">The hash string.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="hash"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the length of <paramref name="hash"/> is not 40 characters.</exception>
    public unsafe Sha1(string hash)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(hash);
#else
        _ = hash ?? throw new ArgumentNullException(nameof(hash));
#endif

        if (hash.Length != 40)
            throw new ArgumentException($"{nameof(Sha1)} must have a length of 40 characters.", nameof(hash));

#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        var span = hash.AsSpan();
#endif
        uint* comps = stackalloc uint[5];

        int offset = 0;
        for (int i = 0; i < 5; i++)
        {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
            byte b0 = byte.Parse(span.Slice(offset + 0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            byte b1 = byte.Parse(span.Slice(offset + 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            byte b2 = byte.Parse(span.Slice(offset + 4, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            byte b3 = byte.Parse(span.Slice(offset + 6, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
#else
            byte b0 = byte.Parse(hash.Substring(offset + 0, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            byte b1 = byte.Parse(hash.Substring(offset + 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            byte b2 = byte.Parse(hash.Substring(offset + 4, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            byte b3 = byte.Parse(hash.Substring(offset + 6, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
#endif
            comps[i] = (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));

            offset += 8;
        }

        _a = comps[0];
        _b = comps[1];
        _c = comps[2];
        _d = comps[3];
        _e = comps[4];
    }

    /// <summary>
    /// Compares two <see cref="Sha1"/> instances for inequality.
    /// </summary>
    /// <param name="a">The left <see cref="Sha1"/> instance.</param>
    /// <param name="b">The right <see cref="Sha1"/> instance.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(Sha1 a, Sha1 b)
    {
        return !(a == b);
    }

    /// <summary>
    /// Compares two <see cref="Sha1"/> instances for equality.
    /// </summary>
    /// <param name="a">The left <see cref="Sha1"/> instance.</param>
    /// <param name="b">The right <see cref="Sha1"/> instance.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(Sha1 a, Sha1 b)
    {
        return a.Equals(b);
    }

    /// <inheritdoc />
    public readonly bool Equals(Sha1 other)
    {
        return _a == other._a && _b == other._b && _c == other._c && _d == other._d && _e == other._e;
    }

    /// <inheritdoc />
    public override readonly bool Equals(object? obj)
    {
        return obj is Sha1 other && other.Equals(this);
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

    /// <summary>
    /// Returns the <see cref="Sha1"/> value as a byte array.
    /// </summary>
    /// <returns>A byte array containing the SHA-1 hash.</returns>
    public readonly byte[] GetHashBuffer()
    {
        // Converts the SHA-1 hash components into a byte array.
        return
        [
            // A
            (byte)(_a & 0xFF),
            (byte)((_a >> 8) & 0xFF),
            (byte)((_a >> 16) & 0xFF),
            (byte)((_a >> 24) & 0xFF),
            
            // B
            (byte)(_b & 0xFF),
            (byte)((_b >> 8) & 0xFF),
            (byte)((_b >> 16) & 0xFF),
            (byte)((_b >> 24) & 0xFF),
            
            // C
            (byte)(_c & 0xFF),
            (byte)((_c >> 8) & 0xFF),
            (byte)((_c >> 16) & 0xFF),
            (byte)((_c >> 24) & 0xFF),
            
            // D
            (byte)(_d & 0xFF),
            (byte)((_d >> 8) & 0xFF),
            (byte)((_d >> 16) & 0xFF),
            (byte)((_d >> 24) & 0xFF),

            // E
            (byte)(_e & 0xFF),
            (byte)((_e >> 8) & 0xFF),
            (byte)((_e >> 16) & 0xFF),
            (byte)((_e >> 24) & 0xFF)
        ];
    }

    /// <summary>
    /// Returns the <see cref="Sha1"/> hash as a string.
    /// </summary>
    /// <returns>A string representation of the SHA-1 hash.</returns>
    public override readonly unsafe string ToString()
    {
        // Converts the SHA-1 hash components into a hexadecimal string.
        var output = new StringBuilder(40);

        uint* components = stackalloc uint[5];
        components[0] = _a;
        components[1] = _b;
        components[2] = _c;
        components[3] = _d;
        components[4] = _e;

        for (int i = 0; i < 5; i++)
            output.Append(string.Concat(
                ((byte)(components[i] & 0xFF)).ToString("X2", CultureInfo.InvariantCulture),
                ((byte)(components[i] >> 8)).ToString("X2", CultureInfo.InvariantCulture),
                ((byte)(components[i] >> 16)).ToString("X2", CultureInfo.InvariantCulture),
                ((byte)(components[i] >> 24)).ToString("X2", CultureInfo.InvariantCulture)
            ));

        return output.ToString();
    }
}