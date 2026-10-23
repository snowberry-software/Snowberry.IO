using System.Globalization;
using Snowberry.IO.Extensions;

namespace Snowberry.IO.Benchmarks.Infrastructure;

/// <summary>
/// A byte signature in the format of <see cref="ReaderExtensions.ScanSignatures"/>, such as <c>48 8B 05 ?? ?? ?? ?? 59</c>, with a
/// reference matcher over memory.
/// </summary>
internal sealed class SignaturePattern
{
    private readonly bool[] _isWildcard;
    private readonly int _prefixLength;
    private readonly byte[] _values;

    /// <summary>
    /// Parses <paramref name="pattern"/>.
    /// </summary>
    /// <param name="pattern">Hexadecimal bytes and <c>??</c> wildcards separated by spaces; the first byte must be known.</param>
    /// <exception cref="ArgumentException"><paramref name="pattern"/> starts with a wildcard.</exception>
    public SignaturePattern(string pattern)
    {
        string[] parts = pattern.Split([' '], StringSplitOptions.RemoveEmptyEntries);

        Text = pattern;
        _values = new byte[parts.Length];
        _isWildcard = new bool[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            _isWildcard[i] = parts[i] == "??";
            _values[i] = _isWildcard[i] ? (byte)0 : byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        if (_isWildcard[0])
            throw new ArgumentException("The pattern must start with a known byte.", nameof(pattern));

        while (_prefixLength < _values.Length && !_isWildcard[_prefixLength])
            _prefixLength++;
    }

    /// <summary>
    /// Counts the non-overlapping matches in <paramref name="data"/> the way <see cref="ReaderExtensions.ScanSignatures"/> does: the leading
    /// known bytes are located with <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/> and the remaining bytes
    /// are compared one by one.
    /// </summary>
    /// <param name="data">The data to search.</param>
    /// <returns>The number of matches.</returns>
    public int Count(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> prefix = _values.AsSpan(0, _prefixLength);
        int last = data.Length - _values.Length;
        int count = 0;
        int i = 0;

        while (i <= last)
        {
            int index = data.Slice(i, last - i + _prefixLength).IndexOf(prefix);

            if (index < 0)
                break;

            i += index;

            if (MatchesRest(data, i))
            {
                count++;
                i += _values.Length;
            }
            else
            {
                i++;
            }
        }

        return count;
    }

    /// <summary>
    /// Writes bytes that match the pattern into <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The buffer to write to.</param>
    /// <param name="offset">The offset in <paramref name="data"/> of the first byte.</param>
    /// <param name="wildcardValue">The byte written in place of every wildcard.</param>
    public void WriteTo(byte[] data, int offset, byte wildcardValue = 0x11)
    {
        for (int i = 0; i < _values.Length; i++)
            data[offset + i] = _isWildcard[i] ? wildcardValue : _values[i];
    }

    private bool MatchesRest(ReadOnlySpan<byte> data, int start)
    {
        for (int j = _prefixLength; j < _values.Length; j++)
        {
            if (!_isWildcard[j] && data[start + j] != _values[j])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the number of bytes the pattern matches.
    /// </summary>
    public int Length => _values.Length;

    /// <summary>
    /// Gets the pattern text.
    /// </summary>
    public string Text { get; }
}