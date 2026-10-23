#if NETFRAMEWORK
using System.Globalization;
#endif
using AwesomeAssertions;
using Snowberry.IO.Common;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.TestHelper;

namespace Snowberry.IO.Tests.Common;

/// <summary>
/// Contains tests for <see cref="Sha1"/>.
/// </summary>
public class Sha1Tests
{
    /// <summary>
    /// Parses <paramref name="hash"/> with the hex parser of the library build under test: <c>Convert.FromHexString</c> on .NET,
    /// and <c>byte.Parse</c> with <c>NumberStyles.AllowHexSpecifier</c> for each pair on .NET Framework.
    /// </summary>
    /// <param name="hash">The hash string of 40 characters.</param>
    /// <param name="bytes">The parsed bytes, or an empty array when parsing fails.</param>
    /// <param name="message">The message of the <see cref="FormatException"/> the library throws, or <see langword="null"/> when parsing succeeds.</param>
    /// <returns><see langword="true"/> if the parser accepts <paramref name="hash"/>; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseWithRuntimeHexParser(string hash, out byte[] bytes, out string? message)
    {
#if NETFRAMEWORK
        try
        {
            bytes = new byte[Sha1.c_StructSize];

            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hash.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

            message = null;
            return true;
        }
        catch (FormatException exception)
        {
            bytes = [];
            message = exception.Message;
            return false;
        }
#else
        try
        {
            bytes = Convert.FromHexString(hash);
            message = null;
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            message = "Sha1 must only contain hexadecimal digits.";
            return false;
        }
#endif
    }

