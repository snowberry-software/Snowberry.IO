using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.Extensions;
using Snowberry.IO.Reader;

namespace Snowberry.IO.Benchmarks.Reader;

/// <summary>
/// Measures <see cref="ReaderExtensions.ScanSignatures"/> over a <see cref="MemoryStream"/> with an exact pattern, a pattern with wildcards
/// and a pattern whose leading known run is a single byte, against matching the same bytes in memory.
/// </summary>
/// <remarks>
/// The exact pattern is matched with repeated <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/> calls; the other
/// patterns with <see cref="SignaturePattern.Count"/>, which uses the algorithm of the reader in memory.
/// </remarks>
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ScanSignatureBenchmarks
{
    private static readonly SignaturePattern s_Exact = new("48 8B 05 1D 2C 3B 4A 59");
    private static readonly byte[] s_Signature = [0x48, 0x8B, 0x05, 0x1D, 0x2C, 0x3B, 0x4A, 0x59];
    private static readonly SignaturePattern s_SingleBytePrefix = new("E8 ?? ?? ?? ?? 48 8B D8");
    private static readonly SignaturePattern s_Wildcard = new("48 8B 05 ?? ?? ?? ?? 59");

    private byte[] _data = [];
    private EndianStreamReader _reader = null!;

    /// <summary>
    /// Closes the reader.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _reader.Dispose();
    }

    /// <summary>
    /// Counts the signature whose leading known run is a single byte in memory.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SingleBytePrefix")]
    public int ReferenceSingleBytePrefix()
    {
        return s_SingleBytePrefix.Count(_data);
    }

    /// <summary>
    /// Counts the signature with four wildcard bytes in memory.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Wildcard")]
    public int ReferenceWildcard()
    {
        return s_Wildcard.Count(_data);
    }

    /// <summary>
    /// Scans for the exact signature with <see cref="ReaderExtensions.ScanSignatures"/>.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    [BenchmarkCategory("Exact")]
    public int ScanExact()
    {
        return Scan(s_Exact);
    }

    /// <summary>
    /// Scans for the signature whose leading known run is a single byte with <see cref="ReaderExtensions.ScanSignatures"/>.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    [BenchmarkCategory("SingleBytePrefix")]
    public int ScanSingleBytePrefix()
    {
        return Scan(s_SingleBytePrefix);
    }

    /// <summary>
    /// Scans for the signature with four wildcard bytes with <see cref="ReaderExtensions.ScanSignatures"/>.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    [BenchmarkCategory("Wildcard")]
    public int ScanWildcard()
    {
        return Scan(s_Wildcard);
    }

    /// <summary>
    /// Creates the data with each pattern at its start, middle and end, opens a reader over it and checks that the reader finds the same
    /// matches as the in-memory methods.
    /// </summary>
    /// <exception cref="InvalidOperationException">The methods find a different number of matches.</exception>
    [GlobalSetup]
    public void Setup()
    {
        int size = SizeKiB * 1024;
        _data = Data == DataKind.Random ? BenchmarkData.RandomBytes(size) : BenchmarkData.CodeBytes(size);

        foreach (int offset in new[] { 16, size / 2, size - 64 })
        {
            s_Exact.WriteTo(_data, offset);
            s_Wildcard.WriteTo(_data, offset + 16);
            s_SingleBytePrefix.WriteTo(_data, offset + 32);
        }

        _reader = new EndianStreamReader(new MemoryStream(_data, writable: false));

        Verify(nameof(ScanExact), SpanIndexOf(), ScanExact());
        Verify(nameof(ScanWildcard), ReferenceWildcard(), ScanWildcard());
        Verify(nameof(ScanSingleBytePrefix), ReferenceSingleBytePrefix(), ScanSingleBytePrefix());
    }

    /// <summary>
    /// Counts the exact signature with <see cref="MemoryExtensions.IndexOf{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>.
    /// </summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Exact")]
    public int SpanIndexOf()
    {
        ReadOnlySpan<byte> remaining = _data;
        ReadOnlySpan<byte> signature = s_Signature;
        int count = 0;

        while (true)
        {
            int index = remaining.IndexOf(signature);

            if (index < 0)
                return count;

            count++;
            remaining = remaining[(index + signature.Length)..];
        }
    }

    private int Scan(SignaturePattern pattern)
    {
        _reader.Position = 0;
        return _reader.ScanSignatures(pattern.Text, int.MaxValue, long.MaxValue).Count;
    }

    private void Verify(string method, int expected, int actual)
    {
        if (expected < 3 || actual != expected)
            throw new InvalidOperationException($"{method} found {actual} matches instead of {expected} ({SizeKiB} KiB, {Data}).");
    }

    /// <summary>
    /// Gets or sets the kind of scanned data.
    /// </summary>
    [Params(DataKind.Random, DataKind.Code)]
    public DataKind Data { get; set; }

    /// <summary>
    /// Gets or sets the size of the scanned data in kibibytes.
    /// </summary>
    [Params(4, 1024, 64 * 1024)]
    public int SizeKiB { get; set; }

    /// <summary>
    /// The kinds of scanned data.
    /// </summary>
    public enum DataKind
    {
        /// <summary>
        /// Uniformly distributed random bytes.
        /// </summary>
        Random,

        /// <summary>
        /// Random bytes with the distribution of x64 machine code, see <see cref="BenchmarkData.CodeBytes"/>.
        /// </summary>
        Code
    }
}