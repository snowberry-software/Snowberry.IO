using System.Buffers.Binary;
using System.Text;
using Snowberry.IO.Common;

namespace Snowberry.IO.Benchmarks.Infrastructure;

/// <summary>
/// Creates deterministic benchmark inputs and temporary directories.
/// </summary>
internal static class BenchmarkData
{
    /// <summary>
    /// The number of random bits that select a slot of <see cref="s_CodeByteTable"/>.
    /// </summary>
    private const int c_CodeTableBits = 10;

    private const string c_TextAlphabet = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 0123456789 äöüéßñ";

    /// <summary>
    /// The bytes that <see cref="CodeBytes"/> picks from: 1024 slots, of which the bytes that are frequent in x64 machine code fill several.
    /// </summary>
    private static readonly byte[] s_CodeByteTable = CreateCodeByteTable();

    /// <summary>
    /// Creates a buffer of pseudo-random bytes with the skewed distribution of x64 machine code, where for example <c>0x00</c>, <c>0x48</c>,
    /// <c>0x8B</c> and <c>0xE8</c> are much more frequent than other bytes.
    /// </summary>
    /// <remarks>The bytes come from a SplitMix64 generator instead of <see cref="Random"/>, so they are the same on every runtime.</remarks>
    /// <param name="length">The number of bytes.</param>
    /// <param name="seed">The seed of the random generator.</param>
    /// <returns>The filled buffer.</returns>
    public static byte[] CodeBytes(int length, ulong seed = 42)
    {
        const int c_BytesPerValue = 64 / c_CodeTableBits;
        const ulong c_SlotMask = (1 << c_CodeTableBits) - 1;

        byte[] data = new byte[length];
        ulong state = seed;

        for (int i = 0; i < length;)
        {
            ulong value = NextSplitMix64(ref state);

            for (int j = 0; j < c_BytesPerValue && i < length; j++, i++)
            {
                data[i] = s_CodeByteTable[(int)(value & c_SlotMask)];
                value >>= c_CodeTableBits;
            }
        }

        return data;
    }

    /// <summary>
    /// Creates a new, empty directory in the temporary directory.
    /// </summary>
    /// <param name="name">A name that identifies the benchmark that uses the directory.</param>
    /// <returns>The full path of the created directory.</returns>
    public static string CreateTempDirectory(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "snowberry-bench-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes a directory and its contents, ignoring a directory that does not exist.
    /// </summary>
    /// <param name="path">The directory to delete, or <see langword="null"/> to do nothing.</param>
    public static void DeleteDirectory(string? path)
    {
        if (path != null && Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Creates a buffer of pseudo-random bytes that is the same on every run.
    /// </summary>
    /// <param name="length">The number of bytes.</param>
    /// <param name="seed">The seed of the random generator.</param>
    /// <returns>The filled buffer.</returns>
    public static byte[] RandomBytes(int length, int seed = 42)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>
    /// Creates a text of letters, digits, spaces and a few non-ASCII letters that is the same on every run.
    /// </summary>
    /// <remarks>The text contains no line breaks and no null characters.</remarks>
    /// <param name="length">The number of characters.</param>
    /// <param name="seed">The seed of the random generator.</param>
    /// <returns>The text.</returns>
    public static string RandomText(int length, int seed = 42)
    {
        var random = new Random(seed);
        var builder = new StringBuilder(length);

        for (int i = 0; i < length; i++)
            builder.Append(c_TextAlphabet[random.Next(c_TextAlphabet.Length)]);

        return builder.ToString();
    }

    /// <summary>
    /// Reads a <see cref="Guid"/> from the first 16 bytes of <paramref name="data"/> with the BCL, using the same layout as
    /// <see cref="BinaryEndianConverter.ToGuid(ReadOnlySpan{byte}, int, EndianType)"/>.
    /// </summary>
    /// <param name="data">The source data. Must be at least 16 bytes long.</param>
    /// <param name="endian">The byte order of the first three components.</param>
    /// <returns>The read <see cref="Guid"/>.</returns>
    public static Guid ReadGuid(ReadOnlySpan<byte> data, EndianType endian)
    {
#if NET8_0_OR_GREATER
        return new Guid(data[..16], endian == EndianType.BIG);
#else
        bool little = endian == EndianType.LITTLE;
        int a = little ? BinaryPrimitives.ReadInt32LittleEndian(data) : BinaryPrimitives.ReadInt32BigEndian(data);
        short b = little ? BinaryPrimitives.ReadInt16LittleEndian(data[4..]) : BinaryPrimitives.ReadInt16BigEndian(data[4..]);
        short c = little ? BinaryPrimitives.ReadInt16LittleEndian(data[6..]) : BinaryPrimitives.ReadInt16BigEndian(data[6..]);
        return new Guid(a, b, c, data[8], data[9], data[10], data[11], data[12], data[13], data[14], data[15]);
#endif
    }

    private static byte[] CreateCodeByteTable()
    {
        // The most frequent bytes of x64 machine code (zero, REX.W, MOV, CALL, ...) and the slots they fill.
        (byte Value, int Slots)[] frequent =
        [
            (0x00, 160), (0x48, 80), (0x8B, 80), (0x89, 48), (0xFF, 40), (0xE8, 32), (0x0F, 24),
            (0x83, 24), (0x4C, 24), (0x24, 16), (0x44, 16), (0xCC, 16), (0xC3, 8)
        ];

        byte[] table = new byte[1 << c_CodeTableBits];
        int index = 0;

        foreach (var entry in frequent)
        {
            for (int i = 0; i < entry.Slots; i++)
                table[index++] = entry.Value;
        }

        // The remaining slots hold every byte value once or twice.
        for (int value = 0; index < table.Length; value++)
            table[index++] = (byte)value;

        return table;
    }

    private static ulong NextSplitMix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EB;
        return value ^ (value >> 31);
    }
}