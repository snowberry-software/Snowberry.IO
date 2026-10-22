using AwesomeAssertions;
using Snowberry.IO.SingleFile.Threading;
using Xunit;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for <see cref="ParallelWork"/>.
/// </summary>
public class ParallelWorkTests
{
    private static void InterlockedMax(ref int target, int value)
    {
        int current;

        do
        {
            current = Volatile.Read(ref target);

            if (value <= current)
                return;
        }
        while (Interlocked.CompareExchange(ref target, value, current) != current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForEachAsync_FirstItemBlocks_OtherWorkerTakesRemainingItemsInListOrder(bool useAsync)
    {
        const int c_Count = 40;
        int[] items = [.. Enumerable.Range(0, c_Count)];
        using var othersDone = new CountdownEvent(c_Count - 1);
        var order = new List<int>();

        await ParallelWork.ForEachAsync(useAsync, items, 2, (item, _) =>
        {
            if (item == 0)
            {
                // The worker that takes the first item keeps it until the other worker has processed all remaining items.
                othersDone.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
                return ValueTask.CompletedTask;
            }

            lock (order)
                order.Add(item);

            othersDone.Signal();
            return ValueTask.CompletedTask;
        }, TestContext.Current.CancellationToken);

        // Items are handed out one at a time; index ranges would let the other worker start in the middle of the list.
        order.Should().Equal(Enumerable.Range(1, c_Count - 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForEachAsync_ItemFails_ThrowsItsExceptionUnwrapped(bool useAsync)
    {
        int[] items = [.. Enumerable.Range(0, 16)];

        await FluentActions.Awaiting(() => ParallelWork.ForEachAsync(useAsync, items, 4, (item, _) =>
                item == 5 ? throw new InvalidDataException("item 5") : ValueTask.CompletedTask, TestContext.Current.CancellationToken).AsTask())
            .Should().ThrowExactlyAsync<InvalidDataException>().WithMessage("item 5");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForEachAsync_Unbounded_RunsAtMostProcessorCountItems(bool useAsync)
    {
        int[] items = [.. Enumerable.Range(0, Environment.ProcessorCount * 8)];
        int running = 0;
        int maxRunning = 0;

        await ParallelWork.ForEachAsync(useAsync, items, -1, (_, _) =>
        {
            int current = Interlocked.Increment(ref running);
            InterlockedMax(ref maxRunning, current);

            // Blocking work makes the thread pool add threads, which an unbounded loop would use.
            Thread.Sleep(100);

            Interlocked.Decrement(ref running);
            return ValueTask.CompletedTask;
        }, TestContext.Current.CancellationToken);

        maxRunning.Should().BeLessThanOrEqualTo(Environment.ProcessorCount);
    }
}