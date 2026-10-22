using Microsoft.Win32.SafeHandles;

namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// A temporary file of a <see cref="SpillFilePool"/>, which spill buffers append their data to one buffer at a time.
/// </summary>
internal sealed class SpillFile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpillFile"/> class.
    /// </summary>
    /// <param name="stream">The opened temporary file; it is only used for its handle and closed by <see cref="SpillFilePool.Dispose"/>.</param>
    public SpillFile(FileStream stream)
    {
        Stream = stream;
        Handle = stream.SafeFileHandle;
    }

    /// <summary>
    /// Gets the handle for positional reads and writes.
    /// </summary>
    public SafeFileHandle Handle { get; }

    /// <summary>
    /// Gets or sets the end of the data appended so far; only the buffer that holds the file changes it.
    /// </summary>
    public long Length { get; set; }

    /// <summary>
    /// Gets the opened temporary file.
    /// </summary>
    public FileStream Stream { get; }
}