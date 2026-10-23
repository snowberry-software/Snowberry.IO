namespace Snowberry.IO.Tests.Infrastructure.SingleFile;

/// <summary>
/// A memory stream that fails on asynchronous I/O (<c>allowSync</c>) or on synchronous I/O (otherwise), to prove the two API flavors stay separate.
/// </summary>
internal sealed class GuardStream : MemoryStream
{
    private readonly bool _allowSync;
    private readonly bool _armed;

    // MemoryStream implements its async methods by calling the synchronous ones.
    private bool _inAsyncCall;

    public GuardStream(byte[] data, bool allowSync)
    {
        base.Write(data, 0, data.Length);
        Position = 0;
        _allowSync = allowSync;
        _armed = true;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        EnsureSync();
        return base.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        EnsureSync();
        return base.Read(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureAsync();
        _inAsyncCall = true;

        try
        {
            return base.ReadAsync(buffer, cancellationToken);
        }
        finally
        {
            _inAsyncCall = false;
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        EnsureAsync();
        _inAsyncCall = true;

        try
        {
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }
        finally
        {
            _inAsyncCall = false;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureSync();
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureSync();
        base.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureAsync();
        _inAsyncCall = true;

        try
        {
            return base.WriteAsync(buffer, cancellationToken);
        }
        finally
        {
            _inAsyncCall = false;
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        EnsureAsync();
        _inAsyncCall = true;

        try
        {
            return base.WriteAsync(buffer, offset, count, cancellationToken);
        }
        finally
        {
            _inAsyncCall = false;
        }
    }

    private void EnsureAsync()
    {
        if (_armed && _allowSync)
            throw new InvalidOperationException("Asynchronous I/O on the synchronous path.");
    }

    private void EnsureSync()
    {
        if (_armed && !_allowSync && !_inAsyncCall)
            throw new InvalidOperationException("Synchronous I/O on the asynchronous path.");
    }
}