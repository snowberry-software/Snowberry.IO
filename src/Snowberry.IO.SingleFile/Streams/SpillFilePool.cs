namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// The temporary files that the spill buffers of one operation append their data to.
/// </summary>
/// <remarks>
/// A buffer holds a file exclusively while it appends to it and returns it once its data is complete, so an operation creates one file per buffer
/// that spills at the same time instead of one per buffer. The files are deleted when the pool is disposed.
/// </remarks>
/// <param name="useAsync">Whether the files are opened for asynchronous I/O.</param>
/// <param name="directory">The directory to create the files in, or <see langword="null"/> for the temporary directory of the current user.</param>
internal sealed class SpillFilePool(bool useAsync, string? directory = null) : IDisposable
{
    private readonly List<SpillFile> _files = [];
    private readonly Stack<SpillFile> _idle = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Creates a temporary file that is deleted when it is closed and, on Linux and macOS, readable and writable only by the current user.
    /// </summary>
    /// <param name="useAsync">Whether the file is opened for asynchronous I/O.</param>
    /// <param name="directory">The directory to create the file in, or <see langword="null"/> for the temporary directory of the current user.</param>
    /// <returns>The opened, unbuffered temporary file.</returns>
    public static FileStream CreateTempFile(bool useAsync, string? directory = null)
    {
        string path = Path.Combine(directory ?? Path.GetTempPath(), $"snowberry-bundle-{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 0,
            Options = FileOptions.DeleteOnClose | (useAsync ? FileOptions.Asynchronous : FileOptions.None)
        };

        // The temporary directory is usually shared, and the buffered data may be confidential.
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        return new FileStream(path, options);
    }

    /// <summary>
    /// Closes and thereby deletes all files.
    /// </summary>
    public void Dispose()
    {
        SpillFile[] files;

        lock (_lock)
        {
            files = [.. _files];
            _files.Clear();
            _idle.Clear();
        }

        foreach (var file in files)
            file.Stream.Dispose();
    }

    /// <summary>
    /// Takes a file that no buffer is appending to, creating one if all are in use.
    /// </summary>
    /// <returns>The file, which the caller appends to exclusively until it passes it to <see cref="Return"/>.</returns>
    public SpillFile Rent()
    {
        lock (_lock)
        {
            if (_idle.TryPop(out var idle))
                return idle;
        }

        var file = new SpillFile(CreateTempFile(useAsync, directory));

        lock (_lock)
            _files.Add(file);

        return file;
    }

    /// <summary>
    /// Makes a file available to other buffers; data appended to it before stays in place until the pool is disposed.
    /// </summary>
    /// <param name="file">A file from <see cref="Rent"/>.</param>
    public void Return(SpillFile file)
    {
        lock (_lock)
            _idle.Push(file);
    }

    /// <summary>
    /// Gets the number of files the pool created.
    /// </summary>
    public int FileCount
    {
        get
        {
            lock (_lock)
                return _files.Count;
        }
    }

    /// <summary>
    /// Gets the number of bytes the files hold, excluding data that buffers discarded.
    /// </summary>
    public long Length
    {
        get
        {
            lock (_lock)
                return _files.Sum(f => f.Length);
        }
    }
}