namespace Snowberry.IO.Benchmarks.Infrastructure;

/// <summary>
/// Opens the streams of a <see cref="StreamKind"/> that the reader and writer benchmarks use.
/// </summary>
internal static class BenchmarkStreams
{
    /// <summary>
    /// The size of the buffer of a <see cref="StreamKind.BufferedFile"/> stream.
    /// </summary>
    public const int c_BufferSize = 64 * 1024;

    /// <summary>
    /// Creates the temporary directory for the streams of <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">The kind of the streams.</param>
    /// <param name="name">A name that identifies the benchmark that uses the directory.</param>
    /// <returns>The full path of the created directory, or <see langword="null"/> for <see cref="StreamKind.Memory"/>.</returns>
    public static string? CreateDirectory(StreamKind kind, string name)
    {
        return kind == StreamKind.Memory ? null : BenchmarkData.CreateTempDirectory(name);
    }

    /// <summary>
    /// Opens a read-only stream over <paramref name="data"/>.
    /// </summary>
    /// <param name="kind">The kind of stream.</param>
    /// <param name="data">The data that the stream contains.</param>
    /// <param name="directory">The directory that file streams are created in; only used by the file kinds.</param>
    /// <param name="fileName">The name of the file that <paramref name="data"/> is written to; only used by the file kinds.</param>
    /// <returns>The opened stream, positioned at its start.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <see langword="null"/> for a file kind.</exception>
    public static Stream OpenRead(StreamKind kind, byte[] data, string? directory, string fileName)
    {
        if (kind == StreamKind.Memory)
            return new MemoryStream(data, writable: false);

        string path = Path.Combine(directory ?? throw new ArgumentNullException(nameof(directory)), fileName);
        File.WriteAllBytes(path, data);

        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1);
        return kind == StreamKind.File ? file : new BufferedStream(file, c_BufferSize);
    }

    /// <summary>
    /// Opens an empty, writable and seekable stream.
    /// </summary>
    /// <param name="kind">The kind of stream.</param>
    /// <param name="capacity">The initial capacity of a <see cref="MemoryStream"/>.</param>
    /// <param name="directory">The directory that file streams are created in; only used by the file kinds.</param>
    /// <param name="fileName">The name of the created file; only used by the file kinds.</param>
    /// <returns>The opened stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <see langword="null"/> for a file kind.</exception>
    public static Stream OpenWrite(StreamKind kind, int capacity, string? directory, string fileName)
    {
        if (kind == StreamKind.Memory)
            return new MemoryStream(capacity);

        string path = Path.Combine(directory ?? throw new ArgumentNullException(nameof(directory)), fileName);

        var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
        return kind == StreamKind.File ? file : new BufferedStream(file, c_BufferSize);
    }

    /// <summary>
    /// Reads from <paramref name="stream"/> until <paramref name="count"/> bytes are read or the stream ends.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="buffer">The buffer that receives the bytes.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    public static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        int total = 0;

        while (total < count)
        {
            int read = stream.Read(buffer, total, count - total);

            if (read == 0)
                break;

            total += read;
        }

        return total;
    }
}