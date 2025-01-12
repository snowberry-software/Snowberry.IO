using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using Snowberry.IO.Reader;
using Snowberry.IO.SingleFile.Meta;
using Snowberry.IO.Writer;

namespace Snowberry.IO.SingleFile;

public class SingleFileBinaryData : IDisposable
{
    private Stream? _modifiedBundleStream;

    // https://github.com/dotnet/runtime/blob/release/8.0/src/installer/managed/Microsoft.NET.HostModel/AppHost/HostWriter.cs#L173
    private static readonly byte[] s_BundleSignature =
    [
        // 32 bytes represent the bundle signature: SHA-256 for ".net core bundle"
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    ];

    /// <summary>
    /// Gets the binary file data from the file.
    /// </summary>
    /// <remarks>Returns <see langword="null"/> if the bundle could not be found!</remarks>
    /// <param name="filePath">The file path.</param>
    /// <returns>The binary file data.</returns>
    public static SingleFileBinaryData? GetFromFile(string filePath)
    {
        using var binaryFileMap = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = binaryFileMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        int offset = BinarySearchHelper.SearchInView(accessor, s_BundleSignature);

        if (offset == -1)
            return null;

        long bundleManifestOffset = accessor.ReadInt64(offset - 8);

        using var reader = new EndianStreamReader(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        reader.Position = bundleManifestOffset;
        var bundleManifest = new BundleManifest();
        bundleManifest.Read(reader);

        long offsetMin = long.MaxValue;
        long offsetMax = long.MinValue;
        long bundleSize = 0;
        for (int i = 0; i < bundleManifest.FileEntries.Count; i++)
        {
            var file = bundleManifest.FileEntries[i];
            offsetMin = Math.Min(offsetMin, file.Location.Offset);
            offsetMax = Math.Max(offsetMax, file.Location.Offset + file.ActualSize);

            bundleSize += file.ActualSize;
        }

        var result = new SingleFileBinaryData()
        {
            FilePath = filePath,
            BundleSignatureOffset = offset,
            BundleManifestOffset = bundleManifestOffset,
            BundleManifest = bundleManifest,
            IsBundleLastBinaryData = reader.Length == reader.Position && offsetMax == bundleManifestOffset,
            BundleOffset = offsetMin,
            SignatureOffset = offset
        };

        return result;
    }

    /// <summary>
    /// Saves the modified single file published binary application to the given target file path.
    /// </summary>
    /// <param name="targetFilePath">The target file path.</param>
    /// <param name="targetInfo">The target info.</param>
    /// <param name="options">The bundler options.</param>
    public void Save(string targetFilePath, TargetInfo targetInfo, BundlerOptions? options = null)
    {
        Save(new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
            targetInfo, options);
    }

    /// <summary>
    /// Saves the modified single file published binary application to the given target file path.
    /// </summary>
    /// <param name="targetStream">The target stream.</param>
    /// <param name="targetInfo">The target info.</param>
    /// <param name="options">The bundler options.</param>
    public void Save(Stream targetStream, TargetInfo targetInfo, BundlerOptions? options = null)
    {
        options ??= new();

        using var writer = new EndianStreamWriter(targetStream, false);
        using var binaryReader = new EndianStreamReader(new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        using var bundleHash = SHA256.Create();

        binaryReader.CopyTo(writer.BaseStream, (int)BundleOffset);

        using var fileListWriter = new EndianStreamWriter(new MemoryStream(), false);

        int assemblyFileAlignment = targetInfo switch
        {
            TargetInfo.Windows => 4096,
            TargetInfo.Arm64 => 4096,
            TargetInfo.Other => 64,
            _ => 1
        };

        long fileOffsetStart = writer.BaseStream.Position;

        var depsJsonLocation = new FileLocation();
        var runtimeJsonLocation = new FileLocation();

        for (int i = 0; i < BundleManifest.FileEntries.Count; i++)
        {
            var fileEntry = BundleManifest.FileEntries[i];
            var fileResult = AddToBundle(
                fileEntry,
                assemblyFileAlignment,
                options,
                fileOffsetStart,
                bundleHash);

            var fileEntryLocation = new FileLocation()
            {
                Offset = fileOffsetStart + fileResult.Offset,
                Size = fileResult.FileSize
            };

            fileEntry.WriteWith(fileListWriter, fileEntryLocation, fileResult.CompressedSize);

            if (fileEntry.FileType == FileType.DepsJson)
                depsJsonLocation = fileEntryLocation;
            else if (fileEntry.FileType == FileType.RuntimeConfigJson)
                runtimeJsonLocation = fileEntryLocation;
        }

        ModifiedBundleStream.Position = 0;
        ModifiedBundleStream.CopyTo(writer.BaseStream);

        long bundleManifestOffset = writer.Position;

        BundleManifest.Write(
            writer,
            false,
            GenerateDeterministicId(bundleHash),
            depsJsonLocation,
            runtimeJsonLocation);

        fileListWriter.Position = 0;
        fileListWriter.BaseStream.CopyTo(writer.BaseStream);

        writer.Position = SignatureOffset - 8;
        writer.Write(bundleManifestOffset);

        DisposeModifiedBundleStream();
    }

    private (long Offset, long CompressedSize, long FileSize) AddToBundle(
        FileEntry fileEntry,
        int assemblyFileAlignment,
        BundlerOptions options,
        long bundleStartOffset,
        SHA256 bundleHash)
    {
        long offset = ModifiedBundleStream.Position;

        using var fileEntryStream = GetStream(fileEntry) ?? throw new IOException($"Could not get stream to file entry: `{fileEntry.RelativePath}`");
        long fileSize = fileEntryStream.Length;

        // Entry file hash
        {
            byte[] hashBytes = ComputeSha256Hash(fileEntryStream);
            bundleHash.TransformBlock(hashBytes, 0, hashBytes.Length, hashBytes, 0);
        }

        fileEntryStream.Position = 0;

        if (options.UseCompression && ShouldCompress(fileEntry.FileType))
        {
            var smallestSize = (CompressionLevel)3;
            using (var compressionStream = new DeflateStream(ModifiedBundleStream, Enum.IsDefined(typeof(CompressionLevel), smallestSize) ? smallestSize : CompressionLevel.Optimal, leaveOpen: true))
            {
                fileEntryStream.CopyTo(compressionStream);
            }

            long compressedSize = ModifiedBundleStream.Position - offset;

            if (options.ForceCompression || compressedSize < fileSize * options.CompressionThreshold)
                return (offset, compressedSize, fileSize);

            ModifiedBundleStream.Seek(offset, SeekOrigin.Begin);
        }

        if (fileEntry.FileType == FileType.Assembly && assemblyFileAlignment != 1)
        {
            long misalignment = (ModifiedBundleStream.Position + bundleStartOffset) % assemblyFileAlignment;

            if (misalignment != 0)
            {
                long padding = assemblyFileAlignment - misalignment;
                ModifiedBundleStream.Position += padding;
            }
        }

        fileEntryStream.Position = 0;
        offset = ModifiedBundleStream.Position;

        fileEntryStream.CopyTo(ModifiedBundleStream);
        return (offset, 0, fileSize);
    }

    /// <summary>
    /// Changes the modified state of the given <see cref="FileEntry"/>.
    /// </summary>
    /// <param name="fileEntry">The file entry.</param>
    /// <param name="data">The modified data.</param>
    public static void ModifyFileEntry(FileEntry fileEntry, byte[] data)
    {
        fileEntry.ModifiedFileEntryMeta?.Dispose();
        fileEntry.ModifiedFileEntryMeta = new()
        {
            ModifiedDataStream = new MemoryStream(data),
            FileLocation = new()
            {
                Offset = 0,
                Size = data.Length
            }
        };
    }

    /// <summary>
    /// Gets a data stream to the given file entry.
    /// </summary>
    /// <remarks>
    /// The file entry must exist in <see cref="BundleManifest"/>.<para/>
    /// Be careful, if the file is modified it will reuse the modified file stream.
    /// </remarks>
    /// <param name="fileEntry">The file entry.</param>
    /// <returns>The stream to the file entry data,</returns>
    public Stream? GetStream(FileEntry fileEntry)
    {
        if (BundleManifest == null || !BundleManifest.FileEntries.Contains(fileEntry))
            return null;

        if (!File.Exists(FilePath))
            return null;

        if (fileEntry.ModifiedFileEntryMeta != null && fileEntry.ModifiedFileEntryMeta.ModifiedDataStream != null)
        {
            var modifiedStream = fileEntry.ModifiedFileEntryMeta.ModifiedDataStream;
            var viewStream = new StreamView(
                modifiedStream,
                fileEntry.ModifiedFileEntryMeta.FileLocation.Offset,
                fileEntry.ModifiedFileEntryMeta.FileLocation.Size,
                true);

            return viewStream;
        }

        using var reader = new EndianStreamReader(new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        reader.Position = fileEntry.Location.Offset;

        MemoryStream memoryStream = new();
        if (fileEntry.CompressedSize > 0)
        {
            using var compressionStream = new DeflateStream(reader.Stream!, CompressionMode.Decompress, leaveOpen: true);

            byte[] buffer = new byte[4096 * 2];
            int bytesRead;

            while ((bytesRead = compressionStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                memoryStream.Write(buffer, 0, bytesRead);

                if (memoryStream.Length >= fileEntry.Location.Size)
                    break;
            }

            memoryStream.Position = 0;
            return memoryStream;
        }

        reader.CopyTo(memoryStream, (int)fileEntry.Location.Size);
        memoryStream.Position = 0;
        return memoryStream;
    }

    private static string GenerateDeterministicId(SHA256 bundleHash)
    {
        bundleHash.TransformFinalBlock([], 0, 0);
        byte[] manifestHash = bundleHash.Hash!;

        return Convert.ToBase64String(manifestHash)[BundleManifest.BundleIdLength..].Replace('/', '_');
    }

    private static byte[] ComputeSha256Hash(Stream stream)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(stream);
    }

    private static bool ShouldCompress(FileType type)
    {
        return type switch
        {
            FileType.DepsJson or FileType.RuntimeConfigJson => false,
            _ => true,
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        BundleManifest?.Dispose();
        DisposeModifiedBundleStream();
    }

    private void DisposeModifiedBundleStream()
    {
        _modifiedBundleStream?.Dispose();
        _modifiedBundleStream = null;
    }

    /// <summary>
    /// The offset of the signature where the manifest offset is located.
    /// </summary>
    public long SignatureOffset { get; set; }

    /// <summary>
    /// The file path.
    /// </summary>
    public string FilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Determines whether the bundle is the last binary data in the file after the host code etc.
    /// </summary>
    public bool IsBundleLastBinaryData { get; set; }

    /// <summary>
    /// The signature offset of the bundle.
    /// </summary>
    public int BundleSignatureOffset { get; set; }

    /// <summary>
    /// The offset of the bundle manifest.
    /// </summary>
    public long BundleManifestOffset { get; set; }

    /// <summary>
    /// The offset where the bundle starts.
    /// </summary>
    public long BundleOffset { get; set; }

    /// <summary>
    /// The modified bundle stream.
    /// </summary>
    /// <remarks>
    /// The existing stream will be dispoed when the setter is used. 
    /// By default a <see cref="MemoryStream"/> is used.
    /// </remarks>
    public Stream ModifiedBundleStream
    {
        get
        {
            _modifiedBundleStream ??= new MemoryStream();

            return _modifiedBundleStream;
        }

        set
        {
            DisposeModifiedBundleStream();
            _modifiedBundleStream = value;
        }
    }

    /// <summary>
    /// The bundle manifest.
    /// </summary>
    public BundleManifest BundleManifest { get; set; } = new();
}
