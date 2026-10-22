using AwesomeAssertions;
using Snowberry.IO.Common;
using Xunit;

namespace Snowberry.IO.Tests.Common;

/// <summary>
/// Contains tests for <see cref="BinaryUtils"/>.
/// </summary>
public class BinaryUtilsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(255)]
    public void AlignmentMethods_InvalidAlignment_ThrowForAlignmentWithItsValue(byte alignment)
    {
        long position = 5;

        var fromPadding = FluentActions.Invoking(() => BinaryUtils.CalculatePadding(5, alignment)).Should().ThrowExactly<ArgumentOutOfRangeException>().Which;
        var fromAlignment = FluentActions.Invoking(() => BinaryUtils.ApplyAlignment(ref position, alignment)).Should().ThrowExactly<ArgumentOutOfRangeException>().Which;

        foreach (var exception in new[] { fromPadding, fromAlignment })
        {
            exception.ParamName.Should().Be("alignment");
            exception.ActualValue.Should().Be(alignment);
            exception.Message.Should().StartWith("Alignment must be a non-zero power of two.");
        }

        position.Should().Be(5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(128)]
    public void AlignmentMethods_PowerOfTwo_AgreeWithEachOther(byte alignment)
    {
        foreach (long start in new long[] { 0, 1, 7, 127, 128, 129, -1, -129, long.MaxValue - 256 })
        {
            long position = start;
            BinaryUtils.ApplyAlignment(ref position, alignment);

            position.Should().Be(start + BinaryUtils.CalculatePadding(start, alignment));
            (position % alignment).Should().Be(0);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void ApplyAlignment_PowerOfTwo_AlignsPosition(byte alignment)
    {
        long position = 5;

        FluentActions.Invoking(() => BinaryUtils.ApplyAlignment(ref position, alignment)).Should().NotThrow();

        (position % alignment).Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(200)]
    public void ApplyAlignment_ZeroOrNotPowerOfTwo_ThrowsArgumentOutOfRangeException(byte alignment)
    {
        long position = 5;

        FluentActions.Invoking(() => BinaryUtils.ApplyAlignment(ref position, alignment)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalculatePadding_PositionsUpTo64_ReturnsPaddingToNextBoundary()
    {
        for (long position = 0; position < 64; position++)
        {
            long padding = BinaryUtils.CalculatePadding(position, 16);

            padding.Should().BeInRange(0, 15);
            ((position + padding) % 16).Should().Be(0);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(200)]
    public void CalculatePadding_ZeroOrNotPowerOfTwo_ThrowsArgumentOutOfRangeException(byte alignment)
    {
        FluentActions.Invoking(() => BinaryUtils.CalculatePadding(5, alignment)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }
}