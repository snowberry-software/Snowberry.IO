using System.Text;
using Snowberry.IO.Common;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Xunit;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains tests for error handling, exceptions, and boundary conditions.
/// </summary>
public class ErrorHandlingTests
{
    [Fact]
    public void NullStream_Reader_ThrowsException()
    {
        using var reader = new EndianStreamReader(null);

        Assert.Throws<NullReferenceException>(() => _ = reader.Position);
    }

    [Fact]
    public void NullStream_Writer_ThrowsException()
    {
        Assert.Throws<ArgumentNullException>(() => new EndianStreamWriter(null!, true));
    }

    [Fact]
    public void DisposedReader_ThrowsObjectDisposedException()
    {
        var stream = new MemoryStream();
        var reader = new EndianStreamReader(stream);
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => reader.ReadInt32());
        Assert.Throws<ObjectDisposedException>(reader.ReadString);
    }

    [Fact]
    public void ReadBeyondEnd_ThrowsEndOfStreamException()
    {
        var stream = new MemoryStream(new byte[] { 1, 2 });
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<EndOfStreamException>(() => reader.ReadInt32());
    }

    [Fact]
    public void ReadInt64BeyondEnd_ThrowsEndOfStreamException()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<EndOfStreamException>(() => reader.ReadInt64());
    }

    [Fact]
    public void ReadGuidBeyondEnd_ThrowsEndOfStreamException()
    {
        var stream = new MemoryStream(new byte[10]);
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<EndOfStreamException>(() => reader.ReadGuid());
    }

    [Fact]
    public void ReadSha1BeyondEnd_ThrowsEndOfStreamException()
    {
        var stream = new MemoryStream(new byte[10]);
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<EndOfStreamException>(() => reader.ReadSha1());
    }

    [Fact]
    public void CopyTo_NullDestination_ThrowsException()
    {
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<ArgumentNullException>(() => reader.CopyTo(null!));
    }

    [Fact]
    public void NegativePosition_ThrowsException()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.Position = -1);
    }

    [Fact]
    public void WriteSizedCString_NullString_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentNullException>(() => writer.WriteSizedCString(null!, 10));
    }

    [Fact]
    public void WriteSizedCString_NegativeSize_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteSizedCString("test", -1));
    }

    [Fact]
    public void WriteSizedCString_SizeTooSmall_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteSizedCString("Hello", 3));
    }

    [Fact]
    public void WriteSizedCString_UnicodeExceedsSize_ThrowsIOException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true, Encoding.UTF8);

        // Emoji characters take more than 1 byte in UTF-8
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteSizedCString("😀😀😀😀", 5));
    }

    [Fact]
    public void WriteCString_NullString_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentNullException>(() => writer.WriteCString(null!));
    }

    [Fact]
    public void WriteStringCharacters_NullString_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentNullException>(() => writer.WriteStringCharacters(null!));
    }

    [Fact]
    public void ReadString_NegativeLength_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write7BitEncodedInt(-1);
        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        Assert.Throws<IOException>(reader.ReadString);
    }

    [Fact]
    public void ReadString_VeryLargeLength_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);
        writer.Write7BitEncodedInt(int.MaxValue);
        stream.Position = 0;

        using var reader = new EndianStreamReader(stream);
        Assert.Throws<EndOfStreamException>(reader.ReadString);
    }

    [Fact]
    public void ReadBytes_NegativeCount_ThrowsException()
    {
        var stream = new MemoryStream();
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadBytes(-1));
    }

    [Fact]
    public void ReadSizedCString_NegativeSize_ThrowsException()
    {
        var stream = new MemoryStream(new byte[10]);
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadSizedCString(-1));
    }

    [Fact]
    public void ReadSizedCString_SizeExceedsStream_ThrowsException()
    {
        var stream = new MemoryStream(new byte[10]);
        using var reader = new EndianStreamReader(stream);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadSizedCString(100));
    }

    [Fact]
    public void NonSeekableStream_Position_ThrowsException()
    {
        using var nonSeekable = new NonSeekableStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(nonSeekable);

        Assert.Throws<NotSupportedException>(() => reader.Position = 0);
    }

    [Fact]
    public void NonSeekableStream_Length_ThrowsException()
    {
        using var nonSeekable = new NonSeekableStream(new byte[] { 1, 2, 3 });
        using var reader = new EndianStreamReader(nonSeekable);

        Assert.Throws<NotSupportedException>(() => _ = reader.Length);
    }

    [Fact]
    public void ReadOnlyStream_Write_ThrowsException()
    {
        using var readOnly = new MemoryStream(new byte[] { 1, 2, 3 }, writable: false);

        Assert.Throws<ArgumentException>(() =>
        {
            using var writer = new EndianStreamWriter(readOnly, true);
            writer.Write(0);
        });
    }

    [Fact]
    public void BinaryEndianConverter_InvalidOffset_ThrowsException()
    {
        byte[] buffer = new byte[10];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BinaryEndianConverter.ToInt32(buffer, -1, EndianType.LITTLE));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BinaryEndianConverter.ToInt32(buffer, 100, EndianType.LITTLE));
    }

    [Fact]
    public void BinaryEndianConverter_BufferTooSmall_ThrowsException()
    {
        byte[] buffer = new byte[2];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BinaryEndianConverter.ToInt32(buffer, 0, EndianType.LITTLE));
    }

    [Fact]
    public void RegionView_RangeBeyondStream_ThrowsException()
    {
        using var reader = new EndianStreamReader(new MemoryStream(new byte[100]));

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.EnableRegionView(0..200));
    }

    [Fact]
    public void ReadAlignment_ZeroAlignment_ThrowsException()
    {
        using var reader = new EndianStreamReader(new MemoryStream(new byte[100]));

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadAlignment(0));
    }

    [Fact]
    public void WritePadding_ZeroAlignment_ThrowsException()
    {
        var stream = new MemoryStream();
        using var writer = new EndianStreamWriter(stream, true);

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WritePadding(0));
    }

    [Fact]
    public void DoubleDispose_DoesNotThrow()
    {
        var stream = new MemoryStream();
        var reader = new EndianStreamReader(stream);

        reader.Dispose();
        reader.Dispose(); // Should not throw
    }

    [Fact]
    public void CopyTo_NegativeLength_ThrowsException()
    {
        var source = new MemoryStream(new byte[100]);
        var destination = new MemoryStream();
        using var reader = new EndianStreamReader(source);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.CopyTo(destination, -1));
    }

    [Fact]
    public void CopyTo_InvalidBufferSize_ThrowsException()
    {
        var source = new MemoryStream(new byte[100]);
        var destination = new MemoryStream();
        using var reader = new EndianStreamReader(source);

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.CopyTo(destination, 100, -1));
    }

    [Fact]
    public void ReadUntilEnd_NegativeBufferSize_ThrowsException()
    {
        using var reader = new EndianStreamReader(new MemoryStream(new byte[100]));

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadUntilEnd(-1));
    }

    [Fact]
    public void EnsureBufferSize_NegativeSize_ThrowsException()
    {
        using var reader = new EndianStreamReader(new MemoryStream());

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.EnsureBufferSize(-1));
    }

    // Helper class for non-seekable stream testing
    private class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
