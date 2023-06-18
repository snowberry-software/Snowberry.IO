using Xunit;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;

namespace Snowberry.IO.Tests;

public class DataTests
{
    [Fact]
    private void Sha1ToString()
    {
        const string hash = "0CB57B7044818D2BAFDDE19E0345DC5808E50CF2";

        var test = new Sha1(1887155468, 730693956, 2665602479, 1490830595, 4060931336);
        Assert.Equal(hash, test.ToString());

        test = new Sha1(hash);
        Assert.Equal(hash, test.ToString());
    }

    [Fact]
    private void Sha1Equality()
    {
        byte[] hashBuffer = new byte[Sha1.StructSize];
        ReaderTests.Random.NextBytes(hashBuffer);

        var a = new Sha1(hashBuffer);
        hashBuffer[10] += 1;
        var b = new Sha1(hashBuffer);

        Assert.NotEqual(a, b);

        var c = new Sha1(hashBuffer);
        Assert.Equal(c, b);
    }
}
