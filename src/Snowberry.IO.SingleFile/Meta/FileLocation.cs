using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;

namespace Snowberry.IO.SingleFile.Meta;

/// <summary>
/// Represents a file location in the application binary.
/// </summary>
public struct FileLocation
{
    /// <summary>
    /// Offset of the file in the application binary.
    /// </summary>
    public long Offset;

    /// <summary>
    /// Size of the file in the application binary (uncompressed).
    /// </summary>
    public long Size;

    /// <summary>
    /// Reads a <see cref="FileLocation"/> from the given <see cref="IEndianReader"/>.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The read <see cref="FileLocation"/>.</returns>
    public static FileLocation Read(IEndianReader reader)
    {
        return new()
        {
            Offset = reader.ReadInt64(),
            Size = reader.ReadInt64()
        };
    }

    /// <summary>
    /// Writes the <see cref="FileLocation"/> to the given <see cref="IEndianWriter"/>.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public readonly void Write(IEndianWriter writer)
    {
        writer.Write(Offset);
        writer.Write(Size);
    }
}
