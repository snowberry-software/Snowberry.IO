using System.Security.Cryptography;
using Snowberry.IO.SingleFile.Streams;

namespace Snowberry.IO.SingleFile.Format.Writing;

/// <summary>
/// Decompresses the entries that are written decompressed ahead of the writer, in parallel and in their order, into pooled buffers.
/// </summary>
/// <remarks>
/// <para>Entries are started in order while fewer than <paramref name="maxInFlight"/> are decompressed or waiting to be written and the budget
/// covers the buffer of the next one, so the decompressed data held in memory stays within the budget and never spills to a file. Each
/// decompression takes a slot of <paramref name="workers"/>, which the other work beside the writer shares. The writer
/// takes the buffers in the same order; an entry that was not started when the writer reaches it, for example because it is larger than the
/// whole budget, is decompressed by the writer itself.</para>
/// <para>The pipeline is used by the writer's thread only; the decompression runs on the thread pool, also for the synchronous API.</para>
/// </remarks>
/// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
/// <param name="entries">The entries that are written decompressed, in the order they are written.</param>
/// <param name="needsHash">Whether the content hashes that are not known yet must be computed.</param>
/// <param name="budget">The memory shared with the buffers of the entries compressed in advance.</param>
/// <param name="budgetLimit">The total size of <paramref name="budget"/>.</param>
/// <param name="spillFiles">The temporary files of the save; a buffer of the pipeline never uses them, because it reserves its memory up front.</param>
/// <param name="workers">The slots for the work beside the writer, one per entry being processed.</param>
/// <param name="maxInFlight">The most entries that are decompressed or waiting to be written at a time.</param>
/// <param name="cancellationToken">The token of the save operation.</param>
internal sealed class DecompressionPipeline(
    bool useAsync,
    List<PlannedEntry> entries,
    bool needsHash,
    MemoryBudget budget,
    long budgetLimit,
    SpillFilePool spillFiles,
    SemaphoreSlim workers,
    int maxInFlight,
    CancellationToken cancellationToken) : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    private readonly long[] _reserved = new long[entries.Count];
    private readonly Task<SpillBuffer>?[] _tasks = new Task<SpillBuffer>?[entries.Count];
    private int _inFlight;
    private int _next;
    private int _taken;

    /// <summary>
    /// Cancels the decompression that is still running, waits for it and releases its buffers.
    /// </summary>
    public void Dispose()
    {
        _cancellation.Cancel();

        for (int i = 0; i < _tasks.Length; i++)
        {
            if (_tasks[i] is { } task)
            {
                ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult();
                ReleaseTask(i, task);
            }
        }

        _cancellation.Dispose();
    }

    /// <summary>
    /// Cancels the decompression that is still running, waits for it asynchronously and releases its buffers.
    /// </summary>
    /// <returns>A task that completes when everything has been released.</returns>
    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);

        for (int i = 0; i < _tasks.Length; i++)
        {
            if (_tasks[i] is { } task)
            {
                await ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                ReleaseTask(i, task);
            }
        }

        _cancellation.Dispose();
    }

    /// <summary>
    /// Returns the memory of the entry taken last to the budget and starts the next entries.
    /// </summary>
    public void Release()
    {
        ReleaseReservation(_taken - 1);
        Start();
    }

    /// <summary>
    /// Starts decompressing the next entries as far as the budget and the number of entries in flight allow.
    /// </summary>
    public void Start()
    {
        while (_next < entries.Count && _inFlight < maxInFlight)
        {
            var planned = entries[_next];

            // An entry that does not fit even into the whole budget is left to the writer, and the entries after it are started meanwhile.
            long footprint = planned.Size > budgetLimit ? long.MaxValue : SpillBuffer.GetMemoryFootprint(planned.Size);

            if (footprint > budgetLimit)
            {
                _next++;
                continue;
            }

            if (!budget.TryReserve(footprint))
                return;

            int index = _next++;
            var token = _cancellation.Token;
            _reserved[index] = footprint;
            _inFlight++;
            _tasks[index] = Task.Run(() => DecompressAsync(planned, footprint, token), CancellationToken.None);
        }
    }

    /// <summary>
    /// Takes the decompressed content of the next entry; every call is followed by <see cref="Release"/> once the entry is written.
    /// </summary>
    /// <param name="planned">The next entry in the order of the pipeline.</param>
    /// <returns>The buffer with the content, which the caller disposes before calling <see cref="Release"/>, or <see langword="null"/> if the
    /// caller must decompress the entry itself.</returns>
    public async ValueTask<SpillBuffer?> TakeAsync(PlannedEntry planned)
    {
        int index = _taken++;

        if (!ReferenceEquals(entries[index], planned))
            throw new InvalidOperationException("The entries were taken out of order.");

        // Everything before this entry is written, so the whole budget may be free to start it now.
        if (index >= _next)
            Start();

        if (index >= _next || _tasks[index] is not { } task)
        {
            _next = Math.Max(_next, index + 1);
            return null;
        }

        _tasks[index] = null;
        _inFlight--;

        try
        {
            return useAsync ? await task.ConfigureAwait(false) : task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The decompression observes a linked token; report the caller's.
            ReleaseReservation(index);
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            ReleaseReservation(index);
            throw;
        }
    }

    private async Task<SpillBuffer> DecompressAsync(PlannedEntry planned, long footprint, CancellationToken token)
    {
        // The buffer has a budget of its own that covers the declared size, so it neither spills nor charges the shared budget twice, and the
        // content is decompressed straight into its segments.
        await workers.WaitAsync(token).ConfigureAwait(false);
        var buffer = new SpillBuffer(new MemoryBudget(footprint), spillFiles, planned.Size);

        try
        {
            using var hash = needsHash && planned.Hash == null ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            var content = planned.Entry.Data.Open();

            try
            {
                long copied = await buffer.CopyFromAsync(useAsync, content, hash, token).ConfigureAwait(false);
                planned.EnsureContentLength(copied);
            }
            finally
            {
                await StreamCopy.DisposeAsync(useAsync, content).ConfigureAwait(false);
            }

            if (hash != null)
                planned.SetHash(hash.GetHashAndReset());

            buffer.Complete();
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        finally
        {
            workers.Release();
        }
    }

    private void ReleaseReservation(int index)
    {
        budget.Release(_reserved[index]);
        _reserved[index] = 0;
    }

    private void ReleaseTask(int index, Task<SpillBuffer> task)
    {
        if (task.IsCompletedSuccessfully)
            task.Result.Dispose();
        else
            _ = task.Exception; // The save already failed; observing the exception keeps it from being reported as unobserved.

        _tasks[index] = null;
        ReleaseReservation(index);
    }
}