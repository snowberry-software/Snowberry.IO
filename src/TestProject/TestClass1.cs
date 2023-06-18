using Snowberry.IO.Attributes;
using Snowberry.IO.Common;

namespace TestProject;

[BinarySerialize(true, EndianType.LITTLE)]
public partial class TestClass1<T>
{
    public int Test { get; set;  }
}