    [Theory]
    [InlineData(Sha1.c_StructSize - 1)]
    [InlineData(Sha1.c_StructSize + 1)]
    public void Constructor_BufferWithInvalidSize_ThrowsArgumentException(int size)
    {
        byte[] buffer = new byte[size];

        FluentActions.Invoking(() => new Sha1(buffer)).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void Constructor_LowercaseString_EqualsUppercaseString()
    {
        const string upperHash = "0CB57B7044818D2BAFDDE19E0345DC5808E50CF2";
        const string lowerHash = "0cb57b7044818d2bafdde19e0345dc5808e50cf2";

        var upper = new Sha1(upperHash);
        var lower = new Sha1(lowerHash);

        lower.Should().Be(upper);
        upper.ToString().Should().Be(upperHash);
        lower.ToString().Should().Be(upperHash);
    }

    [Fact]
    public void Constructor_NullString_ThrowsArgumentNullExceptionForHash()
    {
        FluentActions.Invoking(() => new Sha1((string)null!)).Should().ThrowExactly<ArgumentNullException>()
            .Which.ParamName.Should().Be("hash");
    }

    [Fact]
    public void Constructor_SourceBufferChangedAfterwards_KeepsOriginalBytes()
    {
        byte[] buffer = SequentialBytes(Sha1.c_StructSize);
        byte[] originalCopy = (byte[])buffer.Clone();
        var sha1 = new Sha1(buffer);

        buffer[0] = 255;

        byte[] retrieved = sha1.GetHashBuffer();
        retrieved.Should().Equal(originalCopy);
        retrieved.Should().NotEqual(buffer);
    }

    [Theory]
    [InlineData(Sha1.c_StructSize + 1)]
    [InlineData(Sha1.c_StructSize * 2)]
    public void Constructor_SpanLongerThanStructSize_ThrowsArgumentExceptionForData(int length)
    {
        byte[] data = new byte[length];

        var fromSpan = FluentActions.Invoking(() => new Sha1(new ReadOnlySpan<byte>(data))).Should().ThrowExactly<ArgumentException>().Which;
        var fromArray = FluentActions.Invoking(() => new Sha1(data)).Should().ThrowExactly<ArgumentException>().Which;

        foreach (var exception in new[] { fromSpan, fromArray })
        {
            exception.ParamName.Should().Be("data");
            exception.Message.Should().StartWith("Sha1 cannot be more than 20 bytes in length");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(Sha1.c_StructSize - 1)]
    public void Constructor_SpanShorterThanStructSize_ThrowsArgumentExceptionForData(int length)
    {
        byte[] data = new byte[length];

        var fromSpan = FluentActions.Invoking(() => new Sha1(new ReadOnlySpan<byte>(data))).Should().ThrowExactly<ArgumentException>().Which;
        var fromArray = FluentActions.Invoking(() => new Sha1(data)).Should().ThrowExactly<ArgumentException>().Which;

        foreach (var exception in new[] { fromSpan, fromArray })
        {
            exception.ParamName.Should().Be("data");
            exception.Message.Should().StartWith("Sha1 must be a minimum of 20 bytes in length");
        }
    }

    [Fact]
    public void Constructor_Span_ReadsEachComponentAsLittleEndian()
    {
        var sha1 = new Sha1(SequentialBytes(Sha1.c_StructSize).AsSpan());

        sha1.Should().Be(new Sha1(0x03020100, 0x07060504, 0x0B0A0908, 0x0F0E0D0C, 0x13121110));
    }

    [Fact]
    public void Constructor_StringFromToString_EqualsOriginal()
    {
        var sha1 = new Sha1(0x12345678, 0x9ABCDEF0, 0xFEDCBA98, 0x76543210, 0x13579BDF);

        var copy = new Sha1(sha1.ToString());

        copy.Should().Be(sha1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(38)]
    [InlineData(39)]
    public void Constructor_StringWithAnyCharacter_AcceptsWhatTheRuntimeHexParserAccepts(int position)
    {
        var characters = Enumerable.Range(0, 0x180)
            .Concat(Enumerable.Range(0x660, 10))
            .Concat(Enumerable.Range(0xFF10, 0x40))
            .Concat([0x1680, 0x2000, 0x2028, 0x3000, 0xD800, 0xFEFF, 0xFFFF]);

        foreach (int c in characters)
        {
            char[] chars = "0123456789ABCDEFabcdef0123456789abcdefAB".ToCharArray();
            chars[position] = (char)c;
            string hash = new(chars);

            if (TryParseWithRuntimeHexParser(hash, out byte[] expected, out string? expectedMessage))
            {
                new Sha1(hash).GetHashBuffer().Should().Equal(expected, $"U+{c:X4} at {position} is accepted by the runtime hex parser");
            }
            else
            {
                FluentActions.Invoking(() => new Sha1(hash)).Should().ThrowExactly<FormatException>($"U+{c:X4} at {position} is rejected by the runtime hex parser")
                    .Which.Message.Should().Be(expectedMessage);
            }
        }
    }

    [Fact]
    public void Constructor_StringWithInvalidCharacters_ThrowsFormatException()
    {
        FluentActions.Invoking(() => new Sha1("0CB57B7044818D2BAFDDE19E0345DC5808E50CZZ")).Should().ThrowExactly<FormatException>();
    }

    [Theory]
    [InlineData("TOOLONG123456789012345678901234567890")]
    [InlineData("TOOSHORT")]
    public void Constructor_StringWithInvalidLength_ThrowsArgumentException(string hash)
    {
        FluentActions.Invoking(() => new Sha1(hash)).Should().ThrowExactly<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0CB57B7044818D2BAFDDE19E0345DC5808E50CF")]
    [InlineData("0CB57B7044818D2BAFDDE19E0345DC5808E50CF20")]
    public void Constructor_StringWithInvalidLength_ThrowsArgumentExceptionForHash(string hash)
    {
        var exception = FluentActions.Invoking(() => new Sha1(hash)).Should().ThrowExactly<ArgumentException>().Which;

        exception.ParamName.Should().Be("hash");
        exception.Message.Should().StartWith("Sha1 must have a length of 40 characters.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(38)]
    [InlineData(39)]
    public void Constructor_StringWithNonHexCharacter_ThrowsFormatException(int position)
    {
        foreach (char c in new[] { ' ', '\t', '\n', 'G', 'g', 'x', 'X', 'Z', '-', '+', '/', ':', '@', '`', ' ', '٠', '０', 'Ａ' })
        {
            char[] chars = new string('0', 40).ToCharArray();
            chars[position] = c;
            string hash = new(chars);

            FluentActions.Invoking(() => new Sha1(hash)).Should().ThrowExactly<FormatException>();
        }
    }

    [Fact]
    public void Equals_BuffersDifferingInOneByte_ReturnsFalse()
    {
        byte[] buffer = RandomBytes(Sha1.c_StructSize);
        var original = new Sha1(buffer);

        buffer[10] += 1;
        var changed = new Sha1(buffer);
        var changedCopy = new Sha1(buffer);

        changed.Should().NotBe(original);
        changed.Should().Be(changedCopy);
    }

    [Fact]
    public void Equals_DifferentComponents_ReturnsFalse()
    {
        var a = new Sha1(1, 2, 3, 4, 5);
        var b = new Sha1(1, 2, 3, 4, 6);

        b.Should().NotBe(a);
        (a == b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        sha1.Equals(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Equals_OneComponentDifferent_ReturnsFalse(int component)
    {
        uint[] components = [1, 2, 3, 4, 5];
        var a = new Sha1(components[0], components[1], components[2], components[3], components[4]);
        components[component] ^= 0x80000000;
        var b = new Sha1(components[0], components[1], components[2], components[3], components[4]);

        a.Equals(b).Should().BeFalse();
        a.Equals((object)b).Should().BeFalse();
        (a == b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Equals_OtherType_ReturnsFalse()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        sha1.Equals("not a sha1").Should().BeFalse();
        sha1.Equals(12345).Should().BeFalse();
    }

    [Fact]
    public void Equals_SameComponents_ReturnsTrue()
    {
        var a = new Sha1(1, 2, 3, 4, 5);
        var b = new Sha1(1, 2, 3, 4, 5);

        b.Should().Be(a);
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
        b.GetHashCode().Should().Be(a.GetHashCode());
    }

    [Fact]
    public void From_ExistingInstance_ReplacesAllComponents()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        sha1.From(SequentialBytes(Sha1.c_StructSize));

        sha1.Should().Be(new Sha1(0x03020100, 0x07060504, 0x0B0A0908, 0x0F0E0D0C, 0x13121110));
    }

    [Theory]
    [InlineData(Sha1.c_StructSize - 1)]
    [InlineData(Sha1.c_StructSize + 1)]
    public void From_InvalidLength_ThrowsAndKeepsTheComponents(int length)
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        FluentActions.Invoking(() => sha1.From(new byte[length])).Should().ThrowExactly<ArgumentException>();

        sha1.Should().Be(new Sha1(1, 2, 3, 4, 5));
    }

    [Fact]
    public void GetHashBuffer_CalledTwice_ReturnsEqualCopies()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        byte[] first = sha1.GetHashBuffer();
        byte[] second = sha1.GetHashBuffer();

        second.Should().NotBeSameAs(first);
        second.Should().Equal(first);
    }

    [Theory]
    [MemberData(nameof(HashBuffers))]
    public void GetHashBuffer_ConstructedFromBytes_ReturnsSameBytes(byte[] buffer)
    {
        var sha1 = new Sha1(buffer);

        sha1.GetHashBuffer().Should().Equal(buffer);
    }

    [Fact]
    public void GetHashCode_CalledTwice_ReturnsSameValue()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);

        int first = sha1.GetHashCode();
        int second = sha1.GetHashCode();

        second.Should().Be(first);
    }

    [Fact]
    public void GetHashCode_DifferentComponents_ReturnsDifferentValues()
    {
        var a = new Sha1(1, 2, 3, 4, 5);
        var b = new Sha1(1, 2, 3, 4, 6);

        b.GetHashCode().Should().NotBe(a.GetHashCode());
    }

    [Fact]
    public void StructSize_Always_Is20()
    {
        Sha1.c_StructSize.Should().Be(20);
    }

    [Theory]
    [InlineData(0u, "0000000000000000000000000000000000000000")]
    [InlineData(uint.MaxValue, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    public void ToString_AllComponentsEqual_ReturnsRepeatedHexDigits(uint component, string expected)
    {
        var sha1 = new Sha1(component, component, component, component, component);

        sha1.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    [InlineData("123456789ABCDEF0123456789ABCDEF012345678")]
    [InlineData("FEDCBA9876543210FEDCBA9876543210FEDCBA98")]
    [InlineData("0123456789ABCDEFFEDCBA9876543210AAAAAAAA")]
    [InlineData("DA39A3EE5E6B4B0D3255BFEF95601890AFD80709")] // SHA-1 of the empty string.
    [InlineData("2FD4E1C67A2D28FCED849EE1BB76E7391B93EB12")] // SHA-1 of "The quick brown fox jumps over the lazy dog".
    public void ToString_ConstructedFromString_ReturnsUppercaseHash(string hash)
    {
        var sha1 = new Sha1(hash);

        sha1.ToString().Should().Be(hash.ToUpperInvariant());
    }

    [Fact]
    public void ToString_EveryByteValue_MatchesBitConverter()
    {
        byte[] values = [.. Enumerable.Range(0, 260).Select(i => (byte)i)];

        for (int start = 0; start + Sha1.c_StructSize <= values.Length; start += Sha1.c_StructSize)
        {
            byte[] digest = values.AsSpan(start, Sha1.c_StructSize).ToArray();

            new Sha1(digest).ToString().Should().Be(BitConverter.ToString(digest).Replace("-", string.Empty));
        }
    }

    [Fact]
    public void ToString_FromComponentsOrString_ReturnsUppercaseHash()
    {
        const string hash = "0CB57B7044818D2BAFDDE19E0345DC5808E50CF2";

        var fromComponents = new Sha1(1887155468, 730693956, 2665602479, 1490830595, 4060931336);
        var fromString = new Sha1(hash);

        fromComponents.ToString().Should().Be(hash);
        fromString.ToString().Should().Be(hash);
    }

    [Fact]
    public void ToString_SequentialBytes_ReturnsFortyUppercaseHexDigits()
    {
        var sha1 = new Sha1([.. Enumerable.Range(1, Sha1.c_StructSize).Select(i => (byte)i)]);

        string result = sha1.ToString();

        result.Length.Should().Be(40);
        result.Should().Be(result.ToUpperInvariant());
        result.Should().MatchRegex("^[0-9A-F]{40}$");
    }

    [Fact]
    public void ToString_Zero_ReturnsAllZeros()
    {
        Sha1.s_Zero.ToString().Should().Be(new string('0', 40));
    }

    [Fact]
    public void TryWriteBytes_DestinationTooSmall_LeavesDestinationUntouched()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);
        byte[] destination = [.. Enumerable.Repeat((byte)0xEE, Sha1.c_StructSize - 1)];

        sha1.TryWriteBytes(destination).Should().BeFalse();

        destination.Should().OnlyContain(b => b == 0xEE);
    }

    [Fact]
    public void TryWriteBytes_DestinationTooSmall_ReturnsFalse()
    {
        var sha1 = new Sha1(1, 2, 3, 4, 5);
        Span<byte> destination = stackalloc byte[Sha1.c_StructSize - 1];

        sha1.TryWriteBytes(destination).Should().BeFalse();
    }

    [Fact]
    public void TryWriteBytes_LargeEnoughDestination_WritesHashBuffer()
    {
        var sha1 = new Sha1(0x11223344, 0x55667788, 0x99AABBCC, 0xDDEEFF00, 0x01020304);
        Span<byte> destination = stackalloc byte[Sha1.c_StructSize];

        bool success = sha1.TryWriteBytes(destination);

        success.Should().BeTrue();
        destination.ToArray().Should().Equal(sha1.GetHashBuffer());
    }

    [Fact]
    public void TryWriteBytes_LargerDestination_WritesEachComponentLittleEndianAndLeavesTheRestUntouched()
    {
        var sha1 = new Sha1(0x11223344, 0x55667788, 0x99AABBCC, 0xDDEEFF00, 0x01020304);
        byte[] destination = [.. Enumerable.Repeat((byte)0xEE, Sha1.c_StructSize + 3)];

        sha1.TryWriteBytes(destination).Should().BeTrue();

        destination.Should().Equal(
            0x44, 0x33, 0x22, 0x11, 0x88, 0x77, 0x66, 0x55, 0xCC, 0xBB, 0xAA, 0x99, 0x00, 0xFF, 0xEE, 0xDD, 0x04, 0x03, 0x02, 0x01, 0xEE, 0xEE, 0xEE);
    }

    public static TheoryData<byte[]> HashBuffers =>
    [
        SequentialBytes(Sha1.c_StructSize),
        RandomBytes(Sha1.c_StructSize)
    ];
}