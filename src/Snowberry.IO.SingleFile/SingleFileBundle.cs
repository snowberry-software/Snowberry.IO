using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Snowberry.IO.SingleFile.Content;
using Snowberry.IO.SingleFile.Format;
using Snowberry.IO.SingleFile.Format.Hosts;
using Snowberry.IO.SingleFile.Format.Layout;
using Snowberry.IO.SingleFile.Format.Manifests;
using Snowberry.IO.SingleFile.Format.Writing;
using Snowberry.IO.SingleFile.Sources;
using Snowberry.IO.SingleFile.Streams;
using Snowberry.IO.SingleFile.Threading;

namespace Snowberry.IO.SingleFile;

/// <summary>
/// The bundle of a single-file published .NET application: the files embedded in the executable and the manifest that describes them.
/// </summary>
/// <remarks>
/// <para>Opening a bundle reads the application host up to the bundle signature and the manifest; entry content is read on demand. A new bundle
/// is created empty, after an application host or without one, and filled with entries before it is saved.
/// Saving writes the destination in a single forward pass. Entries that are compressed while saving are compressed first and buffered until
/// they are written: in memory up to <see cref="BundleWriteOptions.MaxCompressionMemory"/>, and in temporary files beyond it.</para>
/// <para>Reading entries is thread-safe; reads of a bundle opened from a stream are serialized. Modifying entries and saving must not run
/// concurrently with any other operation on the bundle.</para>
/// </remarks>
public sealed class SingleFileBundle : IDisposable, IAsyncDisposable
{
    private const UnixFileMode c_DefaultExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly List<BundleEntry> _entries;
    private readonly Dictionary<string, BundleEntry> _entriesByPath;
    private readonly BundleLayout _layout;

    private readonly BundleSource _source;
    private bool _disposed;

    private SingleFileBundle(BundleSource source, BundleLayout layout)
    {
        _source = source;
        _layout = layout;

        var manifestEntries = layout.Manifest.Entries;
        _entries = new List<BundleEntry>(manifestEntries.Count);
        _entriesByPath = new Dictionary<string, BundleEntry>(manifestEntries.Count, StringComparer.Ordinal);

        foreach (var record in manifestEntries)
        {
            var data = new EntryData(source, record.Offset, record.Size, record.CompressedSize, record.RelativePath, ownsSource: false);
            var entry = new BundleEntry(this, record.RelativePath, record.Type, data, isOriginal: true);

            if (!_entriesByPath.TryAdd(record.RelativePath, entry))
                throw new InvalidDataException($"The bundle contains the path '{record.RelativePath}' more than once.");

            _entries.Add(entry);
        }

        Entries = new ReadOnlyCollection<BundleEntry>(_entries);
    }

