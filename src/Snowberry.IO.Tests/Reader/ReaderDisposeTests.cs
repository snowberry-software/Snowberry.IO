using AwesomeAssertions;
using Snowberry.IO.Common;
using Snowberry.IO.Reader;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for disposing <see cref="EndianStreamReader"/> and for its behavior after disposal.
/// </summary>
public class ReaderDisposeTests
{
    [Fact]
    public void Close_ThroughBinaryReader_DisposesReaderAndStream()
    {
        var stream = new MemoryStream(new byte[8]);
        BinaryReader reader = CreateReader(stream);

        reader.Close();

        ((EndianStreamReader)reader).Disposed.Should().BeTrue();
        stream.CanRead.Should().BeFalse();
        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DisposesStreamOnce()
    {
        var stream = new DisposeCountingStream();
        var reader = CreateReader(stream);

        reader.Dispose();
        ((IDisposable)reader).Dispose();

        stream.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var reader = CreateReader([]);

        reader.Dispose();

        FluentActions.Invoking(reader.Dispose).Should().NotThrow();
    }

    [Fact]
    public void Dispose_Default_ClosesStream()
    {
        var stream = new MemoryStream(new byte[8]);
        var reader = CreateReader(stream);

        reader.Dispose();

        reader.Disposed.Should().BeTrue();
        stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public void Dispose_KeepStreamOpenResetAfterConstruction_ClosesStream()
    {
        var stream = new MemoryStream(new byte[8]);
        var reader = new EndianStreamReader(stream) { KeepStreamOpen = true };

        reader.KeepStreamOpen = false;
        reader.Dispose();

        stream.CanRead.Should().BeFalse();
    }

    [Fact]
    public void Dispose_KeepStreamOpenSetAfterRead_LeavesStreamUsable()
    {
        var stream = new MemoryStream([1, 2, 3]);

        using (var reader = new EndianStreamReader(stream))
        {
            reader.KeepStreamOpen = true;
            reader.ReadByte();
        }

        stream.Position.Should().Be(1);
        FluentActions.Invoking(() => stream.Position = 0).Should().NotThrow();
    }

    [Fact]
    public void Dispose_KeepStreamOpen_LeavesStreamOpen()
    {
        var stream = new MemoryStream(new byte[8]);
        var reader = new EndianStreamReader(stream) { KeepStreamOpen = true };

        reader.ReadInt32();
        reader.Dispose();

        reader.Disposed.Should().BeTrue();
        stream.CanRead.Should().BeTrue();
        stream.Position.Should().Be(4);
    }

    [Fact]
    public void Dispose_NullStream_SetsDisposed()
    {
        var reader = new EndianStreamReader(null);

        reader.Dispose();

        reader.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Disposed_AfterDispose_ReturnsTrue()
    {
        var reader = CreateReader([]);

        reader.Disposed.Should().BeFalse();
        reader.Dispose();
        reader.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Length_AfterDisposeWithKeepStreamOpen_ThrowsObjectDisposedException()
    {
        var reader = new EndianStreamReader(new MemoryStream(new byte[10])) { KeepStreamOpen = true };

        reader.Dispose();

        FluentActions.Invoking(() => _ = reader.Length).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void ReadAndPosition_AfterDispose_ThrowObjectDisposedException()
    {
        var reader = CreateReader([1, 2, 3]);
        reader.Dispose();

        FluentActions.Invoking(() => reader.ReadByte()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.Position = 0).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => _ = reader.Position).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void ReadPrimitivesAndString_AfterDispose_ThrowObjectDisposedException()
    {
        var reader = CreateReader([]);
        reader.Dispose();

        FluentActions.Invoking(() => reader.ReadByte()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(reader.ReadString).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void Reads_AfterDisposeWithKeepStreamOpen_Throw()
    {
        var reader = new EndianStreamReader(new MemoryStream(new byte[64])) { KeepStreamOpen = true };
        reader.Dispose();

        FluentActions.Invoking(() => reader.ReadInt32()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.ReadInt32(EndianType.BIG)).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.ReadChar()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.ReadBytes(2)).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.ReadByteSafe()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.TryReadInt32(out _)).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.ReadCString()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.ReadSizedCString(4)).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => reader.ReadLine()).Should().ThrowExactly<ObjectDisposedException>();
        FluentActions.Invoking(() => _ = reader.Position).Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void Reads_AfterDispose_ThrowObjectDisposedExceptionNamingTheReader()
    {
        var reader = CreateReader([1, 2, 3, 4]);
        reader.Dispose();

        FluentActions.Invoking(() => reader.ReadInt32(EndianType.BIG)).Should().ThrowExactly<ObjectDisposedException>()
            .Which.ObjectName.Should().Be(typeof(EndianStreamReader).FullName);
        FluentActions.Invoking(() => reader.TryReadInt16(out _)).Should().ThrowExactly<ObjectDisposedException>()
            .Which.ObjectName.Should().Be(typeof(EndianStreamReader).FullName);
        FluentActions.Invoking(() => reader.ReadByteSafe()).Should().ThrowExactly<ObjectDisposedException>()
            .Which.ObjectName.Should().Be(typeof(EndianStreamReader).FullName);
    }
}