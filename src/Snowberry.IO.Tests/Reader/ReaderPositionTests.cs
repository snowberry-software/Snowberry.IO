using AwesomeAssertions;
using Snowberry.IO.Tests.Infrastructure;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for the position, length and alignment of the readers.
/// </summary>
public class ReaderPositionTests
{
    [Fact]
    public void ActualPosition_WithoutRegionView_EqualsPosition()
    {
        using var reader = CreateReader(new byte[100]);

        reader.Position = 50;

        reader.Position.Should().Be(50);
        reader.ActualPosition.Should().Be(50);
    }

    [Fact]
    public void CanReadData_AfterOnlyByte_ReturnsFalse()
    {
        using var reader = CreateReader([1]);

        reader.ReadByte();

        reader.CanReadData.Should().BeFalse();
    }

    [Fact]
    public void CanReadData_BeforeLastByte_ReturnsTrue()
    {
        using var reader = CreateReader([1, 2, 3]);

        reader.CanReadData.Should().BeTrue();
        reader.ReadByte();
        reader.CanReadData.Should().BeTrue();
        reader.ReadByte();
        reader.CanReadData.Should().BeTrue();
        reader.ReadByte();
        reader.CanReadData.Should().BeFalse();
    }

    [Fact]
    public void CanReadData_EmptyStream_ReturnsFalse()
    {
        using var reader = CreateReader([]);

        reader.CanReadData.Should().BeFalse();
    }

    [Fact]
    public void Length_NonSeekableStream_ThrowsNotSupportedException()
    {
        using var reader = CreateReader(new ChunkedReadStream([1, 2, 3], canSeek: false));

        FluentActions.Invoking(() => _ = reader.Length).Should().ThrowExactly<NotSupportedException>();
    }

    [Fact]
    public void Length_WithoutRegionView_EqualsStreamLength()
    {
        using var reader = CreateReader(new byte[100]);

        reader.Length.Should().Be(100);
        reader.ActualLength.Should().Be(100);
    }

    [Fact]
    public void Position_AfterReadOfEachType_AdvancesByTypeSize()
    {
        using var reader = CreateReader(WriteBytes(w => w.Write((byte)1).Write((short)2).Write(3).Write(4L).Write(5.0f).Write(6.0)));

        reader.Position.Should().Be(0);
        reader.ReadByte();
        reader.Position.Should().Be(1);
        reader.ReadInt16();
        reader.Position.Should().Be(3);
        reader.ReadInt32();
        reader.Position.Should().Be(7);
        reader.ReadInt64();
        reader.Position.Should().Be(15);
        reader.ReadSingle();
        reader.Position.Should().Be(19);
        reader.ReadDouble();
        reader.Position.Should().Be(27);
    }

    [Fact]
    public void Position_AfterReadingWrittenValues_EqualsWriterPosition()
    {
        long writerPosition = 0;
        byte[] data = WriteBytes(w =>
        {
            w.Write(100).Write(200);
            writerPosition = w.Position;
        });
        using var reader = CreateReader(data);

        reader.ReadInt32();
        reader.ReadInt32();

        reader.Position.Should().Be(writerPosition);
    }

    [Fact]
    public void Position_AfterReads_EqualsBytesRead()
    {
        using var reader = CreateReader(WriteBytes(w => w.Write(100).Write(200L).Write((short)300)));

        reader.Position.Should().Be(0);

        reader.ReadInt32();
        reader.Position.Should().Be(4);

        reader.ReadInt64();
        reader.Position.Should().Be(12);

        reader.ReadInt16();
        reader.Position.Should().Be(14);
    }

    [Fact]
    public void Position_Negative_ThrowsArgumentOutOfRangeException()
    {
        using var reader = CreateReader([]);

        FluentActions.Invoking(() => reader.Position = -1).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Position_NonSeekableStream_ThrowsNotSupportedException()
    {
        using var reader = CreateReader(new ChunkedReadStream([1, 2, 3], canSeek: false));

        FluentActions.Invoking(() => reader.Position = 0).Should().ThrowExactly<NotSupportedException>();
    }

    [Fact]
    public void Position_SetBackToZeroAfterRead_ReturnsZero()
    {
        using var reader = CreateReader(new byte[100]);
        reader.ReadInt32();

        reader.Position = 0;

        reader.Position.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void Position_SetThousandTimes_ReturnsEachAssignedValue()
    {
        using var reader = CreateReader(new byte[10000]);

        for (int i = 0; i < 1000; i++)
        {
            reader.Position = i * 10;
            reader.Position.Should().Be(i * 10);
        }
    }

    [Fact]
    public void Position_SetToValueOffset_ReadsValueAtOffset()
    {
        using var reader = CreateReader(WriteBytes(w =>
        {
            for (int i = 0; i < 10; i++)
                w.Write(i);
        }));

        reader.Position = 20;

        reader.ReadInt32().Should().Be(5);
    }

    [Fact]
    public void Position_Set_ReturnsAssignedValue()
    {
        using var reader = CreateReader(new byte[100]);

        reader.Position = 50;
        reader.Position.Should().Be(50);

        reader.Position = 0;
        reader.Position.Should().Be(0);

        reader.Position = 99;
        reader.Position.Should().Be(99);
    }

    [Fact]
    public void ReadAlignment_AfterWritePadding_SkipsPadding()
    {
        WriteThenRead(
            w => w.Write((byte)0xFF).WritePadding(16),
            r =>
            {
                r.ReadByte();
                r.ReadAlignment(16);
                r.Position.Should().Be(16);
            });
    }

    [Theory]
    [InlineData(16L, 16, 16L)]
    [InlineData(17L, 16, 32L)]
    [InlineData(1L, 4, 4L)]
    public void ReadAlignment_Position_MovesToNextBoundary(long position, byte alignment, long expectedPosition)
    {
        using var reader = CreateReader(new byte[100]);
        reader.Position = position;

        reader.ReadAlignment(alignment);

        reader.Position.Should().Be(expectedPosition);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(255)]
    public void ReadAlignment_ZeroOrNotPowerOfTwo_ThrowsAndKeepsPosition(byte alignment)
    {
        using var reader = CreateReader(new byte[100]);
        reader.Position = 1;

        FluentActions.Invoking(() => reader.ReadAlignment(alignment)).Should().ThrowExactly<ArgumentOutOfRangeException>();
        reader.Position.Should().Be(1);
    }

    [Fact]
    public void ReadAlignment_Zero_ThrowsForAlignmentParameter()
    {
        using var reader = CreateReader(new byte[100]);

        FluentActions.Invoking(() => reader.ReadAlignment(0)).Should().ThrowExactly<ArgumentOutOfRangeException>()
            .WithParameterName("alignment")
            .WithMessage("Alignment must be greater than 0.*");
    }
}