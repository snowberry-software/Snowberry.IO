using System;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Common.Reader;

/// <summary>
/// Determines a view in the <see cref="IEndianReader"/>.
/// </summary>
public struct RegionRange : IEquatable<RegionRange>
{
    /// <summary>
    /// Creates a new <see cref="RegionRange"/> based on the given <paramref name="range"/>.
    /// </summary>
    /// <param name="range">The range.</param>
    /// <returns>The newly created <see cref="RegionRange"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="range"/> uses an <see cref="Index"/> that is relative to the end (for example <c>^1</c>),
    /// or when the resulting end of the range is before its start.
    /// </exception>
    public static RegionRange ToRegionRange(Range range)
    {
        return (RegionRange)range;
    }

    /// <summary>
    /// Compares two <see cref="RegionRange"/> instances for equality.
    /// </summary>
    /// <param name="left">The left <see cref="RegionRange"/> instance.</param>
    /// <param name="right">The right <see cref="RegionRange"/> instance.</param>
    /// <returns><see langword="true"/> if the instances are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(RegionRange left, RegionRange right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Converts a <see cref="Range"/> whose indexes are both relative to the start into a <see cref="RegionRange"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Range.Start"/> becomes <see cref="StartPosition"/>, and <see cref="Size"/> is <see cref="Range.End"/> minus <see cref="Range.Start"/>.
    /// To use indexes relative to the end, call <see cref="IEndianReader.EnableRegionView(Range)"/> instead.
    /// </remarks>
    /// <param name="range">The given range.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="range"/> uses an <see cref="Index"/> that is relative to the end (for example <c>^1</c>),
    /// or when the resulting end of the range is before its start.
    /// </exception>
    public static explicit operator RegionRange(Range range)
    {
        if (range.Start.IsFromEnd)
            throw new ArgumentException("The start of the range cannot be relative to the end.", nameof(range));

        if (range.End.IsFromEnd)
            throw new ArgumentException("The end of the range cannot be relative to the end.", nameof(range));

        long start = range.Start.Value;
        long end = range.End.Value;

        if (end < start)
            throw new ArgumentException("The end of the range cannot be before its start.", nameof(range));

        return new()
        {
            StartPosition = start,
            Size = end - start
        };
    }

    /// <summary>
    /// Compares two <see cref="RegionRange"/> instances for inequality.
    /// </summary>
    /// <param name="left">The left <see cref="RegionRange"/> instance.</param>
    /// <param name="right">The right <see cref="RegionRange"/> instance.</param>
    /// <returns><see langword="true"/> if the instances are not equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(RegionRange left, RegionRange right)
    {
        return !(left == right);
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj)
    {
        if (obj is not RegionRange other)
            return false;

        return Equals(other);
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

    /// <summary>
    /// Gets or sets the size, in bytes, of the region.
    /// </summary>
    public long Size { readonly get; set; }

    /// <summary>
    /// Gets or sets the zero-based starting position of the region, relative to the underlying reader.
    /// </summary>
    public long StartPosition { readonly get; set; }
}