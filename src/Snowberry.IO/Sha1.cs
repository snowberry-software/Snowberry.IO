using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Snowberry.IO;

/// <summary>
/// Representation of a Sha1 hash.
/// </summary>
public struct Sha1 : IEquatable<Sha1>
{
    // Reference: https://en.wikipedia.org/wiki/SHA-1

    public const int StructSize = 20;
    public static readonly Sha1 Zero = new(null as byte[]);

    private uint _a, _b, _c, _d, _e;

    /// <summary>
    /// Creates a new <see cref="Sha1" /> using the given data.
    /// </summary>
    /// <remarks>The <paramref name="buffer"/> must have a length of minimum 20 bytes when specified.</remarks>
    /// <param name="buffer">The optional pre-defined buffer.</param>
    public Sha1(byte[]? buffer)
    {
        if (buffer == null)
        {
            _a = _b = _c = _d = _e = 0;
            return;
        }

        if (buffer.Length < 20)
            throw new ArgumentException($"{nameof(Sha1)} buffer must be a minimum of {StructSize} bytes in length", nameof(buffer));

        _a = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
        _b = (uint)(buffer[4] | (buffer[5] << 8) | (buffer[6] << 16) | (buffer[7] << 24));
        _c = (uint)(buffer[8] | (buffer[9] << 8) | (buffer[10] << 16) | (buffer[11] << 24));
        _d = (uint)(buffer[12] | (buffer[13] << 8) | (buffer[14] << 16) | (buffer[15] << 24));
        _e = (uint)(buffer[16] | (buffer[17] << 8) | (buffer[18] << 16) | (buffer[19] << 24));
    }

    /// <summary>
    /// Creates a new <see cref="Sha1"/> using the given components.
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
    /// Create <see cref="Sha1"/> using the given span data.
    /// </summary>
    /// <remarks>The <paramref name="data"/> must have a length of minimum 20 bytes.</remarks>
    /// <param name="data">The span data.</param>
    public Sha1(ReadOnlySpan<byte> data)
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
    /// Create <see cref="Sha1"/> using the given <paramref name="hash"/>.
    /// </summary>
    /// <remarks>The <paramref name="hash"/> has to have a length of <see langword="40"/>.</remarks>
    /// <param name="hash">The hash text.</param>
    public unsafe Sha1(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        if (hash.Length != 40)
            throw new ArgumentException($"{nameof(Sha1)} must have a length of 40 characters.", nameof(hash));

        var span = hash.AsSpan();
        uint* comps = stackalloc uint[5];

        int offset = 0;
        for (int i = 0; i < 5; i++)
        {
            byte b0 = byte.Parse(span.Slice(offset + 0, 2), NumberStyles.AllowHexSpecifier);
            byte b1 = byte.Parse(span.Slice(offset + 2, 2), NumberStyles.AllowHexSpecifier);
            byte b2 = byte.Parse(span.Slice(offset + 4, 2), NumberStyles.AllowHexSpecifier);
            byte b3 = byte.Parse(span.Slice(offset + 6, 2), NumberStyles.AllowHexSpecifier);

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
    /// Compare <paramref name="a" /> to <paramref name="b" />.
    /// </summary>
    /// <param name="a">The left hash.</param>
    /// <param name="b">The right hash.</param>
    /// <returns>Whether both hashes aren't equal to each other.</returns>
    public static bool operator !=(Sha1 a, Sha1 b)
    {
        return !(a == b);
    }

    /// <summary>
    /// Compare <paramref name="a" /> to <paramref name="b" />
    /// </summary>
    /// <param name="a">The left hash.</param>
    /// <param name="b">The right hash.</param>
    /// <returns>Whether both hashes are equal to each other.</returns>
    public static bool operator ==(Sha1 a, Sha1 b)
    {
        return a.Equals(b);
    }

    /// <inheritdoc />
    public bool Equals(Sha1 other)
    {
        return _a == other._a && _b == other._b && _c == other._c && _d == other._d;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Sha1 other && other.Equals(this);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_a, _b, _c, _d, _e);
    }

    /// <summary>
    /// Returns the <see cref="Sha1" /> value as a byte array.
    /// </summary>
    /// <returns>The <see cref="Sha1" /> value as a byte array.</returns>
    public byte[] GetHashBuffer()
    {
        // NOTE(VNC:)
        // Take all hash components and turn their 32 bits into smaller 8 bit parts.

        return new byte[]
        {
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
        };
    }

    /// <summary>
    /// Returns the <see cref="Sha1" /> hash as a string.
    /// </summary>
    /// <returns>The <see cref="Sha1" /> hash as a string value.</returns>
    public override unsafe string ToString()
    {
        // NOTE(VNC): The length will be 2 chars * (5*4) components.
        var output = new StringBuilder(10);

        uint* components = stackalloc uint[5];
        components[0] = _a;
        components[1] = _b;
        components[2] = _c;
        components[3] = _d;
        components[4] = _e;

        for (int i = 0; i < 5; i++)
        {
            output.Append(string.Concat(
                ((byte)(components[i] & 0xFF)).ToString("X2"),
                ((byte)(components[i] >> 0x8)).ToString("X2"),
                ((byte)(components[i] >> 0x10)).ToString("X2"),
                ((byte)(components[i] >> 0x18)).ToString("X2")
            ));
        }

        return output.ToString();
    }
}
