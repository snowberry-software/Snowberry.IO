namespace Snowberry.IO.SingleFile.Streams;

/// <summary>
/// A thread-safe number of bytes that buffers may hold in memory.
/// </summary>
/// <param name="limit">The number of bytes available.</param>
internal sealed class MemoryBudget(long limit)
{
    private long _remaining = limit;

    /// <summary>
    /// Returns previously reserved bytes.
    /// </summary>
    /// <param name="bytes">The number of bytes to return.</param>
    public void Release(long bytes)
    {
        if (bytes > 0)
            Interlocked.Add(ref _remaining, bytes);
    }

    /// <summary>
    /// Reserves <paramref name="bytes"/> if that many are still available.
    /// </summary>
    /// <param name="bytes">The number of bytes to reserve.</param>
    /// <returns><see langword="true"/> if the bytes were reserved; otherwise, <see langword="false"/>.</returns>
    public bool TryReserve(long bytes)
    {
        long current;

        do
        {
            current = Volatile.Read(ref _remaining);

            if (current < bytes)
                return false;
        }
        while (Interlocked.CompareExchange(ref _remaining, current - bytes, current) != current);

        return true;
    }
}