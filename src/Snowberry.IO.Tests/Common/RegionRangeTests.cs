using AwesomeAssertions;
using Snowberry.IO.Common.Reader;
using Xunit;

namespace Snowberry.IO.Tests.Common;

/// <summary>
/// Contains tests for <see cref="RegionRange"/>.
/// </summary>
public class RegionRangeTests
{
    [Fact]
    public void EqualsObject_NullOtherTypeOrDifferentRange_ReturnsFalse()
    {
        var region = new RegionRange { StartPosition = 10, Size = 20 };

        region.Equals(null).Should().BeFalse();
        region.Equals("10..30").Should().BeFalse();
        region.Equals((object)new RegionRange { StartPosition = 10, Size = 21 }).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentStartOrSize_ReturnsFalse()
    {
        var region = new RegionRange { StartPosition = 10, Size = 20 };
        var otherStart = new RegionRange { StartPosition = 11, Size = 20 };
        var otherSize = new RegionRange { StartPosition = 10, Size = 21 };

        region.Equals(otherStart).Should().BeFalse();
        region.Equals(otherSize).Should().BeFalse();
        (region == otherStart).Should().BeFalse();
        (region != otherSize).Should().BeTrue();
    }

    [Fact]
    public void Equals_SameStartAndSize_ReturnsTrue()
    {
        var region = new RegionRange { StartPosition = 10, Size = 20 };
        var same = new RegionRange { StartPosition = 10, Size = 20 };

        region.Equals(same).Should().BeTrue();
        region.Equals((object)same).Should().BeTrue();
        (region == same).Should().BeTrue();
        (region != same).Should().BeFalse();
        region.GetHashCode().Should().Be(same.GetHashCode());
    }

    [Fact]
    public void ExplicitConversion_BoundedRange_UsesStartAndLength()
    {
        var region = (RegionRange)(20..80);

        region.StartPosition.Should().Be(20);
        region.Size.Should().Be(60);
    }

    [Fact]
    public void ExplicitConversion_OpenEndedRange_ThrowsArgumentException()
    {
        FluentActions.Invoking(() => (RegionRange)(5..)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void ExplicitConversion_StartFromEnd_ThrowsArgumentException()
    {
        FluentActions.Invoking(() => (RegionRange)(^5..80)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void ToRegionRange_BoundedRange_UsesStartAndLength()
    {
        var region = RegionRange.ToRegionRange(20..80);

        region.StartPosition.Should().Be(20);
        region.Size.Should().Be(60);
        region.Should().Be((RegionRange)(20..80));
    }

    [Fact]
    public void ToRegionRange_FromEndOrReversedRange_ThrowsArgumentException()
    {
        FluentActions.Invoking(() => RegionRange.ToRegionRange(^10..)).Should().ThrowExactly<ArgumentException>();
        FluentActions.Invoking(() => RegionRange.ToRegionRange(0..^1)).Should().ThrowExactly<ArgumentException>();
        FluentActions.Invoking(() => RegionRange.ToRegionRange(new Range(20, 10))).Should().ThrowExactly<ArgumentException>();
    }
}