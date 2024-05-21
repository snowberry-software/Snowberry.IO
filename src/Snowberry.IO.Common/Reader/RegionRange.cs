using System;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Common.Reader;

/// <summary>
/// Determines a view in the <see cref="IEndianReader"/>.
/// </summary>
public struct RegionRange : IEquatable<RegionRange>
{
#pragma warning disable CA1051 // Do not declare visible instance fields
    public long StartPosition;
    public long Size;
#pragma warning restore CA1051 // Do not declare visible instance fields

    /// <summary>
    /// Uses the <see cref="Range"/> to create a new <see cref="RegionRange"/>.
    /// </summary>
    /// <remarks>
    /// The <see cref="Range.Start"/> is considered as <see cref="RegionRange.StartPosition"/>.
    /// <para/>
    /// The <see cref="Range.End"/> is considered as <see cref="RegionRange.Size"/>.
    /// </remarks>
    /// <param name="range">The given range.</param>
    public static implicit operator RegionRange(Range range)
    {
        return new()
        {
            StartPosition = range.Start.Value,
            Size = range.End.Value
        };
    }

    /// <summary>
    /// Creates a new <see cref="RegionRange"/> based on the given <paramref name="range"/>.
    /// </summary>
    /// <param name="range">The range.</param>
    /// <returns>The newly created <see cref="RegionRange"/>.</returns>
    public static RegionRange ToRegionRange(Range range)
    {
        return range;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj)
    {
        if (obj is not RegionRange other)
            return false;

        return Equals(other);
    }

    public static bool operator ==(RegionRange left, RegionRange right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(RegionRange left, RegionRange right)
    {
        return !(left == right);
    }

    /// <inheritdoc/>
    public readonly bool Equals(RegionRange other)
    {
        return other.StartPosition == StartPosition && other.Size == Size;
    }

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
#if NETSTANDARD2_1_OR_GREATER || NET6_0_OR_GREATER
        return HashCode.Combine(StartPosition, Size);
#else
        return 0x327482 ^ StartPosition.GetHashCode() ^ Size.GetHashCode();
#endif
    }
}
