using AwesomeAssertions;
using Snowberry.IO.Common.Reader;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Reader;

/// <summary>
/// Contains tests for the region view of the readers, which limits reads to a range of the underlying data.
/// </summary>
public class RegionViewTests
{
    [Fact]
    public void CopyTo_RegionView_CopiesOnlyRegion()
    {
        byte[] data = SequentialBytes(100);
        using var reader = CreateReader(data);
        reader.EnableRegionView(10..20);
        var destination = new MemoryStream();

        reader.CopyTo(destination);

        destination.ToArray().Should().Equal(data.Skip(10).Take(10));
    }

    [Fact]
    public void DisableRegionView_NotEnabled_DoesNothing()
    {
        using var reader = CreateReader(new byte[100]);

        reader.IsRegionViewEnabled.Should().BeFalse();
        FluentActions.Invoking(reader.DisableRegionView).Should().NotThrow();
        reader.IsRegionViewEnabled.Should().BeFalse();
    }

    [Fact]
    public void EnableRegionView_AlreadyEnabled_ReplacesRegion()
    {
        using var reader = CreateReader(new byte[200]);

        reader.EnableRegionView(50..150);
        reader.IsRegionViewEnabled.Should().BeTrue();

        reader.EnableRegionView(60..140);
        reader.IsRegionViewEnabled.Should().BeTrue();
    }

    [Fact]
    public void EnableRegionView_BoundedRange_MapsPositionAndLengthToRegion()
    {
        using var reader = CreateReader(new byte[200]);
        reader.Position = 100;

        reader.EnableRegionView(100..200);

        reader.Position.Should().Be(0);
        reader.Position += 50;
        reader.Position.Should().Be(50);
        reader.Position += 50;
        reader.Length.Should().Be(100);

        reader.DisableRegionView();

        reader.Position.Should().Be(200);
    }

    [Fact]
    public void EnableRegionView_RangeBeyondStream_ThrowsArgumentOutOfRangeException()
    {
        using var reader = CreateReader(new byte[100]);

        FluentActions.Invoking(() => reader.EnableRegionView(0..200)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EnableRegionView_RangeEndBeforeStart_ThrowsAndStaysDisabled()
    {
        using var reader = CreateReader(new byte[100]);

        FluentActions.Invoking(() => reader.EnableRegionView(50..^60)).Should().ThrowExactly<ArgumentOutOfRangeException>();
        reader.IsRegionViewEnabled.Should().BeFalse();
    }

    [Fact]
    public void EnableRegionView_RangeFromEnd_ResolvesAgainstLength()
    {
        using var reader = CreateReader(new byte[100]);

        reader.EnableRegionView(20..);
        reader.RegionView.StartPosition.Should().Be(20);
        reader.RegionView.Size.Should().Be(80);

        reader.EnableRegionView(^30..^10);
        reader.RegionView.StartPosition.Should().Be(70);
        reader.RegionView.Size.Should().Be(20);
    }

    [Theory]
    [InlineData(-5L, 10L)]
    [InlineData(5L, -10L)]
    [InlineData(90L, 20L)]
    [InlineData(10L, long.MaxValue)]
    public void EnableRegionView_RegionOutsideStream_ThrowsAndStaysDisabled(long startPosition, long size)
    {
        using var reader = CreateReader(new byte[100]);

        FluentActions.Invoking(() => reader.EnableRegionView(new RegionRange { StartPosition = startPosition, Size = size }))
            .Should().ThrowExactly<ArgumentOutOfRangeException>();
        reader.IsRegionViewEnabled.Should().BeFalse();
    }

    [Fact]
    public void EnableRegionView_SkippedPrefix_ReadsStringsRelativeToRegion()
    {
        const string text = "someTestString";
        byte[] data = WriteBytes(w => w.Write(new byte[20]).WriteCString(text).WriteCString(text + "28").WriteSizedCString(text, 40));
        using var reader = CreateReader(data);

        reader.EnableRegionView(20..);

        reader.ActualPosition.Should().Be(20);
        reader.Position.Should().Be(0);
        reader.Length.Should().Be(text.Length + 1 + text.Length + 2 + 1 + 40);

        reader.ReadCString().Should().Be(text);
        reader.ReadCString().Should().Be(text + "28");
        reader.ReadSizedCString(40).Should().Be(text);
        reader.Position.Should().Be(reader.Length);

        long end = reader.Position;
        reader.Position = 0;
        reader.ActualPosition.Should().Be(20);
        reader.Position.Should().Be(0);

        reader.Position = end;
        reader.Position.Should().Be(end);

        reader.CopyTo(new MemoryStream());
    }

    [Fact]
    public void EnableRegionView_WholeStream_KeepsPositionAndLength()
    {
        using var reader = CreateReader(new byte[100]);

        reader.EnableRegionView(0..100);

        reader.Position.Should().Be(0);
        reader.Length.Should().Be(100);
        reader.ActualPosition.Should().Be(0);
        reader.ActualLength.Should().Be(100);
    }

    [Fact]
    public void Position_NegativeInsideRegionView_ThrowsAndKeepsPosition()
    {
        using var reader = CreateReader(SequentialBytes(100));
        reader.EnableRegionView(10..20);
        reader.Position = 4;

        FluentActions.Invoking(() => reader.Position = -5).Should().ThrowExactly<ArgumentOutOfRangeException>()
            .WithParameterName("value").Which.ActualValue.Should().Be(-5L);
        reader.Position.Should().Be(4);
        reader.ReadByte().Should().Be(14);
    }

    [Fact]
    public void ReadExactly_CountBeyondRegionEnd_ThrowsBeforeReading()
    {
        using var reader = CreateReader(new byte[100]);
        reader.EnableRegionView(10..20);
        reader.Position = 8;

        FluentActions.Invoking(() => reader.ReadExactly(new byte[5], 0, 5)).Should().ThrowExactly<EndOfStreamException>();
        reader.ActualPosition.Should().Be(18);
    }

    [Fact]
    public void Read_CountBeyondRegionEnd_StopsAtRegionEnd()
    {
        using var reader = CreateReader(new byte[100]);
        reader.EnableRegionView(10..20);
        reader.Position = 8;

        int read = reader.Read(new byte[10], 0, 10);

        read.Should().Be(2);
        reader.Position.Should().Be(10);
    }
}