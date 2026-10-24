using System.Buffers.Binary;
using System.Text;
using AwesomeAssertions;
using Snowberry.IO.SingleFile.Format.Hosts;
using Xunit;
using static Snowberry.IO.Tests.Infrastructure.SingleFile.SyntheticMachOHost;

namespace Snowberry.IO.Tests.SingleFile;

/// <summary>
/// Contains tests for reading the load commands and the signing identifier of untrusted macOS executables.
/// </summary>
public class MachOImageTests
{
    // Load commands of the signed synthetic host: __PAGEZERO, __TEXT with one section, __DATA, __LINKEDIT, LC_SYMTAB and LC_CODE_SIGNATURE.
    private const int c_PageZero = 32;
    private const int c_Text = 104;
    private const int c_Data = 256;
    private const int c_LinkEdit = 328;
    private const int c_SymbolTable = 400;
    private const int c_CodeSignature = 424;
    private const int c_CommandsEnd = 440;

    public static TheoryData<string> InvalidLayouts => new()
    {
        "truncated header",
        "commands beyond header",
        "zero command size",
        "command beyond commands",
        "code signature not last",
        "duplicate __LINKEDIT",
        "duplicate LC_SYMTAB",
        "__LINKEDIT not last segment",
        "string table beyond __LINKEDIT",
        "signature far after string table",
        "too many sections",
        "commands reach __LINKEDIT",
        "file offset beyond long range"
    };

    [Theory]
    [MemberData(nameof(InvalidLayouts))]
    public void Parse_InvalidLayout_ReturnsNull(string layout)
    {
        byte[] host = Build(signingIdentifier: "MyApp");
        var span = host.AsSpan();

        switch (layout)
        {
            case "truncated header":
                host = host[..31];
                break;

            case "commands beyond header":
                BinaryPrimitives.WriteUInt32LittleEndian(span[20..], 70_000);
                break;

            case "zero command size":
                BinaryPrimitives.WriteUInt32LittleEndian(span[(c_PageZero + 4)..], 0);
                break;

            case "command beyond commands":
                BinaryPrimitives.WriteUInt32LittleEndian(span[(c_CodeSignature + 4)..], 4096);
                break;

            case "code signature not last":
                BinaryPrimitives.WriteUInt32LittleEndian(span[16..], 7);
                BinaryPrimitives.WriteUInt32LittleEndian(span[20..], c_CommandsEnd - 32 + 8);
                BinaryPrimitives.WriteUInt32LittleEndian(span[c_CommandsEnd..], 0x99);
                BinaryPrimitives.WriteUInt32LittleEndian(span[(c_CommandsEnd + 4)..], 8);
                break;

            case "duplicate __LINKEDIT":
                Encoding.ASCII.GetBytes("__LINKEDIT").CopyTo(span[(c_Data + 8)..]);
                break;

            case "duplicate LC_SYMTAB":
                BinaryPrimitives.WriteUInt32LittleEndian(span[c_PageZero..], 0x02);
                break;

            case "__LINKEDIT not last segment":
                BinaryPrimitives.WriteUInt64LittleEndian(span[(c_Data + 40)..], c_LinkEditOffset + 0x10);
                break;

            case "string table beyond __LINKEDIT":
                BinaryPrimitives.WriteUInt32LittleEndian(span[(c_SymbolTable + 20)..], 0x10_0000);
                break;

            case "signature far after string table":
                BinaryPrimitives.WriteUInt32LittleEndian(span[(c_SymbolTable + 20)..], 0x10);
                break;

            case "too many sections":
                BinaryPrimitives.WriteUInt32LittleEndian(span[(c_Text + 64)..], 2);
                break;

            case "commands reach __LINKEDIT":
                // Every segment starts at the file start, so only the load commands overlap __LINKEDIT.
                BinaryPrimitives.WriteUInt64LittleEndian(span[(c_Data + 40)..], 0);
                BinaryPrimitives.WriteUInt64LittleEndian(span[(c_LinkEdit + 40)..], 300);
                BinaryPrimitives.WriteUInt64LittleEndian(span[(c_LinkEdit + 48)..], (ulong)(host.Length - 300));
                break;

            case "file offset beyond long range":
                BinaryPrimitives.WriteUInt64LittleEndian(span[(c_LinkEdit + 40)..], ulong.MaxValue);
                break;
        }

        MachOImage.Parse(host).Should().BeNull();
    }

    [Theory]
    [InlineData(c_CpuTypeArm64, 16384)]
    [InlineData(c_CpuTypeX64, 4096)]
    public void Parse_SdkLayout_ReadsLoadCommands(uint cpuType, int pageSize)
    {
        byte[] host = Build(cpuType, "MyApp");

        var image = MachOImage.Parse(host)!;

        image.Should().NotBeNull();
        image.CommandCount.Should().Be(6);
        image.CommandsSize.Should().Be(c_CommandsEnd - 32);
        image.LinkEditCommandOffset.Should().Be(c_LinkEdit);
        image.SymbolTableCommandOffset.Should().Be(c_SymbolTable);
        image.CodeSignatureCommandOffset.Should().Be(c_CodeSignature);
        image.CodeSignatureOffset.Should().Be(c_StringTableEnd);
        image.UnsignedEnd.Should().Be(c_StringTableEnd);
        image.CodePageSize.Should().Be(pageSize);

        MachOImage.Parse(Build(cpuType))!.CodeSignatureCommandOffset.Should().Be(-1);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("empty")]
    [InlineData("bad magic")]
    [InlineData("blob count beyond buffer")]
    [InlineData("directory beyond buffer")]
    [InlineData("identifier beyond buffer")]
    [InlineData("missing terminator")]
    public void ReadIdentifier_UntrustedSignature_ReturnsIdentifierOnlyWhenValid(string variant)
    {
        byte[] signature = Build(signingIdentifier: "MyApp")[c_StringTableEnd..];
        var span = signature.AsSpan();

        switch (variant)
        {
            case "empty":
                signature = [];
                break;

            case "bad magic":
                span[0] = 0;
                break;

            case "blob count beyond buffer":
                BinaryPrimitives.WriteUInt32BigEndian(span[8..], uint.MaxValue);
                BinaryPrimitives.WriteUInt32BigEndian(span[12..], 1);
                break;

            case "directory beyond buffer":
                BinaryPrimitives.WriteUInt32BigEndian(span[16..], uint.MaxValue - 4);
                break;

            case "identifier beyond buffer":
                BinaryPrimitives.WriteUInt32BigEndian(span[(20 + 20)..], uint.MaxValue);
                break;

            case "missing terminator":
                signature = signature[..^1];
                break;
        }

        MachOCodeSignature.ReadIdentifier(signature).Should().Be(variant == "valid" ? "MyApp" : null);
    }
}
