﻿using System.Runtime.CompilerServices;

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
    /// <param name="alignment">The alignment.</param>
#if NET6_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public static void ApplyAlignment(ref long position, byte alignment)
    {
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
    /// <param name="alignment">The alignment.</param>
    /// <returns>The number of padding bytes required to align the start of a data structure.</returns>
#if NET6_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public static long CalculatePadding(long position, byte alignment)
    {
        // NOTE(VNC): https://en.wikipedia.org/wiki/Data_structure_alignment
        //
        // padding = (align - (offset & (align - 1))) & (align - 1)
        //         = -offset & (align - 1)
        //

        return -position & (alignment - 1);
    }
}
