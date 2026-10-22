using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Snowberry.IO.Benchmarks.Infrastructure;
using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Benchmarks.SingleFile;

/// <summary>
/// Writes synthetic single-file bundles: a Windows application host followed by the file data and a format version 6 manifest of a
/// self-contained application.
/// </summary>
/// <remarks>
/// The assemblies are named <c>lib/AssemblyNNNN.bin</c>, so extracting them does not create <c>.dll</c> files that antivirus software
/// scans more thoroughly.
/// </remarks>
internal static class SyntheticBundle
{
    /// <summary>
    /// The file name the benchmarks give a bundle; the <c>.bin</c> extension keeps antivirus software from treating it as an executable.
    /// </summary>
    public const string c_FileName = "App.bin";

    private const int c_AssemblyAlignment = 4096;
    private const int c_LargeDataSectionMarkerOffset = 0x2000;
    private const int c_LargeHeadersSize = 0x400;

    private const int c_LargeHostLength = 10 * 1024 * 1024;

    private const uint c_MajorVersion = 6;
    private const int c_PeHeaderOffset = 0x80;
    private const int c_PeOptionalHeaderOffset = c_PeHeaderOffset + 24;
    private const int c_PeOptionalHeaderSize = 0xF0;
    private const int c_PeSectionHeaderSize = 40;

    private const string c_RuntimeConfig =
        """{"runtimeOptions":{"tfm":"net10.0","includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}""";

    private const uint c_ScnCode = 0x00000020;
    private const uint c_ScnInitializedData = 0x00000040;
    private const uint c_ScnMemDiscardable = 0x02000000;
    private const uint c_ScnMemExecute = 0x20000000;
    private const uint c_ScnMemRead = 0x40000000;
    private const uint c_ScnMemWrite = 0x80000000;

    private const int c_SmallHostLength = 1024;
    private const int c_SmallManifestOffsetField = 900;
    private const int c_SmallPeImageEnd = 400;

    private static readonly byte[] s_BundleSignature =
    [
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    ];

    private static readonly string[] s_Words =
    [
        "System", "Runtime", "Collections", "Generic", "List", "Dictionary", "String", "Int32", "Object", "Task", "Async", "Await",
        "public", "static", "class", "void", "return", "namespace", "using", "private", "readonly", "struct", "Span", "Memory"
    ];

    /// <summary>
    /// The sections of the large host: name, size of the raw data and characteristics. The <c>.data</c> section holds the bundle marker.
    /// </summary>
    private static readonly (string Name, int Size, uint Characteristics)[] s_LargeSections =
    [
        (".text", (7 * 1024 * 1024) - c_LargeHeadersSize, c_ScnCode | c_ScnMemExecute | c_ScnMemRead),
        (".rdata", (2 * 1024 * 1024) + (256 * 1024), c_ScnInitializedData | c_ScnMemRead),
        (".data", 128 * 1024, c_ScnInitializedData | c_ScnMemRead | c_ScnMemWrite),
        (".pdata", 512 * 1024, c_ScnInitializedData | c_ScnMemRead),
        (".reloc", 128 * 1024, c_ScnInitializedData | c_ScnMemDiscardable | c_ScnMemRead)
    ];


    /// <summary>
    /// Creates content of random words from a small vocabulary, which deflate compresses to well below the 0.75 threshold of
    /// <see cref="BundleWriteOptions.CompressionThreshold"/>.
    /// </summary>
    /// <param name="size">The number of bytes.</param>
    /// <param name="seed">The seed of the random generator.</param>
    /// <returns>The ASCII content.</returns>
    public static byte[] CreateContent(int size, int seed)
    {
        var random = new Random(seed);
        byte[] content = new byte[size];

        for (int i = 0; i < size;)
        {
            string word = s_Words[random.Next(s_Words.Length)];

            for (int j = 0; j < word.Length && i < size; j++)
                content[i++] = (byte)word[j];

            if (i < size)
                content[i++] = (byte)' ';
        }

        return content;
    }

    /// <summary>
    /// Returns the relative path of the assembly entry at <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The zero-based index of the assembly.</param>
    /// <returns>The relative path of the entry.</returns>
    public static string GetAssemblyPath(int index)
    {
        return $"lib/Assembly{index:D4}.bin";
    }

    /// <summary>
    /// Returns the uncompressed sizes of the assembly entries of <paramref name="layout"/>.
    /// </summary>
    /// <param name="layout">The layout of the entries.</param>
    /// <returns>The size of each assembly entry, in bytes, in the order of the manifest.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="layout"/> is not a defined value.</exception>
    public static int[] GetAssemblySizes(EntryLayout layout)
    {
        return layout switch
        {
            EntryLayout.Uniform => Enumerable.Repeat(256 * 1024, 64).ToArray(),
            EntryLayout.ManySmall => Enumerable.Range(0, 175).Select(i => (8 * 1024) << (i % 5)).ToArray(),
            EntryLayout.Skewed => [.. Enumerable.Repeat(64 * 1024, 128), 16 * 1024 * 1024],
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown entry layout.")
        };
    }

