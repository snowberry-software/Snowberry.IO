using Snowberry.IO.Common;
using Snowberry.IO.Generator.Attributes;

namespace Snowberry.IO.Tests.Models;

[BinarySerialization(5)]
public partial class BinaryModel1
{
    [BinaryProperty(Index = 0)]
    public int Id { get; set; }

    [BinaryProperty(Index = 1, MinimumVersion = 1, MaximumVersion = uint.MaxValue)]
    public int IntWithVersion { get; set; }

    [BinaryProperty(Index = 2, MaximumVersion = 4)]
    public Guid SomeId { get; set; }

    [BinaryProperty(Index = 3)]
    public Sha1 Hash { get; set; }

    [BinaryProperty(Index = 4, EndianType = EndianType.BIG)]
    public int IntWithBigEndian { get; set; }
}