    /// <summary>
    /// Creates an empty bundle without an application host, for working with the bundle format alone.
    /// </summary>
    /// <remarks>
    /// <para>Saving writes only the 8-byte manifest offset and the bundle signature in place of an application host, followed by the entries and the
    /// manifest. <see cref="Open(Stream, bool)"/> reads the result, but it is not an executable; use <see cref="Create(string, BundleHeader?)"/>
    /// to bundle the entries with an application host.</para>
    /// <para>Uncompressed assemblies are aligned to 4096 bytes, as for an unrecognized host; <see cref="BundleWriteOptions.AssemblyAlignment"/>
    /// overrides it.</para>
    /// </remarks>
    /// <param name="header">
    /// The header of the bundle, or <see langword="null"/> for format version 6.0, which .NET 6 and later use, without flags. Its
    /// <see cref="BundleHeader.Id"/> is replaced by the identifier computed when saving.
    /// </param>
    /// <returns>The empty bundle.</returns>
    /// <exception cref="ArgumentException"><paramref name="header"/> has an unsupported format version, or flags its format version does not have.</exception>
    public static SingleFileBundle Create(BundleHeader? header = null)
    {
        header = ValidateHeader(header);

        byte[] marker = new byte[BundleFormat.c_ManifestOffsetLength + BundleFormat.c_SignatureLength];
        BundleFormat.Signature.CopyTo(marker.AsSpan(BundleFormat.c_ManifestOffsetLength));

        return CreateCoreAsync(useAsync: false, new MemoryBundleSource(marker, nameof(SingleFileBundle)), header, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates an empty bundle after the application host in a file, as the .NET SDK bundler does.
    /// </summary>
    /// <remarks>
    /// <para>The host must contain the bundle signature with a manifest offset of <c>0</c>, like the <c>apphost</c> and <c>singlefilehost</c>
    /// templates of the .NET runtime packs. It is used as is, so preparing it, for example writing the application name into an <c>apphost</c>, is
    /// up to the caller. The entries are saved after the end of the host.</para>
    /// <para>Saving removes the code signature of the host, which the bundle invalidates: the Authenticode signature of a Windows host, and the
    /// code signature of a macOS host, which then gets a new ad-hoc signature unless <see cref="BundleWriteOptions.MacOSCodeSign"/> is
    /// <see langword="false"/>.</para>
    /// </remarks>
    /// <param name="hostPath">The path of the application host.</param>
    /// <param name="header">
    /// The header of the bundle, or <see langword="null"/> for format version 6.0, which .NET 6 and later use, without flags. Its
    /// <see cref="BundleHeader.Id"/> is replaced by the identifier computed when saving.
    /// </param>
    /// <returns>The empty bundle, which keeps the host file open for reading until it is disposed.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="hostPath"/> is <see langword="null"/> or empty, or <paramref name="header"/> has an unsupported format version, or flags its
    /// format version does not have.
    /// </exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to read the file.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not an application host without a bundle, or the host is not supported: a universal (fat) macOS executable, or a macOS
    /// executable whose load commands do not have the layout the .NET SDK requires.
    /// </exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static SingleFileBundle Create(string hostPath, BundleHeader? header = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostPath);
        header = ValidateHeader(header);
        return CreateCoreAsync(useAsync: false, FileBundleSource.Open(hostPath, useAsync: false), header, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates an empty bundle after the application host in a stream, as the .NET SDK bundler does.
    /// </summary>
    /// <remarks>
    /// <para>The host must contain the bundle signature with a manifest offset of <c>0</c>, like the <c>apphost</c> and <c>singlefilehost</c>
    /// templates of the .NET runtime packs. It is used as is, so preparing it, for example writing the application name into an <c>apphost</c>, is
    /// up to the caller. The entries are saved after the end of the host.</para>
    /// <para>Saving removes the code signature of the host, which the bundle invalidates: the Authenticode signature of a Windows host, and the
    /// code signature of a macOS host, which then gets a new ad-hoc signature unless <see cref="BundleWriteOptions.MacOSCodeSign"/> is
    /// <see langword="false"/>.</para>
    /// </remarks>
    /// <param name="host">A readable, seekable stream that contains the application host.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave <paramref name="host"/> open when the bundle is disposed or the stream does not contain a valid host;
    /// otherwise, <see langword="false"/>.
    /// </param>
    /// <param name="header">
    /// The header of the bundle, or <see langword="null"/> for format version 6.0, which .NET 6 and later use, without flags. Its
    /// <see cref="BundleHeader.Id"/> is replaced by the identifier computed when saving.
    /// </param>
    /// <returns>The empty bundle, which reads the host from <paramref name="host"/> until it is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="host"/> is not readable and seekable, or <paramref name="header"/> has an unsupported format version, or flags its format
    /// version does not have.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The stream does not contain an application host without a bundle, or the host is not supported: a universal (fat) macOS executable, or a
    /// macOS executable whose load commands do not have the layout the .NET SDK requires.
    /// </exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static SingleFileBundle Create(Stream host, bool leaveOpen = false, BundleHeader? header = null)
    {
        header = ValidateHeader(header);
        return CreateCoreAsync(useAsync: false, CreateStreamSource(host, leaveOpen), header, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="Create(string, BundleHeader?)"/>
    /// <remarks>
    /// <para>The host must contain the bundle signature with a manifest offset of <c>0</c>, like the <c>apphost</c> and <c>singlefilehost</c>
    /// templates of the .NET runtime packs. It is used as is, so preparing it, for example writing the application name into an <c>apphost</c>, is
    /// up to the caller. The entries are saved after the end of the host.</para>
    /// <para>Saving removes the code signature of the host, which the bundle invalidates: the Authenticode signature of a Windows host, and the
    /// code signature of a macOS host, which then gets a new ad-hoc signature unless <see cref="BundleWriteOptions.MacOSCodeSign"/> is
    /// <see langword="false"/>.</para>
    /// <para>The file is opened for asynchronous I/O. Argument and file-access errors are thrown directly, not through the returned task.</para>
    /// </remarks>
    /// <param name="hostPath">The path of the application host.</param>
    /// <param name="header">
    /// The header of the bundle, or <see langword="null"/> for format version 6.0, which .NET 6 and later use, without flags. Its
    /// <see cref="BundleHeader.Id"/> is replaced by the identifier computed when saving.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the empty bundle, which keeps the host file open for reading until it is disposed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<SingleFileBundle> CreateAsync(string hostPath, BundleHeader? header = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostPath);
        header = ValidateHeader(header);
        return CreateCoreAsync(useAsync: true, FileBundleSource.Open(hostPath, useAsync: true), header, cancellationToken).AsTask();
    }

    /// <inheritdoc cref="Create(Stream, bool, BundleHeader?)"/>
    /// <param name="host">A readable, seekable stream that contains the application host.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave <paramref name="host"/> open when the bundle is disposed or the stream does not contain a valid host;
    /// otherwise, <see langword="false"/>.
    /// </param>
    /// <param name="header">
    /// The header of the bundle, or <see langword="null"/> for format version 6.0, which .NET 6 and later use, without flags. Its
    /// <see cref="BundleHeader.Id"/> is replaced by the identifier computed when saving.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the empty bundle, which reads the host from <paramref name="host"/> until it is disposed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<SingleFileBundle> CreateAsync(Stream host, bool leaveOpen = false, BundleHeader? header = null, CancellationToken cancellationToken = default)
    {
        header = ValidateHeader(header);
        return CreateCoreAsync(useAsync: true, CreateStreamSource(host, leaveOpen), header, cancellationToken).AsTask();
    }

    /// <summary>
    /// Opens the bundle of a single-file application.
    /// </summary>
    /// <param name="path">The path of the executable.</param>
    /// <returns>The opened bundle, which keeps the file open for reading until it is disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to read the file.</exception>
    /// <exception cref="InvalidDataException">The file is not a single-file bundle, or its bundle is malformed or not supported.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static SingleFileBundle Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenCoreAsync(useAsync: false, FileBundleSource.Open(path, useAsync: false), CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Opens the bundle stored in a stream.
    /// </summary>
    /// <param name="stream">A readable, seekable stream that contains the executable.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave <paramref name="stream"/> open when the bundle is disposed or the stream does not contain a valid bundle;
    /// otherwise, <see langword="false"/>.
    /// </param>
    /// <returns>The opened bundle, which reads from <paramref name="stream"/> until it is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable and seekable.</exception>
    /// <exception cref="InvalidDataException">The stream does not contain a single-file bundle, or its bundle is malformed or not supported.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static SingleFileBundle Open(Stream stream, bool leaveOpen = false)
    {
        return OpenCoreAsync(useAsync: false, CreateStreamSource(stream, leaveOpen), CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="Open(string)"/>
    /// <remarks>The file is opened for asynchronous I/O. Argument and file-access errors are thrown directly, not through the returned task.</remarks>
    /// <param name="path">The path of the executable.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the opened bundle, which keeps the file open for reading until it is disposed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<SingleFileBundle> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenCoreAsync(useAsync: true, FileBundleSource.Open(path, useAsync: true), cancellationToken).AsTask();
    }

    /// <inheritdoc cref="Open(Stream, bool)"/>
    /// <param name="stream">A readable, seekable stream that contains the executable.</param>
    /// <param name="leaveOpen">
    /// <see langword="true"/> to leave <paramref name="stream"/> open when the bundle is disposed or the stream does not contain a valid bundle;
    /// otherwise, <see langword="false"/>.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the opened bundle, which reads from <paramref name="stream"/> until it is disposed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<SingleFileBundle> OpenAsync(Stream stream, bool leaveOpen = false, CancellationToken cancellationToken = default)
    {
        return OpenCoreAsync(useAsync: true, CreateStreamSource(stream, leaveOpen), cancellationToken).AsTask();
    }

    /// <summary>
    /// Tries to open the bundle of a single-file application.
    /// </summary>
    /// <param name="path">The path of the executable.</param>
    /// <param name="bundle">When this method returns <see langword="true"/>, the opened bundle; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the file contains a valid, supported single-file bundle; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to read the file.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public static bool TryOpen(string path, [NotNullWhen(true)] out SingleFileBundle? bundle)
    {
        try
        {
            bundle = Open(path);
            return true;
        }
        catch (InvalidDataException)
        {
            bundle = null;
            return false;
        }
    }

    private static async ValueTask<SingleFileBundle> CreateCoreAsync(bool useAsync, BundleSource source, BundleHeader header, CancellationToken cancellationToken)
    {
        try
        {
            var layout = await BundleLayoutReader.ReadHostTemplateAsync(useAsync, source, new Manifest(header, []), cancellationToken).ConfigureAwait(false);
            return new SingleFileBundle(source, layout);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static StreamBundleSource CreateStreamSource(Stream stream, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));

        return new StreamBundleSource(stream, leaveOpen, nameof(SingleFileBundle));
    }

    private static async ValueTask<SingleFileBundle> OpenCoreAsync(bool useAsync, BundleSource source, CancellationToken cancellationToken)
    {
        try
        {
            var layout = await BundleLayoutReader.ReadAsync(useAsync, source, cancellationToken).ConfigureAwait(false);
            return new SingleFileBundle(source, layout);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    private static BundleHeader ValidateHeader(BundleHeader? header)
    {
        header ??= new BundleHeader(6, 0, string.Empty, BundleFlags.None);

        if (!BundleFormat.IsSupportedMajorVersion(header.MajorVersion))
            throw new ArgumentException($"Bundle format version {header.MajorVersion} is not supported.", nameof(header));

        if (header.MajorVersion == 1 && header.Flags != BundleFlags.None)
            throw new ArgumentException("Bundle format version 1 does not have flags.", nameof(header));

        // Saving always replaces the identifier; a null one is replaced here so that Header.Id is never null.
        return header.Id == null ? header with { Id = string.Empty } : header;
    }

    /// <summary>
    /// Adds a file to the bundle.
    /// </summary>
    /// <remarks>Array-backed memory is referenced, not copied; do not change it until the bundle has been saved.</remarks>
    /// <param name="relativePath">
    /// The path relative to the application directory. <c>\</c> is converted to <c>/</c>. The path must not be longer than the application host
    /// accepts: 260 UTF-8 bytes for Windows hosts and 4096 otherwise.
    /// </param>
    /// <param name="type">The type of the file.</param>
    /// <param name="content">The content of the file.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="relativePath"/> is not a valid relative path, or the bundle already contains it.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined <see cref="BundleFileType"/> value.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public BundleEntry Add(string relativePath, BundleFileType type, ReadOnlyMemory<byte> content)
    {
        return AddCore(relativePath, type, path => EntryData.FromMemory(content, path));
    }

    /// <summary>
    /// Adds a file to the bundle, using the data of <paramref name="content"/> from its current position to its end.
    /// </summary>
    /// <remarks>
    /// A seekable stream is read when the bundle is saved, so it must stay open and unchanged until then.
    /// A non-seekable stream is copied into memory immediately, with a synchronous read.
    /// </remarks>
    /// <param name="relativePath">
    /// The path relative to the application directory. <c>\</c> is converted to <c>/</c>. The path must not be longer than the application host
    /// accepts: 260 UTF-8 bytes for Windows hosts and 4096 otherwise.
    /// </param>
    /// <param name="type">The type of the file.</param>
    /// <param name="content">The stream that provides the content.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="content"/> open after the entry no longer needs it; otherwise, <see langword="false"/>.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="relativePath"/> is not a valid relative path, the bundle already contains it, or <paramref name="content"/> is not readable.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined <see cref="BundleFileType"/> value.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    /// <exception cref="IOException">An I/O error occurred while copying a non-seekable <paramref name="content"/>.</exception>
    public BundleEntry Add(string relativePath, BundleFileType type, Stream content, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(content);
        return AddCore(relativePath, type, path => EntryData.FromStream(content, leaveOpen, path));
    }

    /// <summary>
    /// Adds the content of a file to the bundle.
    /// </summary>
    /// <remarks>
    /// The file is opened immediately and read when the bundle is saved. It stays open, readable by others, until the entry is removed, its content
    /// is replaced or the bundle is disposed.
    /// </remarks>
    /// <param name="relativePath">
    /// The path relative to the application directory. <c>\</c> is converted to <c>/</c>. The path must not be longer than the application host
    /// accepts: 260 UTF-8 bytes for Windows hosts and 4096 otherwise.
    /// </param>
    /// <param name="type">The type of the file.</param>
    /// <param name="sourceFileName">The path of the file to add.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="relativePath"/> is not a valid relative path or the bundle already contains it, or <paramref name="sourceFileName"/> is
    /// <see langword="null"/> or empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined <see cref="BundleFileType"/> value.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to read the file.</exception>
    /// <exception cref="IOException">An I/O error occurred while opening the file.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public BundleEntry AddFromFile(string relativePath, BundleFileType type, string sourceFileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceFileName);
        return AddCore(relativePath, type, path => EntryData.FromFile(sourceFileName, path));
    }

    /// <summary>
    /// Releases the opened file, the opened stream unless it was opened with <c>leaveOpen</c> set to <see langword="true"/>, and the files and streams
    /// of added or replaced content that were not passed with <c>leaveOpen</c> set to <see langword="true"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var entry in _entries)
            entry.ReleaseData();

        _source.Dispose();
    }

    /// <summary>
    /// Releases the opened file, the opened stream unless it was opened with <c>leaveOpen</c> set to <see langword="true"/>, and the files and streams
    /// of added or replaced content that were not passed with <c>leaveOpen</c> set to <see langword="true"/>.
    /// </summary>
    /// <returns>A task that completes when everything has been released.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var entry in _entries)
            await entry.ReleaseDataAsync().ConfigureAwait(false);

        await _source.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Extracts every entry into a directory, preserving the relative paths.
    /// </summary>
    /// <remarks>
    /// All paths are validated before anything is written: entry paths come from the file, and a path that would resolve outside of
    /// <paramref name="destinationDirectory"/>, such as <c>../x</c> or an absolute path, or two paths that resolve to the same file make the
    /// extraction fail. Paths are compared ignoring case on Windows and macOS, without Unicode normalization. Symbolic links that already exist in
    /// <paramref name="destinationDirectory"/> are followed. Entries are written in parallel; if one fails, files already written are kept.
    /// </remarks>
    /// <param name="destinationDirectory">The directory to extract into; it and the subdirectories of the entries are created as needed.</param>
    /// <param name="overwrite"><see langword="true"/> to overwrite existing files; otherwise, <see langword="false"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="destinationDirectory"/> is <see langword="null"/>, empty or not a valid path.</exception>
    /// <exception cref="InvalidDataException">
    /// An entry path does not resolve to a file inside of <paramref name="destinationDirectory"/>, two entry paths resolve to the same file, or the
    /// stored data of an entry does not match its declared size.
    /// </exception>
    /// <exception cref="IOException">A file exists and <paramref name="overwrite"/> is <see langword="false"/>, or an I/O error occurred.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to write to <paramref name="destinationDirectory"/>.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public void ExtractToDirectory(string destinationDirectory, bool overwrite = false)
    {
        ExtractToDirectoryCoreAsync(useAsync: false, destinationDirectory, overwrite, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="ExtractToDirectory(string, bool)"/>
    /// <param name="destinationDirectory">The directory to extract into; it and the subdirectories of the entries are created as needed.</param>
    /// <param name="overwrite"><see langword="true"/> to overwrite existing files; otherwise, <see langword="false"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when all entries have been extracted.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task ExtractToDirectoryAsync(string destinationDirectory, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        return ExtractToDirectoryCoreAsync(useAsync: true, destinationDirectory, overwrite, cancellationToken).AsTask();
    }

    /// <summary>
    /// Gets the entry with the given relative path.
    /// </summary>
    /// <param name="relativePath">The path relative to the application directory, using <c>/</c> as the separator. The comparison is case-sensitive.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="KeyNotFoundException">The bundle has no entry with the given path.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public BundleEntry GetEntry(string relativePath)
    {
        return TryGetEntry(relativePath, out var entry)
            ? entry
            : throw new KeyNotFoundException($"The bundle has no entry '{relativePath}'.");
    }

    /// <summary>
    /// Removes the entry with the given relative path.
    /// </summary>
    /// <param name="relativePath">The path relative to the application directory, using <c>/</c> as the separator.</param>
    /// <returns><see langword="true"/> if the entry was removed; <see langword="false"/> if the bundle has no entry with the given path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public bool Remove(string relativePath)
    {
        return TryGetEntry(relativePath, out var entry) && Remove(entry);
    }

    /// <summary>
    /// Removes an entry from the bundle.
    /// </summary>
    /// <param name="entry">The entry to remove.</param>
    /// <returns><see langword="true"/> if the entry was removed; <see langword="false"/> if it does not belong to this bundle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public bool Remove(BundleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ThrowIfDisposed();

        if (!_entriesByPath.TryGetValue(entry.RelativePath, out var existing) || !ReferenceEquals(existing, entry))
            return false;

        _entriesByPath.Remove(entry.RelativePath);
        _entries.Remove(entry);
        entry.Detach();
        return true;
    }

    /// <summary>
    /// Removes every entry that matches a condition.
    /// </summary>
    /// <param name="match">The condition entries to remove must satisfy.</param>
    /// <returns>The number of removed entries.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public int RemoveAll(Predicate<BundleEntry> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        ThrowIfDisposed();

        var removed = _entries.FindAll(match);

        foreach (var entry in removed)
            Remove(entry);

        return removed.Count;
    }

    /// <summary>
    /// Saves the application, including all changes, to a file.
    /// </summary>
    /// <remarks>
    /// <para>The file is written to a temporary file next to <paramref name="path"/> and moved into place when complete. On Linux and macOS it gets
    /// the permissions of the file the bundle was opened or created from, or <c>rwxr-xr-x</c> for a bundle opened or created from a stream or
    /// created without an application host.</para>
    /// <para>Data that followed the manifest in the opened file, such as an Authenticode signature, is not written, and a reference to it in the
    /// Windows application host is removed; sign the saved executable again if needed. A macOS executable gets a new ad-hoc code signature
    /// whose identifier is the file name of <paramref name="path"/>, as the .NET SDK signs it; see <see cref="BundleWriteOptions.MacOSCodeSign"/>.</para>
    /// </remarks>
    /// <param name="path">The path of the executable to create or overwrite. It must differ from the path the bundle was opened from.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="BundleWriteOptions.Default"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="IOException">
    /// <paramref name="path"/> is the file the bundle was opened from, a stream passed to an added or replaced entry changed length, or an I/O
    /// error occurred. When the bundle was opened from a stream over the same file, the save fails with an I/O error instead.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The caller does not have permission to write <paramref name="path"/>.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    /// <exception cref="InvalidDataException">The stored data of an entry does not match its declared size.</exception>
    /// <exception cref="InvalidOperationException">
    /// The bundle has no entries, <see cref="BundleWriteOptions.Compression"/> is <see cref="BundleCompression.Compress"/>, the bundle format
    /// supports compression and the application is framework-dependent, or the macOS executable cannot hold the bundle and its code signature:
    /// they would end beyond 4 GiB, or the load commands have no room for a code signature command.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The application host is a macOS executable whose load commands do not have the layout the .NET SDK requires, or whose bundle does not
    /// follow the start of its string table.
    /// </exception>
    public void Save(string path, BundleWriteOptions? options = null)
    {
        SaveToFileCoreAsync(useAsync: false, path, options, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Saves the application, including all changes, to a stream.
    /// </summary>
    /// <remarks>
    /// <para>The stream is written in a single forward pass, so it does not need to be seekable. It is left open.</para>
    /// <para>Data that followed the manifest in the opened file, such as an Authenticode signature, is not written, and a reference to it in the
    /// Windows application host is removed; sign the saved executable again if needed. A macOS executable gets a new ad-hoc code signature,
    /// as the .NET SDK signs it, whose identifier is <see cref="BundleWriteOptions.MacOSCodeSignIdentifier"/> or, for an opened bundle, that
    /// of its current signature; see <see cref="BundleWriteOptions.MacOSCodeSign"/>.</para>
    /// </remarks>
    /// <param name="destination">The writable stream to write the application to.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="BundleWriteOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="IOException">A stream passed to an added or replaced entry changed length, or an I/O error occurred.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    /// <exception cref="InvalidDataException">The stored data of an entry does not match its declared size.</exception>
    /// <exception cref="InvalidOperationException">
    /// The bundle has no entries, <see cref="BundleWriteOptions.Compression"/> is <see cref="BundleCompression.Compress"/>, the bundle format
    /// supports compression and the application is framework-dependent, or the macOS executable cannot hold the bundle and its code signature:
    /// they would end beyond 4 GiB, the load commands have no room for a code signature command, or no signing identifier is known because
    /// <see cref="BundleWriteOptions.MacOSCodeSignIdentifier"/> is <see langword="null"/> and the bundle was created or the executable is not
    /// signed.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The application host is a macOS executable whose load commands do not have the layout the .NET SDK requires, or whose bundle does not
    /// follow the start of its string table.
    /// </exception>
    public void Save(Stream destination, BundleWriteOptions? options = null)
    {
        SaveCoreAsync(useAsync: false, destination, options, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="Save(string, BundleWriteOptions?)"/>
    /// <param name="path">The path of the executable to create or overwrite. It must differ from the path the bundle was opened from.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="BundleWriteOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file has been written.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task SaveAsync(string path, BundleWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        return SaveToFileCoreAsync(useAsync: true, path, options, cancellationToken).AsTask();
    }

    /// <inheritdoc cref="Save(Stream, BundleWriteOptions?)"/>
    /// <param name="destination">The writable stream to write the application to.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="BundleWriteOptions.Default"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the application has been written.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task SaveAsync(Stream destination, BundleWriteOptions? options = null, CancellationToken cancellationToken = default)
    {
        return SaveCoreAsync(useAsync: true, destination, options, cancellationToken).AsTask();
    }

    /// <summary>
    /// Gets the entry with the given relative path, if it exists.
    /// </summary>
    /// <param name="relativePath">The path relative to the application directory, using <c>/</c> as the separator. The comparison is case-sensitive.</param>
    /// <param name="entry">When this method returns <see langword="true"/>, the entry; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the bundle has an entry with the given path; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The bundle is disposed.</exception>
    public bool TryGetEntry(string relativePath, [NotNullWhen(true)] out BundleEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ThrowIfDisposed();

        return _entriesByPath.TryGetValue(relativePath, out entry);
    }

    /// <summary>
    /// Throws if the bundle has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The bundle has been disposed.</exception>
    internal void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private BundleEntry AddCore(string relativePath, BundleFileType type, Func<string, EntryData> createData)
    {
        ThrowIfDisposed();

        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type), type, $"Unknown {nameof(BundleFileType)} value.");

        int maxPathBytes = _layout.Host.Kind == HostKind.PortableExecutable ? BundlePath.c_MaxWindowsPathBytes : BundlePath.c_MaxUnixPathBytes;
        string normalized = BundlePath.Normalize(relativePath, maxPathBytes, nameof(relativePath));

        if (_entriesByPath.ContainsKey(normalized))
            throw new ArgumentException($"The bundle already contains '{normalized}'.", nameof(relativePath));

        var entry = new BundleEntry(this, normalized, type, createData(normalized), isOriginal: false);
        _entriesByPath.Add(normalized, entry);
        _entries.Add(entry);
        return entry;
    }

    private async ValueTask ExtractToDirectoryCoreAsync(bool useAsync, string destinationDirectory, bool overwrite, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);
        ThrowIfDisposed();

        string root = Path.GetFullPath(destinationDirectory);

        // Windows and macOS file systems are usually case-insensitive, so paths that differ only in case would overwrite each other.
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var targetPaths = new HashSet<string>(comparer);
        var targets = new List<(BundleEntry Entry, string Path)>(_entries.Count);

        string rootPrefix = BundlePath.GetRootPrefix(root);

        foreach (var entry in _entries)
        {
            string fullPath = entry.GetSafeOutputPath(rootPrefix);

            if (!targetPaths.Add(fullPath))
                throw new InvalidDataException($"The bundle entry path '{entry.RelativePath}' resolves to the same file as another entry.");

            targets.Add((entry, fullPath));
        }

        Directory.CreateDirectory(root);

        foreach (string directory in targets.Select(t => Path.GetDirectoryName(t.Path)!).Distinct(comparer))
            Directory.CreateDirectory(directory);

        // Largest first, so a big entry does not start last and leave the other workers idle.
        targets.Sort((a, b) => b.Entry.Size.CompareTo(a.Entry.Size));

        await ParallelWork.ForEachAsync(useAsync, targets, maxDegreeOfParallelism: Environment.ProcessorCount, async (target, token) =>
        {
            await target.Entry.ExtractToFileCoreAsync(useAsync, target.Path, overwrite, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Determines from the bundled <c>runtimeconfig.json</c> whether the application runs on a shared framework.
    /// </summary>
    /// <returns><see langword="true"/> if the application is framework-dependent; <see langword="false"/> if it is self-contained or unknown.</returns>
    private async ValueTask<bool> IsFrameworkDependentAsync(bool useAsync, CancellationToken cancellationToken)
    {
        var runtimeConfig = RuntimeConfigJson;

        if (runtimeConfig == null || runtimeConfig.Size > 1024 * 1024)
            return false;

        try
        {
            byte[] json = useAsync
                ? await runtimeConfig.ReadAllBytesAsync(cancellationToken).ConfigureAwait(false)
                : runtimeConfig.ReadAllBytes();

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });

            if (!document.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions) || runtimeOptions.ValueKind != JsonValueKind.Object)
                return false;

            // Self-contained applications list "includedFrameworks"; framework-dependent ones list "framework" or "frameworks".
            return !runtimeOptions.TryGetProperty("includedFrameworks", out _)
                && (runtimeOptions.TryGetProperty("framework", out _) || runtimeOptions.TryGetProperty("frameworks", out _));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return false;
        }
    }

    private async ValueTask SaveCoreAsync(bool useAsync, Stream destination, BundleWriteOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
            throw new ArgumentException("The stream must be writable.", nameof(destination));

        options ??= BundleWriteOptions.Default;
        await ValidateSaveAsync(useAsync, options, cancellationToken).ConfigureAwait(false);

        await BundleWriter.WriteAsync(useAsync, _source, _layout, _entries, IsModified, destination, options, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SaveToFileCoreAsync(bool useAsync, string path, BundleWriteOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= BundleWriteOptions.Default;
        await ValidateSaveAsync(useAsync, options, cancellationToken).ConfigureAwait(false);

        string fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (_source.FilePath != null && string.Equals(_source.FilePath, fullPath, comparison))
            throw new IOException("The bundle cannot be saved over the file it was opened from; save it to another path.");

        string tempPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var plan = await BundleWriter.PlanAsync(useAsync, _source, _layout, _entries, IsModified, options, Path.GetFileName(fullPath), cancellationToken).ConfigureAwait(false);

        try
        {
            // The planned length lets the file system reserve the whole file up front; the file still ends where writing ends.
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 1,
                Options = useAsync ? FileOptions.Asynchronous : FileOptions.None,
                PreallocationSize = plan.Length
            };

            if (!OperatingSystem.IsWindows())
                streamOptions.UnixCreateMode = (_source as FileBundleSource)?.UnixFileMode ?? c_DefaultExecutableMode;

            var stream = new FileStream(tempPath, streamOptions);

            try
            {
                await BundleWriter.WriteAsync(useAsync, plan, stream, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await StreamCopy.DisposeAsync(useAsync, stream).ConfigureAwait(false);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Keep the original exception; a leftover temporary file is harmless.
            }

            throw;
        }
        finally
        {
            await BundleWriter.DisposeAsync(useAsync, plan).ConfigureAwait(false);
        }
    }

    private async ValueTask ValidateSaveAsync(bool useAsync, BundleWriteOptions options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        if (_layout.Host.Kind == HostKind.MachO && _layout.Host.MachO == null)
            throw new NotSupportedException("The load commands of the macOS executable do not have the layout the .NET SDK requires, so the bundle cannot be saved.");

        // The .NET host refuses to run a bundle without files.
        if (_entries.Count == 0)
            throw new InvalidOperationException("A bundle must contain at least one entry.");

        if (options.Compression == BundleCompression.Compress && Header.SupportsCompression
            && await IsFrameworkDependentAsync(useAsync, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"{nameof(BundleCompression)}.{nameof(BundleCompression.Compress)} requires a self-contained application; the .NET host cannot read compressed entries of a framework-dependent application.");
        }
    }

    /// <summary>
    /// Gets the application's <c>*.deps.json</c> entry, or <see langword="null"/> if the bundle has none.
    /// </summary>
    public BundleEntry? DepsJson => _entries.Find(e => e.Type == BundleFileType.DepsJson);

    /// <summary>
    /// Gets the entries of the bundle, in the order they are saved.
    /// </summary>
    /// <remarks>
    /// The list reflects later <see cref="Add(string, BundleFileType, ReadOnlyMemory{byte})"/> and <see cref="Remove(BundleEntry)"/> calls, so it must not
    /// be enumerated while entries are added or removed; use <see cref="RemoveAll(Predicate{BundleEntry})"/> to remove entries by condition.
    /// </remarks>
    public IReadOnlyList<BundleEntry> Entries { get; }

    /// <summary>
    /// Gets the manifest header of the opened or created bundle.
    /// </summary>
    /// <remarks>
    /// The header describes the opened file, or the header a bundle was created with; saving writes a new identifier when content changed, but does
    /// not update this instance.
    /// </remarks>
    public BundleHeader Header => _layout.Manifest.Header;

    /// <summary>
    /// Gets a value indicating whether the bundle's entries differ from the opened file: entries were added, removed or replaced.
    /// </summary>
    public bool IsModified => _entries.Count != _layout.Manifest.Entries.Count || _entries.Exists(e => e.IsModified);

    /// <summary>
    /// Gets the application's <c>*.runtimeconfig.json</c> entry, or <see langword="null"/> if the bundle has none.
    /// </summary>
    public BundleEntry? RuntimeConfigJson => _entries.Find(e => e.Type == BundleFileType.RuntimeConfigJson);
}