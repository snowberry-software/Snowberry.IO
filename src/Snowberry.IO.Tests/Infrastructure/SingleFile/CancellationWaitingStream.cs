namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// A seekable stream whose asynchronous reads complete only by being canceled, to keep an operation running until other work cancels it.
/// </summary>
/// <param name="length">The length the stream reports.</param>
internal sealed class CancellationWaitingStream(int length) : MemoryStream(new byte[length])
{
    // Long enough for any cancellation to arrive; reaching it fails the test instead of hanging it.
    private static readonly TimeSpan s_Timeout = TimeSpan.FromSeconds(30);

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("Only asynchronous reads are supported.");
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(s_Timeout, cancellationToken);
        throw new TimeoutException("The read was not canceled.");
    }
}