    /// <summary>
    /// Writes a bundle with <paramref name="assemblyCount"/> assemblies of the same size to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file to create or overwrite.</param>
    /// <param name="assemblyCount">The number of assembly entries.</param>
    /// <param name="assemblySize">The uncompressed size of each assembly entry, in bytes.</param>
    /// <param name="compress"><see langword="true"/> to store the assemblies deflate-compressed; otherwise, <see langword="false"/>.</param>
    /// <param name="hostSize">The application host that precedes the bundle.</param>
    public static void Write(string path, int assemblyCount, int assemblySize, bool compress, HostSize hostSize = HostSize.Small)
    {
        Write(path, Enumerable.Repeat(assemblySize, assemblyCount).ToArray(), compress, hostSize);
    }

    /// <summary>
    /// Writes a bundle with a <c>deps.json</c>, a <c>runtimeconfig.json</c> and one assembly of compressible text per element of
    /// <paramref name="assemblySizes"/> to <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The file to create or overwrite.</param>
    /// <param name="assemblySizes">The uncompressed size of each assembly entry, in bytes.</param>
    /// <param name="compress"><see langword="true"/> to store the assemblies deflate-compressed; otherwise, <see langword="false"/>.</param>
    /// <param name="hostSize">The application host that precedes the bundle.</param>
    public static void Write(string path, IReadOnlyList<int> assemblySizes, bool compress, HostSize hostSize = HostSize.Small)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        byte[] host = hostSize == HostSize.Small ? CreateSmallHost(out int manifestOffsetField) : CreateLargeHost(out manifestOffsetField);
        writer.Write(host);

        var entries = new List<Entry>(assemblySizes.Count + 2)
        {
            WriteFile(writer, "App.deps.json", BundleFileType.DepsJson, Encoding.UTF8.GetBytes("{}"), compress: false),
            WriteFile(writer, "App.runtimeconfig.json", BundleFileType.RuntimeConfigJson, Encoding.UTF8.GetBytes(c_RuntimeConfig), compress: false)
        };

        for (int i = 0; i < assemblySizes.Count; i++)
            entries.Add(WriteFile(writer, GetAssemblyPath(i), BundleFileType.Assembly, CreateContent(assemblySizes[i], seed: i), compress));

