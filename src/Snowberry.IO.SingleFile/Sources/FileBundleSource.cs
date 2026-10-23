using Microsoft.Win32.SafeHandles;

namespace Snowberry.IO.SingleFile.Sources;

/// <summary>
/// Reads a file through <see cref="RandomAccess"/>, which allows concurrent reads without a shared position.
/// </summary>
/// <remarks>
/// On Windows the file is opened with <see cref="FileOptions.RandomAccess"/>, which reads cached data faster, notably in large reads such as
/// <see cref="BundleEntry.ReadAllBytes"/> of an uncompressed entry. Elsewhere the hint only turns off the read-ahead of the kernel
/// (<c>posix_fadvise</c> with <c>POSIX_FADV_RANDOM</c>), which saving and extracting benefit from because they read entries front to back.
/// </remarks>
internal sealed class FileBundleSource : BundleSource
{
    private readonly SafeFileHandle _handle;

    private FileBundleSource(SafeFileHandle handle, string filePath) : base(nameof(SingleFileBundle))
    {
        _handle = handle;
        FilePath = filePath;
        Length = RandomAccess.GetLength(handle);
    }

    /// <summary>
    /// Opens a file for shared reading.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="useAsync"><see langword="true"/> to open the handle for asynchronous I/O; <see langword="false"/> for synchronous I/O.</param>
    /// <returns>The opened source.</returns>
    public static FileBundleSource Open(string path, bool useAsync)
    {
        string fullPath = Path.GetFullPath(path);
        var options = (OperatingSystem.IsWindows() ? FileOptions.RandomAccess : FileOptions.None) | (useAsync ? FileOptions.Asynchronous : FileOptions.None);
        var handle = File.OpenHandle(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, options);

        return new FileBundleSource(handle, fullPath);
    }

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        ThrowIfDisposed();
        return RandomAccess.Read(_handle, buffer, offset);
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return RandomAccess.ReadAsync(_handle, buffer, offset, cancellationToken);
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        _handle.Dispose();
    }

    /// <inheritdoc/>
    public override string FilePath { get; }

    /// <inheritdoc/>
    public override long Length { get; }

    /// <summary>
    /// Gets the Unix permissions of the file, or <see langword="null"/> on Windows.
    /// </summary>
    public UnixFileMode? UnixFileMode => OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(_handle);
}