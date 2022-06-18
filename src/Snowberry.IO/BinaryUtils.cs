using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Snowberry.IO;

public static class BinaryUtils
{
    /// <summary>
    /// Applies the correct alignment to the current <paramref name="position"/> based on the padding that is required for the <paramref name="alignment"/>.
    /// </summary>
    /// <param name="position">The current position/offset.</param>
    /// <param name="alignment">The alignment.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
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
    /// Calcualtes the number of padding bytes required to align the start of a data structure.
    /// </summary>
    /// <param name="position">The current position/offset.</param>
    /// <param name="alignment">The alignment.</param>
    /// <returns>The number of padding bytest required to align the start of a data structure.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
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
