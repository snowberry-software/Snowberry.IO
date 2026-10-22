using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Snowberry.IO.SingleFile;

namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// Builds minimal single-file bundles in memory: a fake application host that contains the manifest offset and the
/// bundle signature, followed by the file data and the manifest.
/// </summary>
internal static class SyntheticBundleBuilder
{
    /// <summary>
    /// The end of the ELF host's section header table.
    /// </summary>
    public const int c_ElfImageEnd = 164;

    /// <summary>
    /// The length of the fake application host.
    /// </summary>
    public const int c_HostLength = 1024;

    /// <summary>
    /// The offset of the manifest offset field inside the fake host; the signature follows it.
    /// </summary>
    public const int c_ManifestOffsetField = 900;

    public const int c_PeCheckSumOffset = c_PeOptionalHeaderOffset + 64;

    // PE layout: e_lfanew = 0x80, PE32+ optional header of 0xF0 bytes, one section.
    public const int c_PeHeaderOffset = 0x80;

    /// <summary>
    /// The end of the PE host's only section.
    /// </summary>
    public const int c_PeImageEnd = 400;

    public const int c_PeOptionalHeaderOffset = c_PeHeaderOffset + 24;
    public const int c_PeSecurityDirectoryOffset = c_PeOptionalHeaderOffset + 112 + (4 * 8);

    /// <summary>
    /// The 32-byte bundle signature.
    /// </summary>
    public static readonly byte[] s_BundleSignature =
    [
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    ];

    /// <summary>
    /// Builds a bundle with real file data for each of <paramref name="files"/>.
    /// </summary>
    /// <param name="majorVersion">The bundle format major version.</param>
    /// <param name="files">The files to embed.</param>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="flags">The bundle flags.</param>
    /// <param name="host">The application host format.</param>
    /// <param name="assemblyAlignment">The alignment of uncompressed assemblies, like the .NET SDK uses for the host.</param>
    public static byte[] Build(uint majorVersion, IReadOnlyList<BundleFile> files, string bundleId = "originalId00", BundleFlags flags = BundleFlags.None, TestHost host = TestHost.Pe, int assemblyAlignment = 4096)
    {
        return BuildCore(CreateHost(host), c_ManifestOffsetField, majorVersion, files, bundleId, flags, assemblyAlignment);
    }

