using Snowberry.IO.SingleFile.Content;
using Snowberry.IO.SingleFile.Format;
using Snowberry.IO.SingleFile.Streams;

namespace Snowberry.IO.SingleFile;

/// <summary>
/// A file stored in a <see cref="SingleFileBundle"/>.
/// </summary>
/// <remarks>
/// <see cref="Replace(ReadOnlyMemory{byte})"/>, <see cref="Replace(Stream, bool)"/>, <see cref="ReplaceFromFile(string)"/> and <see cref="Revert"/>
/// change the bundle in memory only; the changes are written when the bundle is saved with
/// <see cref="SingleFileBundle.Save(string, BundleWriteOptions?)"/> or one of its overloads.
/// </remarks>
public sealed class BundleEntry
{
    // Bundled assemblies deflate to about 40% of their size; starting at 8 times the compressed size usually avoids growing the array.
    private const int c_ExpectedMaxDeflateRatio = 8;
    private const int c_MinInitialReadLength = 1024 * 1024;

    private readonly EntryData? _original;
    private SingleFileBundle? _bundle;

    /// <summary>
    /// Creates an entry.
    /// </summary>
    /// <param name="bundle">The bundle the entry belongs to.</param>
    /// <param name="relativePath">The entry path.</param>
    /// <param name="type">The file type.</param>
    /// <param name="data">The content.</param>
    /// <param name="isOriginal">Whether <paramref name="data"/> is the content of the opened file.</param>
    internal BundleEntry(SingleFileBundle bundle, string relativePath, BundleFileType type, EntryData data, bool isOriginal)
    {
        _bundle = bundle;
        _original = isOriginal ? data : null;
        RelativePath = relativePath;
        Type = type;
        Data = data;
    }

