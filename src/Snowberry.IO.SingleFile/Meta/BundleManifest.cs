using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;

namespace Snowberry.IO.SingleFile.Meta;

/// <summary>
/// The bundle manifest that contains the file entry metadata collection.
/// </summary>
public class BundleManifest : IDisposable
{
    public const int BundleIdLength = 12;

    /// <summary>
    /// Reads the data of the bundle manifest.
    /// </summary>
    /// <param name="reader">The reader.</param>
    public void Read(IEndianReader reader)
    {
        FileEntries.Clear();

        BundleMajorVersion = reader.ReadUInt32();
        BundleMinorVersion = reader.ReadUInt32();
        int fileCount = reader.ReadInt32();
        BundleID = reader.ReadString() ?? string.Empty;

        if (BundleMajorVersion >= 2)
        {
            DepsJsonLocation = FileLocation.Read(reader);
            RuntimeConfigJsonLocation = FileLocation.Read(reader);

            Flags = (HeaderFlags)reader.ReadUInt64();
        }

        for (int i = 0; i < fileCount; i++)
        {
            var entry = new FileEntry(this);
            entry.Read(reader);

            if ((DepsJsonFile == null || RuntimeConfigFile == null) && BundleMajorVersion >= 2)
            {
                if (entry.Location.Offset == DepsJsonLocation.Offset && entry.Location.Size == DepsJsonLocation.Size)
                    DepsJsonFile = entry;

                if (entry.Location.Offset == RuntimeConfigJsonLocation.Offset && entry.Location.Size == RuntimeConfigJsonLocation.Size)
                    RuntimeConfigFile = entry;
            }

            FileEntries.Add(entry);
        }
    }

    /// <summary>
    /// Writes the data of the bundle manifest.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="includeFileMetadata">Whether to include the file metadata.</param>
    /// <param name="bundleId">The new bundle id.</param>
    /// <param name="depsLocation">The new *.deps.json file location.</param>
    /// <param name="runtimeLocation">The new *.runtimeconfig.json file location.</param>
    public void Write(
        IEndianWriter writer,
        bool includeFileMetadata,
        string bundleId,
        FileLocation depsLocation,
        FileLocation runtimeLocation)
    {
        writer.Write(BundleMajorVersion);
        writer.Write(BundleMinorVersion);

        writer.Write(FileEntries.Count);
        writer.Write(bundleId);

        if (BundleMajorVersion >= 2)
        {
            depsLocation.Write(writer);
            runtimeLocation.Write(writer);

            writer.Write((ulong)Flags);
        }

        if (includeFileMetadata)
            for (int i = 0; i < FileEntries.Count; i++)
            {
                var entry = FileEntries[i];
                entry.Write(writer);
            }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        for (int i = 0; i < FileEntries.Count; i++)
        {
            var fileEntry = FileEntries[i];
            fileEntry.Dispose();
        }
    }

    /// <summary>
    /// The *.deps.json file location.
    /// </summary>
    /// <remarks>Will only be read and is not a direct reference to the <see cref="FileEntry"/>. Writing will not use this.</remarks>
    public FileLocation DepsJsonLocation { get; set; }

    /// <summary>
    /// The *.runtimeconfig.json file location.
    /// </summary>
    /// <remarks>Will only be read and is not a direct reference to the <see cref="FileEntry"/>. Writing will not use this.</remarks>
    public FileLocation RuntimeConfigJsonLocation { get; set; }

    /// <summary>
    /// The *.deps.json file.
    /// </summary>
    public FileEntry? DepsJsonFile { get; set; }

    /// <summary>
    /// The *.runtimeconfig.json file.
    /// </summary>
    public FileEntry? RuntimeConfigFile { get; set; }

    /// <summary>
    /// The bundle major version.
    /// </summary>
    public uint BundleMajorVersion { get; set; }

    /// <summary>
    /// The bundle minor version.
    /// </summary>
    public uint BundleMinorVersion { get; set; }

    /// <summary>
    /// The bundle id.
    /// </summary>
    public string BundleID { get; set; } = string.Empty;

    /// <summary>
    /// The bundle file collection.
    /// </summary>
    public List<FileEntry> FileEntries { get; set; } = [];

    /// <summary>
    /// The bundle manifest flags.
    /// </summary>
    public HeaderFlags Flags { get; set; }

    /// <summary>
    /// The bundle manifest header flags.
    /// </summary>
    public enum HeaderFlags : ulong
    {
        None = 0,
        NetcoreApp3CompatMode = 1
    }
}
