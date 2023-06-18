using Snowberry.IO.Attributes;

namespace TestProject;

[BinarySerialize(true)]
public partial class TestClass1<T>
{
    public int Test { get; set;  }
}
