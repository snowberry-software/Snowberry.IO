using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;

namespace Snowberry.IO.SingleFile.Meta;

// https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/FileEntry.cs

/// <summary>
/// The metadata of a file in a single-file bundle.
/// </summary>
public class FileEntry(BundleManifest bundleManifest) : IDisposable
{
    public const char DirectorySeparatorChar = '/';

    /// <inheritdoc/>
    public override string ToString()
    {
        return RelativePath;
    }

    /// <summary>
    /// Reads the metadata of the file entry.
    /// </summary>
    /// <param name="reader">The reader.</param>
    public void Read(IEndianReader reader)
    {
        FileMetadataOffset = reader.Position;
        Location = FileLocation.Read(reader);

        if (BundleManifest.BundleMajorVersion >= 6)
            CompressedSize = reader.ReadInt64();

        FileType = (FileType)reader.ReadByte();
        RelativePath = reader.ReadString() ?? string.Empty;
    }

    /// <summary>
    /// Writes the metadata of the file entry.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public void Write(IEndianWriter writer)
    {
        WriteWith(writer, Location, CompressedSize);
    }

    /// <summary>
    /// Writes the metadata of the file entry using custom properties.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="location">The location.</param>
    /// <param name="compressedSize">The compressed size.</param>
    public void WriteWith(IEndianWriter writer, FileLocation location, long compressedSize)
    {
        location.Write(writer);

        if (BundleManifest.BundleMajorVersion >= 6)
            writer.Write(compressedSize);

        writer.Write((byte)FileType);
        writer.Write(RelativePath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (ModifiedFileEntryMeta == null)
            return;

        ModifiedFileEntryMeta.Dispose();
    }

    /// <summary>
    /// The owning bundle manifest.
    /// </summary>
    public BundleManifest BundleManifest { get; set; } = bundleManifest;

    /// <summary>
    /// The location of the data inside the bundle.
    /// </summary>
    public FileLocation Location { get; set; }

    /// <summary>
    /// The compressed size of the file.
    /// </summary>
    public long CompressedSize { get; set; }

    /// <summary>
    /// The relative path of the file.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// The file type.
    /// </summary>
    public FileType FileType { get; set; }

    /// <summary>
    /// The offset of the file metadata in the application.
    /// </summary>
    /// <remarks>Only used when reading.</remarks>
    public long FileMetadataOffset { get; set; }

    /// <summary>
    /// The actual size of the file.
    /// </summary>
    /// <remarks>Uses the compressed size if available, otherwise the location size.</remarks>
    public long ActualSize => CompressedSize > 0 ? CompressedSize : Location.Size;

    /// <summary>
    /// The modified file data.
    /// </summary>
    public ModifiedFileEntryMeta? ModifiedFileEntryMeta { get; set; }
}
