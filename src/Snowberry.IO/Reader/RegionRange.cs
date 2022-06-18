using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Snowberry.IO.Reader.Interfaces;

namespace Snowberry.IO.Reader;

/// <summary>
/// Determines a view in the <see cref="IEndianReader"/>.
/// </summary>
public struct RegionRange
{
    public long StartPosition;
    public long Size;

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
}
