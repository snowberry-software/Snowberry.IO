using Snowberry.IO.SingleFile.Format.Layout;
using Snowberry.IO.SingleFile.Sources;
using Snowberry.IO.SingleFile.Streams;

namespace Snowberry.IO.SingleFile.Format.Writing;

/// <summary>
/// The planned layout of a bundle that is about to be written, with the entries that were compressed in advance and the content hashes that
/// are computed while the bundle is written.
/// </summary>
/// <remarks>Disposing stops the background work that is still running and releases the compressed data that was not written.</remarks>
/// <param name="source">The source the original bundle was read from.</param>
/// <param name="layout">The layout of the original bundle.</param>
/// <param name="contentChanged">Whether the content of any entry changed, so the bundle identifier must be recomputed.</param>
/// <param name="entries">The planned entries, in order.</param>
/// <param name="spillFiles">The temporary files that hold compressed data beyond the memory budget.</param>
internal sealed class BundlePlan(BundleSource source, BundleLayout layout, bool contentChanged, List<PlannedEntry> entries, SpillFilePool spillFiles)
    : IDisposable, IAsyncDisposable
{
    private Task? _background;
    private CancellationTokenSource? _backgroundCancellation;
    private CancellationToken _cancellationToken;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_background != null)
        {
            _backgroundCancellation!.Cancel();

            // The save failed or completed already, so a failure of the background work is not reported again.
            _background.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult();
        }

        Release();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_background != null)
        {
            await _backgroundCancellation!.CancelAsync().ConfigureAwait(false);
            await _background.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        Release();
    }

    /// <summary>
    /// Starts work that computes content hashes in the background, while the plan is completed and the bundle is written.
    /// </summary>
    /// <param name="work">Starts the work; it receives a token that is canceled with <paramref name="cancellationToken"/> or when the plan is disposed.</param>
    /// <param name="cancellationToken">The token of the save operation.</param>
    public void StartBackgroundWork(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _backgroundCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _background = work(_backgroundCancellation.Token);
    }

    /// <summary>
    /// Waits for <paramref name="task"/>, part of the background work, and rethrows its failure, reporting a cancellation of the save with the
    /// caller's token rather than the linked one the work observes.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to wait asynchronously; <see langword="false"/> to block.</param>
    /// <param name="task">The task to wait for.</param>
    /// <returns>A task that completes when <paramref name="task"/> has completed.</returns>
    /// <exception cref="OperationCanceledException">The token of the save operation was canceled.</exception>
    public async ValueTask WaitAsync(bool useAsync, Task task)
    {
        try
        {
            if (useAsync)
                await task.ConfigureAwait(false);
            else
                task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    /// <summary>
    /// Waits until the work started by <see cref="StartBackgroundWork"/> is complete, so all hashes are known, and rethrows its failure.
    /// </summary>
    /// <param name="useAsync"><see langword="true"/> to wait asynchronously; <see langword="false"/> to block.</param>
    /// <returns>A task that completes when the work is complete.</returns>
    /// <exception cref="OperationCanceledException">The token of the save operation was canceled.</exception>
    public async ValueTask WaitForBackgroundWorkAsync(bool useAsync)
    {
        if (_background != null)
            await WaitAsync(useAsync, _background).ConfigureAwait(false);
    }

    private void Release()
    {
        // Observing a failure keeps it from being reported as unobserved; the save reported it already or failed for another reason.
        _ = _background?.Exception;
        _backgroundCancellation?.Dispose();
        _backgroundCancellation = null;
        _background = null;
        Workers?.Dispose();

        foreach (var planned in entries)
        {
            planned.Buffer?.Dispose();
            planned.Buffer = null;
        }

        spillFiles.Dispose();
    }

    /// <summary>
    /// Gets the work started by <see cref="StartBackgroundWork"/>, or <see langword="null"/> if none was started.
    /// </summary>
    public Task? BackgroundWork => _background;

    /// <summary>
    /// Gets a value indicating whether the work started by <see cref="StartBackgroundWork"/> failed or was canceled.
    /// </summary>
    public bool BackgroundWorkFailed => _background is { IsFaulted: true } or { IsCanceled: true };

    /// <summary>
    /// Gets a value indicating whether the content of any entry changed, so the bundle identifier must be recomputed.
    /// </summary>
    public bool ContentChanged => contentChanged;

    /// <summary>
    /// Gets the planned entries, in order.
    /// </summary>
    public List<PlannedEntry> Entries => entries;

    /// <summary>
    /// Gets the layout of the original bundle.
    /// </summary>
    public BundleLayout Layout => layout;

    /// <summary>
    /// Gets the length of the bundle that is written.
    /// </summary>
    public long Length => MachO?.End ?? (ManifestOffset + ManifestLength);

    /// <summary>
    /// Gets or sets how the load commands and the code signature of a macOS host are written, or <see langword="null"/> for other hosts.
    /// </summary>
    public MachOPlan? MachO { get; set; }

    /// <summary>
    /// Gets or sets the length of the manifest.
    /// </summary>
    public int ManifestLength { get; set; }

    /// <summary>
    /// Gets or sets the offset of the manifest, which follows the last entry.
    /// </summary>
    public long ManifestOffset { get; set; }

    /// <summary>
    /// Gets the maximum number of entries that are processed concurrently, or <c>-1</c> for <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public required int MaxDegreeOfParallelism { get; init; }

    /// <summary>
    /// Gets the memory budget for buffered entry data, shared by the entries compressed in advance and those decompressed ahead of the writer.
    /// </summary>
    public required MemoryBudget Memory { get; init; }

    /// <summary>
    /// Gets the total size of <see cref="Memory"/>.
    /// </summary>
    public required long MemoryLimit { get; init; }

    /// <summary>
    /// Gets the source the original bundle was read from.
    /// </summary>
    public BundleSource Source => source;

    /// <summary>
    /// Gets the temporary files that hold compressed data beyond the memory budget.
    /// </summary>
    public SpillFilePool SpillFiles => spillFiles;

    /// <summary>
    /// Gets the slots that the work running beside the writer, hashing and decompressing ahead, takes per entry, so at most
    /// <see cref="MaxDegreeOfParallelism"/> entries are processed at a time; <see langword="null"/> when nothing runs beside the writer.
    /// </summary>
    public SemaphoreSlim? Workers { get; init; }
}