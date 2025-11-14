using Snowberry.IO.Common;
using Xunit;

namespace Snowberry.IO.Tests;

public class DataTests
{
    private static readonly Random Random = new(42);

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
    private void Sha1_FromString_CaseInsensitive()
    {
        const string upperHash = "0CB57B7044818D2BAFDDE19E0345DC5808E50CF2";
        const string lowerHash = "0cb57b7044818d2bafdde19e0345dc5808e50cf2";

        var upper = new Sha1(upperHash);
        var lower = new Sha1(lowerHash);

        Assert.Equal(upper, lower);
        Assert.Equal(upperHash, upper.ToString());
        Assert.Equal(upperHash, lower.ToString());
    }

    [Fact]
    private void Sha1_FromString_InvalidLength_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => new Sha1("TOOLONG123456789012345678901234567890"));
        Assert.Throws<ArgumentException>(() => new Sha1("TOOSHORT"));
    }

    [Fact]
    private void Sha1_FromString_InvalidCharacters_ThrowsException()
    {
        Assert.Throws<FormatException>(() => new Sha1("0CB57B7044818D2BAFDDE19E0345DC5808E50CZZ"));
    }

    [Fact]
    private void Sha1Equality()
    {
        byte[] hashBuffer = new byte[Sha1.StructSize];
        Random.NextBytes(hashBuffer);

        var a = new Sha1(hashBuffer);
        hashBuffer[10] += 1;
        var b = new Sha1(hashBuffer);

        Assert.NotEqual(a, b);

        var c = new Sha1(hashBuffer);
        Assert.Equal(c, b);
    }

    [Fact]
    private void Sha1Equality_SameValue()
    {
        var a = new Sha1(1, 2, 3, 4, 5);
        var b = new Sha1(1, 2, 3, 4, 5);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    private void Sha1Equality_DifferentValue()
    {
        var a = new Sha1(1, 2, 3, 4, 5);
        var b = new Sha1(1, 2, 3, 4, 6);

        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    private void Sha1_ZeroHash()
    {
        var zero = new Sha1(0, 0, 0, 0, 0);
        Assert.Equal("0000000000000000000000000000000000000000", zero.ToString());
    }

    [Fact]
    private void Sha1_MaxHash()
    {
        var max = new Sha1(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);
        Assert.Equal("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", max.ToString());
    }

    [Fact]
    private void Sha1_FromBytes_CorrectSize()
    {
        byte[] buffer = new byte[Sha1.StructSize];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (byte)i;
        }

        var sha1 = new Sha1(buffer);
        byte[] retrieved = sha1.GetHashBuffer();

        Assert.Equal(buffer, retrieved);
    }

    [Fact]
    private void Sha1_FromBytes_InvalidSize_ThrowsException()
    {
        byte[] tooSmall = new byte[Sha1.StructSize - 1];
        byte[] tooLarge = new byte[Sha1.StructSize + 1];

        Assert.Throws<ArgumentException>(() => new Sha1(tooSmall));
        Assert.Throws<ArgumentException>(() => new Sha1(tooLarge));
    }

    [Fact]
    private void Sha1_GetHashBuffer_ReturnsCopy()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);
        byte[] buffer1 = sha1.GetHashBuffer();
        byte[] buffer2 = sha1.GetHashBuffer();

        Assert.NotSame(buffer1, buffer2);
        Assert.Equal(buffer1, buffer2);
    }

    [Fact]
    private void Sha1_StructSize_IsCorrect()
    {
        Assert.Equal(20, Sha1.StructSize);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    [InlineData("123456789ABCDEF0123456789ABCDEF012345678")]
    [InlineData("FEDCBA9876543210FEDCBA9876543210FEDCBA98")]
    private void Sha1_RoundTrip_String(string hash)
    {
        var sha1 = new Sha1(hash);
        string result = sha1.ToString();

        Assert.Equal(hash.ToUpperInvariant(), result);
    }

    [Fact]
    private void Sha1_RoundTrip_Bytes()
    {
        byte[] original = new byte[Sha1.StructSize];
        Random.NextBytes(original);

        var sha1 = new Sha1(original);
        byte[] result = sha1.GetHashBuffer();

        Assert.Equal(original, result);
    }

    [Fact]
    private void Sha1_RoundTrip_UInt32()
    {
        uint h0 = 0x12345678;
        uint h1 = 0x9ABCDEF0;
        uint h2 = 0xFEDCBA98;
        uint h3 = 0x76543210;
        uint h4 = 0x13579BDF;

        var sha1 = new Sha1(h0, h1, h2, h3, h4);

        // Verify through string representation
        string hash = sha1.ToString();
        var sha1Copy = new Sha1(hash);

        Assert.Equal(sha1, sha1Copy);
    }

    [Fact]
    private void Sha1_CompareToNull()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        Assert.False(sha1.Equals(null));
        Assert.False(sha1 == null);
        Assert.True(sha1 != null);
    }

    [Fact]
    private void Sha1_CompareToDifferentType()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        Assert.False(sha1.Equals("not a sha1"));
        Assert.False(sha1.Equals(12345));
    }

    [Fact]
    private void Sha1_HashCode_Consistent()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        int hash1 = sha1.GetHashCode();
        int hash2 = sha1.GetHashCode();

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    private void Sha1_HashCode_DifferentForDifferentValues()
    {
        var sha1a = new Sha1(1, 2, 3, 4, 5);
        var sha1b = new Sha1(1, 2, 3, 4, 6);

        // Hash codes should be different (though collisions are possible)
        Assert.NotEqual(sha1a.GetHashCode(), sha1b.GetHashCode());
    }

    [Theory]
    [InlineData("DA39A3EE5E6B4B0D3255BFEF95601890AFD80709")] // SHA-1 of empty string
    [InlineData("2FD4E1C67A2D28FCED849EE1BB76E7391B93EB12")] // SHA-1 of "The quick brown fox jumps over the lazy dog"
    private void Sha1_RealWorldHashes(string hash)
    {
        var sha1 = new Sha1(hash);
        Assert.Equal(hash, sha1.ToString());
    }

    [Fact]
    private void Sha1_BufferNotModifiedAfterConstruction()
    {
        byte[] buffer = new byte[Sha1.StructSize];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (byte)i;
        }

        byte[] originalCopy = (byte[])buffer.Clone();

        var sha1 = new Sha1(buffer);

        // Modify the original buffer
        buffer[0] = 255;

        // The Sha1 object should not be affected
        byte[] retrieved = sha1.GetHashBuffer();
        Assert.Equal(originalCopy, retrieved);
        Assert.NotEqual(buffer, retrieved);
    }
}
