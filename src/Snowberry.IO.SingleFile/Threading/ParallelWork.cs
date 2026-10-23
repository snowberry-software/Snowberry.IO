using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Snowberry.IO.SingleFile.Threading;

/// <summary>
/// Runs work items in parallel with one implementation for the synchronous and asynchronous APIs.
/// </summary>
internal static class ParallelWork
{
    /// <summary>
    /// Runs <paramref name="body"/> for every item, in parallel.
    /// </summary>
    /// <remarks>
    /// <para>Items are handed out one at a time in list order, so items sorted by descending cost keep every worker busy until the end.</para>
    /// <para>When <paramref name="useAsync"/> is <see langword="false"/>, the calling thread blocks until all items are processed, and the first failure
    /// is rethrown as is instead of wrapped in an <see cref="AggregateException"/>, like awaiting the asynchronous variant.</para>
    /// </remarks>
    /// <typeparam name="T">The type of the items.</typeparam>
    /// <param name="useAsync"><see langword="true"/> to use asynchronous I/O; <see langword="false"/> to complete synchronously.</param>
    /// <param name="items">The items to process.</param>
    /// <param name="maxDegreeOfParallelism">The maximum number of concurrent items, or <c>-1</c> for <see cref="Environment.ProcessorCount"/>.</param>
    /// <param name="body">The work for one item.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when all items have been processed.</returns>
    public static ValueTask ForEachAsync<T>(bool useAsync, IReadOnlyList<T> items, int maxDegreeOfParallelism, Func<T, CancellationToken, ValueTask> body, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
            return ValueTask.CompletedTask;

        // Parallel.ForEach treats -1 as unbounded, while Parallel.ForEachAsync uses the processor count; both use the processor count here.
        int degree = maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = cancellationToken };

        if (useAsync)
            return new ValueTask(Parallel.ForEachAsync(items, parallelOptions, body));

        try
        {
            // A list would be split into index ranges, which can leave the largest items queued behind each other in one range.
            var partitioner = Partitioner.Create(items, EnumerablePartitionerOptions.NoBuffering);
            Parallel.ForEach(partitioner, parallelOptions, item => body(item, cancellationToken).GetAwaiter().GetResult());
        }
        catch (AggregateException ex)
        {
            // Like awaiting the asynchronous variant, surface the first failure.
            ExceptionDispatchInfo.Throw(ex.Flatten().InnerExceptions[0]);
        }

        return ValueTask.CompletedTask;
    }
}