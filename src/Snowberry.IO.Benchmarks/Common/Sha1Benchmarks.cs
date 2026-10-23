using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Common;

namespace Snowberry.IO.Benchmarks.Common;

/// <summary>
/// Measures parsing, formatting, creating and comparing <see cref="Sha1"/> values.
/// </summary>
/// <remarks>
/// <para>Parsing and formatting are compared against a simple hexadecimal conversion written here, so the ratio is relative to the same
/// code on every runtime.</para>
/// <para>Creating, comparing and hashing take about a nanosecond each, so those benchmarks loop over an array of hashes.</para>
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class Sha1Benchmarks
{
    private const string c_Hash = "2FD4E1C67A2D28FCED849EE1BB76E7391B93EB12";
    private const int c_HashCount = 1024;
    private const string c_HexDigits = "0123456789ABCDEF";
    private byte[] _equalHashBuffer = [];
    private Sha1[] _equalHashes = [];

    private Sha1 _hash;
    private byte[] _hashBuffer = [];
    private byte[] _hashBytes = [];
    private Sha1[] _hashes = [];
    private byte[] _otherHashBuffer = [];
    private Sha1[] _otherHashes = [];

    private static int CountEqualHashes(Sha1[] left, Sha1[] right)
    {
        int count = 0;

        for (int i = 0; i < left.Length; i++)
        {
            if (left[i].Equals(right[i]))
                count++;
        }

        return count;
    }

    private static int CountEqualSequences(byte[] left, byte[] right)
    {
        ReadOnlySpan<byte> a = left;
        ReadOnlySpan<byte> b = right;
        int count = 0;

        for (int offset = 0; offset < a.Length; offset += Sha1.c_StructSize)
        {
            if (a.Slice(offset, Sha1.c_StructSize).SequenceEqual(b.Slice(offset, Sha1.c_StructSize)))
                count++;
        }

        return count;
    }

    private static Sha1[] CreateHashes(byte[] buffer)
    {
        var hashes = new Sha1[buffer.Length / Sha1.c_StructSize];

        for (int i = 0; i < hashes.Length; i++)
            hashes[i] = new Sha1(buffer.AsSpan(i * Sha1.c_StructSize, Sha1.c_StructSize));

        return hashes;
    }

    private static Sha1 FromBytes(ReadOnlySpan<byte> bytes)
    {
        return new Sha1(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]));
    }

    private static int HexValue(char c)
    {
        if (c is >= '0' and <= '9')
            return c - '0';

        int lower = c | 0x20;

        if (lower is >= 'a' and <= 'f')
            return lower - 'a' + 10;

        throw new FormatException($"'{c}' is not a hexadecimal digit.");
    }

    /// <summary>
    /// Compares equal hash bytes with <see cref="MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>.
    /// </summary>
    /// <returns>The number of equal pairs.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_HashCount)]
    [BenchmarkCategory("Equals")]
    public int EqualsSequence()
    {
        return CountEqualSequences(_hashBuffer, _equalHashBuffer);
    }

    /// <summary>
    /// Compares equal hashes with <see cref="Sha1.Equals(Sha1)"/>.
    /// </summary>
    /// <returns>The number of equal pairs.</returns>
    [Benchmark(OperationsPerInvoke = c_HashCount)]
    [BenchmarkCategory("Equals")]
    public int EqualsSha1()
    {
        return CountEqualHashes(_hashes, _equalHashes);
    }

    /// <summary>
    /// Formats the hash with <see cref="Sha1.ToString"/>.
    /// </summary>
    /// <returns>The hexadecimal hash.</returns>
    [Benchmark]
    [BenchmarkCategory("Format")]
    public string Format()
    {
        return _hash.ToString();
    }

    /// <summary>
    /// Formats the hash bytes as uppercase hexadecimal digits with a lookup string.
    /// </summary>
    /// <returns>The hexadecimal hash.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Format")]
    public unsafe string FormatHex()
    {
        byte[] bytes = _hashBytes;
        char* chars = stackalloc char[Sha1.c_StructSize * 2];

        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 2] = c_HexDigits[bytes[i] >> 4];
            chars[(i * 2) + 1] = c_HexDigits[bytes[i] & 0xF];
        }

        return new string(chars, 0, Sha1.c_StructSize * 2);
    }

    /// <summary>
    /// Creates hashes from 20-byte slices with five <see cref="BinaryPrimitives.ReadUInt32LittleEndian(ReadOnlySpan{byte})"/> calls and the
    /// <see cref="Sha1(uint, uint, uint, uint, uint)"/> constructor.
    /// </summary>
    /// <returns>The sum of the hash codes of the created hashes.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_HashCount)]
    [BenchmarkCategory("FromSpan")]
    public long FromComponents()
    {
        ReadOnlySpan<byte> buffer = _hashBuffer;
        long sum = 0;

        for (int i = 0; i < c_HashCount; i++)
            sum += FromBytes(buffer.Slice(i * Sha1.c_StructSize, Sha1.c_StructSize)).GetHashCode();

        return sum;
    }

    /// <summary>
    /// Creates hashes from 20-byte slices with the <see cref="Sha1(ReadOnlySpan{byte})"/> constructor.
    /// </summary>
    /// <returns>The sum of the hash codes of the created hashes.</returns>
    [Benchmark(OperationsPerInvoke = c_HashCount)]
    [BenchmarkCategory("FromSpan")]
    public long FromSpan()
    {
        ReadOnlySpan<byte> buffer = _hashBuffer;
        long sum = 0;

        for (int i = 0; i < c_HashCount; i++)
            sum += new Sha1(buffer.Slice(i * Sha1.c_StructSize, Sha1.c_StructSize)).GetHashCode();

        return sum;
    }

    /// <summary>
    /// Computes the hash codes with <see cref="Sha1.GetHashCode"/>.
    /// </summary>
    /// <returns>The sum of the hash codes.</returns>
    [Benchmark(OperationsPerInvoke = c_HashCount)]
    [BenchmarkCategory("GetHashCode")]
    public long HashCode()
    {
        var hashes = _hashes;
        long sum = 0;

        for (int i = 0; i < hashes.Length; i++)
            sum += hashes[i].GetHashCode();

        return sum;
    }

    /// <summary>
    /// Compares different hash bytes with <see cref="MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>.
    /// </summary>
    /// <returns>The number of equal pairs.</returns>
    [Benchmark(Baseline = true, OperationsPerInvoke = c_HashCount)]
    [BenchmarkCategory("NotEquals")]
    public int NotEqualsSequence()
    {
        return CountEqualSequences(_hashBuffer, _otherHashBuffer);
    }

    /// <summary>
    /// Compares different hashes with <see cref="Sha1.Equals(Sha1)"/>.
    /// </summary>
    /// <returns>The number of equal pairs.</returns>
    [Benchmark(OperationsPerInvoke = c_HashCount)]
    [BenchmarkCategory("NotEquals")]
    public int NotEqualsSha1()
    {
        return CountEqualHashes(_hashes, _otherHashes);
    }

    /// <summary>
    /// Parses the hexadecimal hash with the <see cref="Sha1(string)"/> constructor.
    /// </summary>
    /// <returns>The parsed hash.</returns>
    [Benchmark]
    [BenchmarkCategory("Parse")]
    public Sha1 Parse()
    {
        return new Sha1(c_Hash);
    }

    /// <summary>
    /// Parses the hexadecimal hash with a lookup-free conversion of each digit pair and the <see cref="Sha1(uint, uint, uint, uint, uint)"/>
    /// constructor.
    /// </summary>
    /// <returns>The parsed hash.</returns>
    /// <exception cref="FormatException">The hash contains a character that is not a hexadecimal digit.</exception>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Parse")]
    public Sha1 ParseHex()
    {
        Span<byte> bytes = stackalloc byte[Sha1.c_StructSize];

        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((HexValue(c_Hash[i * 2]) << 4) | HexValue(c_Hash[(i * 2) + 1]));

        return FromBytes(bytes);
    }

    /// <summary>
    /// Creates the hashes that are formatted, created and compared, and checks that the baselines produce the same results as <see cref="Sha1"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A baseline does not produce the same result as <see cref="Sha1"/>.</exception>
    [GlobalSetup]
    public void Setup()
    {
        _hash = new Sha1(c_Hash);
        _hashBytes = _hash.GetHashBuffer();

        _hashBuffer = BenchmarkData.RandomBytes(c_HashCount * Sha1.c_StructSize, seed: 1);
        _equalHashBuffer = (byte[])_hashBuffer.Clone();
        _otherHashBuffer = BenchmarkData.RandomBytes(c_HashCount * Sha1.c_StructSize, seed: 2);
        _hashes = CreateHashes(_hashBuffer);
        _equalHashes = CreateHashes(_equalHashBuffer);
        _otherHashes = CreateHashes(_otherHashBuffer);

        if (ParseHex() != Parse() || FormatHex() != Format() || FromComponents() != FromSpan())
            throw new InvalidOperationException("A baseline did not produce the same result as Sha1.");

        if (EqualsSequence() != c_HashCount || EqualsSha1() != c_HashCount || NotEqualsSequence() != 0 || NotEqualsSha1() != 0)
            throw new InvalidOperationException("The equality checks did not produce the expected results.");
    }
}