        WriteManifest(writer, entries, manifestOffsetField);
    }

    /// <summary>
    /// Creates a 10 MiB PE32+ image whose sections, contents and bundle marker location resemble the <c>singlefilehost</c> of the .NET
    /// runtime: the marker, the manifest offset followed by the bundle signature, lies in the writable <c>.data</c> section, about 92% into
    /// the file, so locating it reads almost the whole host.
    /// </summary>
    private static byte[] CreateLargeHost(out int manifestOffsetField)
    {
        const int c_FileAlignment = 0x200;
        const int c_SectionAlignment = 0x1000;

        byte[] host = new byte[c_LargeHostLength];
        var span = host.AsSpan();

        WritePeHeaders(span, machine: 0x8664, sectionCount: s_LargeSections.Length);

        int optionalHeader = c_PeOptionalHeaderOffset;
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 16)..], c_SectionAlignment);   // AddressOfEntryPoint
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 20)..], c_SectionAlignment);   // BaseOfCode
        BinaryPrimitives.WriteUInt64LittleEndian(span[(optionalHeader + 24)..], 0x140000000);          // ImageBase
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 32)..], c_SectionAlignment);   // SectionAlignment
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 36)..], c_FileAlignment);      // FileAlignment
        BinaryPrimitives.WriteUInt16LittleEndian(span[(optionalHeader + 40)..], 6);                    // MajorOperatingSystemVersion
        BinaryPrimitives.WriteUInt16LittleEndian(span[(optionalHeader + 48)..], 6);                    // MajorSubsystemVersion
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 60)..], c_LargeHeadersSize);   // SizeOfHeaders
        BinaryPrimitives.WriteUInt16LittleEndian(span[(optionalHeader + 68)..], 3);                    // Subsystem: Windows CUI
        BinaryPrimitives.WriteUInt16LittleEndian(span[(optionalHeader + 70)..], 0x8160);               // DllCharacteristics
        BinaryPrimitives.WriteUInt64LittleEndian(span[(optionalHeader + 72)..], 0x180000);             // SizeOfStackReserve
        BinaryPrimitives.WriteUInt64LittleEndian(span[(optionalHeader + 80)..], 0x1000);               // SizeOfStackCommit
        BinaryPrimitives.WriteUInt64LittleEndian(span[(optionalHeader + 88)..], 0x100000);             // SizeOfHeapReserve
        BinaryPrimitives.WriteUInt64LittleEndian(span[(optionalHeader + 96)..], 0x1000);               // SizeOfHeapCommit
        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 108)..], 16);                  // NumberOfRvaAndSizes

        int sectionHeader = c_PeOptionalHeaderOffset + c_PeOptionalHeaderSize;
        int rawOffset = c_LargeHeadersSize;
        int virtualAddress = c_SectionAlignment;
        manifestOffsetField = 0;

        foreach (var section in s_LargeSections)
        {
            var header = span.Slice(sectionHeader, c_PeSectionHeaderSize);
            Encoding.ASCII.GetBytes(section.Name).CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)section.Size);                 // VirtualSize
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)virtualAddress);              // VirtualAddress
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], (uint)section.Size);                // SizeOfRawData
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)rawOffset);                   // PointerToRawData
            BinaryPrimitives.WriteUInt32LittleEndian(header[36..], section.Characteristics);           // Characteristics

            var data = span.Slice(rawOffset, section.Size);

            if (section.Name == ".text")
            {
                BenchmarkData.CodeBytes(section.Size).CopyTo(data);
            }
            else if (section.Name == ".data")
            {
                // Mostly zero-initialized globals, with the bundle marker among them.
                BenchmarkData.RandomBytes(section.Size / 8, seed: rawOffset).CopyTo(data);
                manifestOffsetField = rawOffset + c_LargeDataSectionMarkerOffset;
                host.AsSpan(manifestOffsetField, sizeof(long)).Clear();
                s_BundleSignature.CopyTo(host, manifestOffsetField + sizeof(long));
            }
            else
            {
                BenchmarkData.RandomBytes(section.Size, seed: rawOffset).CopyTo(data);
            }

            sectionHeader += c_PeSectionHeaderSize;
            rawOffset += section.Size;
            virtualAddress += (section.Size + c_SectionAlignment - 1) / c_SectionAlignment * c_SectionAlignment;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span[(optionalHeader + 56)..], (uint)virtualAddress); // SizeOfImage

        if (rawOffset != c_LargeHostLength)
            throw new InvalidOperationException($"The sections of the large host end at {rawOffset} instead of {c_LargeHostLength}.");

        return host;
    }

    private static byte[] CreateSmallHost(out int manifestOffsetField)
    {
        byte[] host = new byte[c_SmallHostLength];
        var span = host.AsSpan();

        WritePeHeaders(span, machine: 0, sectionCount: 1);
        int section = c_PeOptionalHeaderOffset + c_PeOptionalHeaderSize;
        BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 16)..], 100);                     // SizeOfRawData
        BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 20)..], c_SmallPeImageEnd - 100); // PointerToRawData

        manifestOffsetField = c_SmallManifestOffsetField;
        s_BundleSignature.CopyTo(host, manifestOffsetField + sizeof(long));
        return host;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var compressed = new MemoryStream();

        using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(data);

        return compressed.ToArray();
    }

    private static Entry WriteFile(BinaryWriter writer, string relativePath, BundleFileType type, byte[] content, bool compress)
    {
        if (type == BundleFileType.Assembly && !compress)
            writer.Write(new byte[(int)((c_AssemblyAlignment - (writer.BaseStream.Position % c_AssemblyAlignment)) % c_AssemblyAlignment)]);

        long offset = writer.BaseStream.Position;
        byte[] stored = compress ? Deflate(content) : content;
        writer.Write(stored);
        return new Entry(offset, content.Length, compress ? stored.Length : 0, type, relativePath);
    }

    private static void WriteManifest(BinaryWriter writer, List<Entry> entries, int manifestOffsetField)
    {
        long manifestOffset = writer.BaseStream.Position;

        writer.Write(c_MajorVersion);
        writer.Write(0u);
        writer.Write(entries.Count);
        writer.Write("benchmark000");

        foreach (var type in new[] { BundleFileType.DepsJson, BundleFileType.RuntimeConfigJson })
        {
            var location = entries.Find(e => e.Type == type);
            writer.Write(location.Offset);
            writer.Write(location.Size);
        }

        writer.Write((ulong)BundleFlags.None);

        foreach (var entry in entries)
        {
            writer.Write(entry.Offset);
            writer.Write(entry.Size);
            writer.Write(entry.CompressedSize);
            writer.Write((byte)entry.Type);
            writer.Write(entry.RelativePath);
        }

        writer.BaseStream.Position = manifestOffsetField;
        writer.Write(manifestOffset);
    }

    private static void WritePeHeaders(Span<byte> host, ushort machine, int sectionCount)
    {
        host[0] = (byte)'M';
        host[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(host[0x3C..], c_PeHeaderOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(host[c_PeHeaderOffset..], 0x00004550);                  // "PE\0\0"
        BinaryPrimitives.WriteUInt16LittleEndian(host[(c_PeHeaderOffset + 4)..], machine);               // Machine
        BinaryPrimitives.WriteUInt16LittleEndian(host[(c_PeHeaderOffset + 6)..], (ushort)sectionCount);  // NumberOfSections
        BinaryPrimitives.WriteUInt16LittleEndian(host[(c_PeHeaderOffset + 20)..], c_PeOptionalHeaderSize); // SizeOfOptionalHeader
        BinaryPrimitives.WriteUInt16LittleEndian(host[c_PeOptionalHeaderOffset..], 0x20B);               // PE32+

        if (machine != 0)
            BinaryPrimitives.WriteUInt16LittleEndian(host[(c_PeHeaderOffset + 22)..], 0x22);             // Characteristics: executable, large address aware
    }

    private readonly record struct Entry(long Offset, long Size, long CompressedSize, BundleFileType Type, string RelativePath);
}