    /// <summary>
    /// Writes the entry's uncompressed content into a directory, at its <see cref="RelativePath"/>.
    /// </summary>
    /// <remarks>
    /// The directory and the subdirectories of <see cref="RelativePath"/> are created as needed. A <see cref="RelativePath"/> that would resolve
    /// outside of <paramref name="destinationDirectory"/>, such as <c>../x</c> or an absolute path, is rejected.
    /// </remarks>
    /// <param name="destinationDirectory">The directory to extract into.</param>
    /// <param name="overwrite"><see langword="true"/> to overwrite an existing file; otherwise, <see langword="false"/>.</param>
    /// <returns>The full path of the written file.</returns>
    /// <exception cref="ArgumentException"><paramref name="destinationDirectory"/> is <see langword="null"/>, empty or not a valid path.</exception>
    /// <exception cref="InvalidDataException">
    /// <see cref="RelativePath"/> does not resolve to a file inside of <paramref name="destinationDirectory"/>, or the stored data is shorter or longer
    /// than <see cref="Size"/>.
    /// </exception>
    /// <exception cref="IOException">The file exists and <paramref name="overwrite"/> is <see langword="false"/>, or an I/O error occurred.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to write to <paramref name="destinationDirectory"/>.</exception>
    /// <exception cref="InvalidOperationException">The entry was removed from its bundle.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public string ExtractToDirectory(string destinationDirectory, bool overwrite = false)
    {
        return ExtractToDirectoryCoreAsync(useAsync: false, destinationDirectory, overwrite, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="ExtractToDirectory(string, bool)"/>
    /// <param name="destinationDirectory">The directory to extract into.</param>
    /// <param name="overwrite"><see langword="true"/> to overwrite an existing file; otherwise, <see langword="false"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the full path of the written file.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task<string> ExtractToDirectoryAsync(string destinationDirectory, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        return ExtractToDirectoryCoreAsync(useAsync: true, destinationDirectory, overwrite, cancellationToken).AsTask();
    }

    /// <summary>
    /// Writes the entry's uncompressed content to a file.
    /// </summary>
    /// <remarks>If writing fails, the partially written file is left in place.</remarks>
    /// <param name="destinationPath">The path of the file to create; its directory must exist.</param>
    /// <param name="overwrite"><see langword="true"/> to overwrite an existing file; otherwise, <see langword="false"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="destinationPath"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="IOException">The file exists and <paramref name="overwrite"/> is <see langword="false"/>, the directory does not exist, or an I/O error occurred.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to write the file.</exception>
    /// <exception cref="InvalidOperationException">The entry was removed from its bundle.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    /// <exception cref="InvalidDataException">The stored data is shorter or longer than <see cref="Size"/>.</exception>
    public void ExtractToFile(string destinationPath, bool overwrite = false)
    {
        ExtractToFileCoreAsync(useAsync: false, destinationPath, overwrite, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="ExtractToFile(string, bool)"/>
    /// <param name="destinationPath">The path of the file to create; its directory must exist.</param>
    /// <param name="overwrite"><see langword="true"/> to overwrite an existing file; otherwise, <see langword="false"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file has been written.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task ExtractToFileAsync(string destinationPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        return ExtractToFileCoreAsync(useAsync: true, destinationPath, overwrite, cancellationToken).AsTask();
    }

    /// <summary>
    /// Opens a read-only stream over the entry's uncompressed content.
    /// </summary>
    /// <remarks>
    /// Compressed entries are decompressed incrementally as the stream is read, and reading fails with <see cref="InvalidDataException"/> when the
    /// data does not match <see cref="Size"/>. Unchanged uncompressed entries return a seekable stream, and reading fails with
    /// <see cref="InvalidDataException"/> when the stored data ends before <see cref="Size"/>; for added or replaced content that ends early, it
    /// fails with <see cref="IOException"/>. Streams of the same bundle can be read concurrently; reads of a bundle opened from a stream are
    /// serialized.
    /// </remarks>
    /// <returns>A stream that must be disposed by the caller.</returns>
    /// <exception cref="InvalidOperationException">The entry was removed from its bundle.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public Stream Open()
    {
        ThrowIfDetached();
        return Data.Open();
    }

    /// <summary>
    /// Reads the entry's uncompressed content into a new array.
    /// </summary>
    /// <returns>The entry's content.</returns>
    /// <exception cref="InvalidOperationException">The entry was removed from its bundle, or its content is too large for an array.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    /// <exception cref="InvalidDataException">The stored data is shorter or longer than <see cref="Size"/>.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public byte[] ReadAllBytes()
    {
        return ReadAllBytesCoreAsync(useAsync: false, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="ReadAllBytes()"/>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the entry's content.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
    {
        return ReadAllBytesCoreAsync(useAsync: true, cancellationToken).AsTask();
    }

    /// <summary>
    /// Replaces the entry's content with <paramref name="content"/>.
    /// </summary>
    /// <remarks>Array-backed memory is referenced, not copied; do not change it until the bundle has been saved.</remarks>
    /// <param name="content">The new content.</param>
    /// <exception cref="InvalidOperationException">The entry was removed from its bundle.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public void Replace(ReadOnlyMemory<byte> content)
    {
        ThrowIfDetached();
        SetData(EntryData.FromMemory(content, RelativePath));
    }

    /// <summary>
    /// Replaces the entry's content with the data of <paramref name="content"/>, from its current position to its end.
    /// </summary>
    /// <remarks>
    /// A seekable stream is read when the bundle is saved, so it must stay open and unchanged until then.
    /// A non-seekable stream is copied into memory immediately, with a synchronous read.
    /// </remarks>
    /// <param name="content">The stream that provides the new content.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="content"/> open after the entry no longer needs it; otherwise, <see langword="false"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="content"/> is not readable.</exception>
    /// <exception cref="InvalidOperationException">The entry was removed from its bundle.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    /// <exception cref="IOException">An I/O error occurred while copying a non-seekable <paramref name="content"/>.</exception>
    public void Replace(Stream content, bool leaveOpen = false)
    {
        ThrowIfDetached();
        SetData(EntryData.FromStream(content, leaveOpen, RelativePath));
    }

    /// <summary>
    /// Replaces the entry's content with the content of a file.
    /// </summary>
    /// <remarks>
    /// The file is opened immediately and read when the bundle is saved. It stays open, readable by others, until the content is replaced again,
    /// reverted or the bundle is disposed.
    /// </remarks>
    /// <param name="sourceFileName">The path of the file.</param>
    /// <exception cref="ArgumentException"><paramref name="sourceFileName"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to read the file.</exception>
    /// <exception cref="IOException">An I/O error occurred while opening the file.</exception>
    /// <exception cref="InvalidOperationException">The entry was removed from its bundle.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public void ReplaceFromFile(string sourceFileName)
    {
        ThrowIfDetached();
        SetData(EntryData.FromFile(sourceFileName, RelativePath));
    }

    /// <summary>
    /// Discards the replaced content and restores the content the entry had when the bundle was opened.
    /// </summary>
    /// <exception cref="InvalidOperationException">The entry was added since the bundle was opened, or it was removed from its bundle.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public void Revert()
    {
        ThrowIfDetached();

        if (_original == null)
            throw new InvalidOperationException($"The entry '{RelativePath}' was added to the bundle and has no original content.");

        SetData(_original);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return RelativePath;
    }

    /// <summary>
    /// Detaches this entry from its bundle and restores its original content.
    /// </summary>
    internal void Detach()
    {
        SetData(_original);
        _bundle = null;
    }

    /// <summary>
    /// Extracts the content of this entry to a file.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="destinationPath">The path of the file to write.</param>
    /// <param name="overwrite"><see langword="true"/> to replace an existing file; <see langword="false"/> to fail when the file exists.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file has been written.</returns>
    internal async ValueTask ExtractToFileCoreAsync(bool useAsync, string destinationPath, bool overwrite, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        ThrowIfDetached();

        var source = Data.Open();

        try
        {
            var options = new FileStreamOptions
            {
                Mode = overwrite ? FileMode.Create : FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 1,
                Options = useAsync ? FileOptions.Asynchronous : FileOptions.None,

                // Compressed data may declare far more than it decompresses to, so only content stored as is reserves its size up front.
                PreallocationSize = Data.IsCompressed ? 0 : Data.Size
            };

            var destination = new FileStream(destinationPath, options);

            try
            {
                await StreamCopy.CopyAsync(useAsync, source, destination, null, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await StreamCopy.DisposeAsync(useAsync, destination).ConfigureAwait(false);
            }
        }
        finally
        {
            await StreamCopy.DisposeAsync(useAsync, source).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resolves the extraction path of this entry inside the extraction directory.
    /// </summary>
    /// <param name="rootPrefix">The full path of the extraction directory with a trailing separator, from <see cref="BundlePath.GetRootPrefix"/>.</param>
    /// <returns>The full path of the file to write.</returns>
    /// <exception cref="InvalidDataException">The path does not resolve to a file inside of the extraction directory.</exception>
    internal string GetSafeOutputPath(string rootPrefix)
    {
        return BundlePath.TryGetSafeOutputPathUnder(rootPrefix, RelativePath, out string? fullPath)
            ? fullPath
            : throw new InvalidDataException($"The bundle entry path '{RelativePath}' does not resolve to a file inside of the extraction directory.");
    }

    /// <summary>
    /// Releases the content of this entry when it was added or replaced.
    /// </summary>
    internal void ReleaseData()
    {
        if (IsModified)
            Data.Dispose();
    }

    /// <summary>
    /// Asynchronously releases the content of this entry when it was added or replaced.
    /// </summary>
    /// <returns>A task that completes when the content has been released.</returns>
    internal ValueTask ReleaseDataAsync()
    {
        return IsModified ? Data.DisposeAsync() : ValueTask.CompletedTask;
    }

    private async ValueTask<string> ExtractToDirectoryCoreAsync(bool useAsync, string destinationDirectory, bool overwrite, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        ThrowIfDetached();

        string fullPath = GetSafeOutputPath(BundlePath.GetRootPrefix(destinationDirectory));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await ExtractToFileCoreAsync(useAsync, fullPath, overwrite, cancellationToken).ConfigureAwait(false);
        return fullPath;
    }

    private async ValueTask<byte[]> ReadAllBytesCoreAsync(bool useAsync, CancellationToken cancellationToken)
    {
        ThrowIfDetached();

        if (Size > Array.MaxLength)
            throw new InvalidOperationException($"The entry '{RelativePath}' is too large to read into an array; use {nameof(Open)} instead.");

        // The declared size of compressed data is not trusted for the allocation: the buffer starts at a multiple of the compressed size and only
        // grows as data is actually decompressed.
        int capacity = IsCompressed ? (int)Math.Min(Size, Math.Max(c_MinInitialReadLength, CompressedSize * c_ExpectedMaxDeflateRatio)) : (int)Size;
        byte[] data = new byte[capacity];
        var stream = Data.Open();

        try
        {
            int total = 0;

            while (total < Size)
            {
                if (total == data.Length)
                    Array.Resize(ref data, (int)Math.Min(Size, (long)data.Length * 2));

                int read = useAsync
                    ? await stream.ReadAsync(data.AsMemory(total), cancellationToken).ConfigureAwait(false)
                    : stream.Read(data, total, data.Length - total);

                if (read == 0)
                    throw new InvalidDataException($"The bundle entry '{RelativePath}' ended after {total} bytes, but declares {Size} bytes.");

                total += read;
            }
        }
        finally
        {
            await StreamCopy.DisposeAsync(useAsync, stream).ConfigureAwait(false);
        }

        return data;
    }

    private void SetData(EntryData? data)
    {
        if (ReferenceEquals(data, Data))
            return;

        ReleaseData();

        if (data != null)
            Data = data;
    }

    private void ThrowIfDetached()
    {
        if (_bundle == null)
            throw new InvalidOperationException($"The entry '{RelativePath}' was removed from its bundle.");

        _bundle.ThrowIfDisposed();
    }

    /// <summary>
    /// Gets the size of the entry's original data as stored compressed in the opened file, or <c>0</c> if that data is stored uncompressed or
    /// <see cref="IsModified"/> is <see langword="true"/>.
    /// </summary>
    public long CompressedSize => IsModified ? 0 : Data.CompressedSize;

    /// <summary>
    /// Gets a value indicating whether the entry still has its original content and that content is stored compressed in the opened file.
    /// </summary>
    public bool IsCompressed => CompressedSize > 0;

    /// <summary>
    /// Gets a value indicating whether the entry was added or its content was replaced since the bundle was opened.
    /// </summary>
    public bool IsModified => !ReferenceEquals(Data, _original);

    /// <summary>
    /// Gets a value indicating whether the entry was added since the bundle was opened.
    /// </summary>
    public bool IsNew => _original == null;

    /// <summary>
    /// Gets the offset of the entry's data from the start of the opened file or stream, or <c>-1</c> if <see cref="IsModified"/> is <see langword="true"/>.
    /// </summary>
    public long Offset => IsModified ? -1 : Data.Offset;

    /// <summary>
    /// Gets the path of the file relative to the application directory, using <c>/</c> as the separator.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Gets the uncompressed size of the entry's current content, in bytes.
    /// </summary>
    /// <remarks>For an unchanged entry this is the size declared by the opened file; reading the content verifies it.</remarks>
    public long Size => Data.Size;

    /// <summary>
    /// Gets the type of the file.
    /// </summary>
    public BundleFileType Type { get; }

    /// <summary>
    /// Gets the content of the entry.
    /// </summary>
    internal EntryData Data { get; private set; }
}