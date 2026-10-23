using System;
using System.Runtime.CompilerServices;

namespace Snowberry.IO.Common;

/// <summary>
/// Provides utility methods for binary data alignment.
/// </summary>
public static class BinaryUtils
{
    /// <summary>
    /// Applies the correct alignment to the current <paramref name="position"/> based on the padding that is required for the <paramref name="alignment"/>.
    /// </summary>
    /// <param name="position">The current position/offset.</param>
    /// <param name="alignment">The alignment. Must be a non-zero power of two.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="alignment"/> is zero or not a power of two.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyAlignment(ref long position, byte alignment)
    {
        ValidateAlignment(alignment);

        // NOTE(VNC): https://en.wikipedia.org/wiki/Data_structure_alignment
        //
        // aligned = (offset + (align - 1)) & ~(align - 1)
        //         = (offset + (align - 1)) & -align
        //
        position = (position + (alignment - 1)) & -alignment;
    }

    /// <summary>
    /// Calculates the number of padding bytes required to align the start of a data structure.
    /// </summary>
    /// <param name="position">The current position/offset.</param>
    /// <param name="alignment">The alignment. Must be a non-zero power of two.</param>
    /// <returns>The number of padding bytes required to align the start of a data structure.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="alignment"/> is zero or not a power of two.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long CalculatePadding(long position, byte alignment)
    {
        ValidateAlignment(alignment);

        // NOTE(VNC): https://en.wikipedia.org/wiki/Data_structure_alignment
        //
        // padding = (align - (offset & (align - 1))) & (align - 1)
        //         = -offset & (align - 1)
        //

        return -position & (alignment - 1);
    }

    /// <summary>
    /// Throws the exception for an <paramref name="alignment"/> that is zero or not a power of two.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="ValidateAlignment"/>: on .NET Framework a method that loads a string literal never inlines into another
    /// assembly, and the JIT treats a call to a method that always throws as not returning.
    /// </remarks>
    /// <param name="alignment">The invalid alignment.</param>
    /// <exception cref="ArgumentOutOfRangeException">Always thrown.</exception>
    private static void ThrowInvalidAlignment(byte alignment)
    {
        throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Alignment must be a non-zero power of two.");
    }

    /// <summary>
    /// Validates that <paramref name="alignment"/> is a non-zero power of two.
    /// </summary>
    /// <param name="alignment">The alignment to validate.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="alignment"/> is zero or not a power of two.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateAlignment(byte alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
            ThrowInvalidAlignment(alignment);
    }
}