    /// <summary>
    /// Builds a bundle whose manifest contains exactly <paramref name="entries"/>, after <paramref name="dataLength"/> zero bytes of file data.
    /// </summary>
    public static byte[] BuildRaw(uint majorVersion, IReadOnlyList<RawEntry> entries, int? fileCountOverride = null, int dataLength = 0, TestHost host = TestHost.Pe)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(CreateHost(host));
        writer.Write(new byte[dataLength]);
        WriteManifest(writer, majorVersion, entries, "originalId00", BundleFlags.None, fileCountOverride);
        return stream.ToArray();
    }

    /// <summary>
    /// Builds a version 6 bundle after a caller-provided application host, which already contains the bundle signature right after
    /// <paramref name="manifestOffsetField"/>.
    /// </summary>
    /// <param name="host">The application host bytes.</param>
    /// <param name="manifestOffsetField">The offset in <paramref name="host"/> where the manifest offset is written.</param>
    /// <param name="files">The files to embed.</param>
    public static byte[] BuildWithHost(byte[] host, int manifestOffsetField, IReadOnlyList<BundleFile> files)
    {
        return BuildCore(host, manifestOffsetField, 6, files, "originalId00", BundleFlags.None, assemblyAlignment: 4096);
    }

    public static byte[] Deflate(byte[] data)
    {
        using var compressed = new MemoryStream();

        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data);

        return compressed.ToArray();
    }

    /// <summary>
    /// Points the PE host's security (Authenticode) directory at the given file offset and sets a checksum.
    /// </summary>
    public static void SetSecurityDirectory(byte[] bundle, uint offset, uint size)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bundle.AsSpan(c_PeSecurityDirectoryOffset), offset);
        BinaryPrimitives.WriteUInt32LittleEndian(bundle.AsSpan(c_PeSecurityDirectoryOffset + 4), size);
        BinaryPrimitives.WriteUInt32LittleEndian(bundle.AsSpan(c_PeCheckSumOffset), 0x12345678);
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to a new temporary file and returns its path.
    /// </summary>
    public static string WriteTempFile(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), "snowberry-bundle-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] BuildCore(byte[] host, int manifestOffsetField, uint majorVersion, IReadOnlyList<BundleFile> files, string bundleId, BundleFlags flags, int assemblyAlignment)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(host);

        var records = new List<RawEntry>();

        foreach (var file in files)
        {
            if (file.Type == BundleFileType.Assembly && !file.Compress)
                writer.Write(new byte[(int)((assemblyAlignment - (stream.Position % assemblyAlignment)) % assemblyAlignment)]);

            long offset = stream.Position;
            byte[] stored = file.Compress ? Deflate(file.Data) : file.Data;
            writer.Write(stored);
            records.Add(new RawEntry(offset, file.Data.Length, file.Compress ? stored.Length : 0, file.Type, file.RelativePath));
        }

        WriteManifest(writer, majorVersion, records, bundleId, flags, manifestOffsetField: manifestOffsetField);
        return stream.ToArray();
    }

    private static byte[] CreateHost(TestHost kind)
    {
        byte[] host = new byte[c_HostLength];
        var span = host.AsSpan();

        switch (kind)
        {
            case TestHost.Pe:
                span[0] = (byte)'M';
                span[1] = (byte)'Z';
                BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], c_PeHeaderOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(span[c_PeHeaderOffset..], 0x00004550);       // "PE\0\0"
                BinaryPrimitives.WriteUInt16LittleEndian(span[(c_PeHeaderOffset + 6)..], 1);          // NumberOfSections
                BinaryPrimitives.WriteUInt16LittleEndian(span[(c_PeHeaderOffset + 20)..], 0xF0);      // SizeOfOptionalHeader
                BinaryPrimitives.WriteUInt16LittleEndian(span[c_PeOptionalHeaderOffset..], 0x20B);    // PE32+
                int section = c_PeOptionalHeaderOffset + 0xF0;
                BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 16)..], 100);                // SizeOfRawData
                BinaryPrimitives.WriteUInt32LittleEndian(span[(section + 20)..], c_PeImageEnd - 100); // PointerToRawData
                break;

            case TestHost.Elf:
                span[0] = 0x7F;
                span[1] = (byte)'E';
                span[2] = (byte)'L';
                span[3] = (byte)'F';
                span[4] = 2; // 64-bit
                span[5] = 1; // little-endian
                BinaryPrimitives.WriteUInt16LittleEndian(span[18..], 0x3E);  // EM_X86_64
                BinaryPrimitives.WriteUInt64LittleEndian(span[0x28..], 100); // e_shoff
                BinaryPrimitives.WriteUInt16LittleEndian(span[0x3A..], 64);  // e_shentsize
                BinaryPrimitives.WriteUInt16LittleEndian(span[0x3C..], 1);   // e_shnum
                break;

            case TestHost.MachO:
                BinaryPrimitives.WriteUInt32LittleEndian(span, 0xFEEDFACF);
                BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0x0100000C); // CPU_TYPE_ARM64
                break;

            case TestHost.UniversalMachO:
                BinaryPrimitives.WriteUInt32BigEndian(span, 0xCAFEBABE);
                break;
        }

        s_BundleSignature.CopyTo(host, c_ManifestOffsetField + 8);
        return host;
    }

    private static void WriteManifest(BinaryWriter writer, uint majorVersion, IReadOnlyList<RawEntry> entries, string bundleId, BundleFlags flags, int? fileCountOverride = null, int manifestOffsetField = c_ManifestOffsetField)
    {
        long manifestOffset = writer.BaseStream.Position;

        writer.Write(majorVersion);
        writer.Write(0u);
        writer.Write(fileCountOverride ?? entries.Count);
        writer.Write(bundleId);

        if (majorVersion >= 2)
        {
            // Like the .NET SDK bundler, record the locations of the deps.json and runtimeconfig.json entries.
            foreach (var type in new[] { BundleFileType.DepsJson, BundleFileType.RuntimeConfigJson })
            {
                var location = entries.FirstOrDefault(e => e.Type == type);
                writer.Write(location.Offset);
                writer.Write(location.Size);
            }

            writer.Write((ulong)flags);
        }

        foreach (var entry in entries)
        {
            writer.Write(entry.Offset);
            writer.Write(entry.Size);

            if (majorVersion >= 6)
                writer.Write(entry.CompressedSize);

            writer.Write((byte)entry.Type);
            writer.Write(entry.RelativePath);
        }

        long end = writer.BaseStream.Position;
        writer.BaseStream.Position = manifestOffsetField;
        writer.Write(manifestOffset);
        writer.BaseStream.Position = end;
    }
}