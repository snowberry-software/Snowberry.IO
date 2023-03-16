using System.Text;
using Snowberry.IO.Extensions;
using Snowberry.IO.Reader.Interfaces;
using Snowberry.IO.Writer.Interfaces;
using Xunit;

using static Snowberry.IO.Tests.TestHelper;

namespace Snowberry.IO.Tests;

/// <summary>
/// Contains tests for the <see cref="IEndianReader"/> and the <see cref="IEndianWriter"/>.
/// </summary>
public class SharedTests
{
    [Theory]
    [InlineData("TestSizedString1", false)]
    [InlineData("TestSizedString😊_{}", true)]
    [InlineData("TestSizedString😊😊😊👳‍♂️_{}😊😊👳‍😊😊👳‍😊😊👳‍😊😊👳‍TestSizedString😊😊😊👳", true, 500)]
    private void ReadWrite_SizedCString_Unicode(string expected, bool isUnicode, int minFixedSize = 0)
    {
        ReadWrite_SizedCString(Encoding.UTF8, expected, isUnicode, minFixedSize);
    }

    [Theory]
    [InlineData("TestSizedString1")]
    [InlineData("TestSizedString{}")]
    [InlineData("TestSizedStringTestSizedString", 500)]
    private void ReadWrite_SizedCString_ASCII(string expected, int minFixedSize = 0)
    {
        ReadWrite_SizedCString(Encoding.ASCII, expected, false, minFixedSize);
    }

    private void ReadWrite_SizedCString(Encoding encoding, string expected, bool isUnicode, int minFixedSize = 0)
    {
        int actualStringPayloadSize = expected.Length;
        CreateShared((x, _) =>
        {
            if (isUnicode)
                actualStringPayloadSize = x.Encoding.GetByteCount(expected.AsSpan());

            if (actualStringPayloadSize < minFixedSize)
                actualStringPayloadSize = minFixedSize;

            x.WriteSizedCString(expected, actualStringPayloadSize);
            x.WriteSizedCString(expected, actualStringPayloadSize);
        },
        (x, _) =>
        {
            string? read = x.ReadSizedCString(actualStringPayloadSize);
            read = x.ReadSizedCString(actualStringPayloadSize);

            Assert.NotNull(read);
            Assert.Equal(actualStringPayloadSize * 2, x.ActualPosition);
            Assert.Equal(expected.Length, read!.Length);
            Assert.Equal(expected, read);
        }, encoding);
    }

    [Theory]
    [InlineData("TestCString1")]
    [InlineData("TestCString😊😊👳‍♂️_{}")]
    private void ReadWrite_CString(string expected)
    {
        CreateShared((x, _) =>
        {
            x.WriteCString(expected);
        },
        (x, _) =>
        {
            string? read = x.ReadCString();

            Assert.NotNull(read);
            Assert.Equal(expected, read);
        });
    }

    [Theory]
    [InlineData("#+#+�{}/``!^#/���")]
    [InlineData("**```���_.,��!!�@")]
    private void ReadWrite_SizePrefixedString(string expected)
    {
        Assert.True(expected.Length <= 100);

        CreateShared((x, _) =>
        {
            x.Write(expected);
        },
        (x, _) =>
        {
            string? read = x.ReadString();

            Assert.NotNull(read);
            Assert.Equal(expected, read);
        });
    }

    [Theory]
    [InlineData("#+#+�{}/``!^#/���")]
    [InlineData("**```���_.,��!!�@")]
    private void ReadWrite_NewLine(string lineText)
    {
        CreateShared((x, _) =>
        {
            x.WriteLine(lineText);
        },
        (x, _) =>
        {
            Assert.Equal(lineText, x.ReadLine());
        });
    }

    // ######################################################################################################################################################################################
    // # DATA TYPES
    // ######################################################################################################################################################################################

    [Theory]
    [InlineData(sbyte.MaxValue)]
    [InlineData(sbyte.MinValue)]
    private void ReadWrite_Int8_LE(sbyte value)
    {
        CreateShared((x, _) =>
        {
            x.Write(value);
            Assert.Equal(sizeof(sbyte), x.Length);
        },
        (x, _) =>
        {
            Assert.Equal(value, (sbyte)x.ReadByte());
        });
    }

    [Theory]
    [InlineData(byte.MaxValue)]
    [InlineData(byte.MinValue)]
    private void ReadWrite_UInt8_LE(byte value)
    {
        CreateShared((x, _) =>
        {
            x.Write(value);
            Assert.Equal(sizeof(byte), x.Length);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadByte());
        });
    }

    [Theory]
    [InlineData(200)]
    [InlineData(short.MaxValue)]
    [InlineData(short.MinValue)]
    private void ReadWrite_Int16_LE_BE(short value)
    {
        const int offset = 20;

        //((EndianStreamWriter)null).Write((byte)22);

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadInt16());
            Assert.Equal(value, x.ReadInt16(EndianType.BIG));

            int bufferSize = offset + (sizeof(short) * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadInt16At(offset: offset));
            Assert.Equal(value, x.ReadInt16At(EndianType.BIG, offset + sizeof(short)));
        });
    }

    [Theory]
    [InlineData(200)]
    [InlineData(ushort.MaxValue)]
    [InlineData(ushort.MinValue)]
    private void ReadWrite_UInt16_LE_BE(ushort value)
    {
        const int offset = 20;

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadUInt16());
            Assert.Equal(value, x.ReadUInt16(EndianType.BIG));

            int bufferSize = offset + (sizeof(ushort) * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadUInt16At(offset: offset));
            Assert.Equal(value, x.ReadUInt16At(EndianType.BIG, offset + sizeof(ushort)));
        });
    }

    [Theory]
    [InlineData(200)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    private void ReadWrite_Int32_LE_BE(int value)
    {
        const int offset = 20;

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadInt32());
            Assert.Equal(value, x.ReadInt32(EndianType.BIG));

            int bufferSize = offset + (sizeof(int) * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadInt32At(offset: offset));
            Assert.Equal(value, x.ReadInt32At(EndianType.BIG, offset + sizeof(int)));
        });
    }

    [Theory]
    [InlineData(200)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    private void ReadWrite_7Bit_Int32(int value)
    {
        CreateShared((x, _) =>
        {
            x.Write7BitEncodedInt(value);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.Read7BitEncodedInt());
        });
    }

    [Theory]
    [InlineData(200L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    private void ReadWrite_7Bit_Int64(long value)
    {
        CreateShared((x, _) =>
        {
            x.Write7BitEncodedInt64(value);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.Read7BitEncodedLong());
        });
    }

    [Theory]
    [InlineData(200u)]
    [InlineData(uint.MaxValue)]
    [InlineData(uint.MinValue)]
    private void ReadWrite_UInt32_LE_BE(uint value)
    {
        const int offset = 20;

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadUInt32());
            Assert.Equal(value, x.ReadUInt32(EndianType.BIG));

            int bufferSize = offset + (sizeof(uint) * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadUInt32At(offset: offset));
            Assert.Equal(value, x.ReadUInt32At(EndianType.BIG, offset + sizeof(uint)));
        });
    }

    [Theory]
    [InlineData(200L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    private void ReadWrite_Int64_LE_BE(long value)
    {
        const int offset = 20;

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadLong());
            Assert.Equal(value, x.ReadLong(EndianType.BIG));

            int bufferSize = offset + (sizeof(long) * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadLongAt(offset: offset));
            Assert.Equal(value, x.ReadLongAt(EndianType.BIG, offset + sizeof(long)));
        });
    }

    [Theory]
    [InlineData(200ul)]
    [InlineData(ulong.MaxValue)]
    [InlineData(ulong.MinValue)]
    private void ReadWrite_UInt64_LE_BE(ulong value)
    {
        const int offset = 20;

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadULong());
            Assert.Equal(value, x.ReadULong(EndianType.BIG));

            int bufferSize = offset + (sizeof(ulong) * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadULongAt(offset: offset));
            Assert.Equal(value, x.ReadULongAt(EndianType.BIG, offset + sizeof(ulong)));
        });
    }

    [Theory]
    [InlineData(200F)]
    [InlineData(200.8888111F)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    private void ReadWrite_Float_LE_BE(float value)
    {
        const int offset = 20;

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadFloat());
            Assert.Equal(value, x.ReadFloat(EndianType.BIG));

            int bufferSize = offset + (sizeof(float) * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadFloatAt(offset: offset));
            Assert.Equal(value, x.ReadFloatAt(EndianType.BIG, offset + sizeof(float)));
        });
    }

    [Theory]
    [InlineData(200D)]
    [InlineData(200.88881111111111111111111111111111D)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    private void ReadWrite_Double_LE_BE(double value)
    {
        const int offset = 20;

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadDouble());
            Assert.Equal(value, x.ReadDouble(EndianType.BIG));

            int bufferSize = offset + (sizeof(double) * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadDoubleAt(offset: offset));
            Assert.Equal(value, x.ReadDoubleAt(EndianType.BIG, offset + sizeof(double)));
        });
    }

    [Fact]
    private void ReadWrite_Guid_LE_BE()
    {
        const int offset = 20;

        var value = Guid.NewGuid();

        CreateShared((x, _) =>
        {
            x.Write(value);
            x.Write(value, EndianType.BIG);

            x.Position += offset;

            x.Write(value);
            x.Write(value, EndianType.BIG);
        },
        (x, _) =>
        {
            Assert.Equal(value, x.ReadGuid());
            Assert.Equal(value, x.ReadGuid(EndianType.BIG));

            int bufferSize = offset + (16 * 2);
            x.EnsureBufferSize(bufferSize);
            x.ReadInInternalBuffer(bufferSize, 0);

            Assert.Equal(value, x.ReadGuidAt(offset: offset));
            Assert.Equal(value, x.ReadGuidAt(EndianType.BIG, offset + 16));
        });
    }

    [Fact]
    private void ReadWrite_ByteArray()
    {
        CreateShared((x, _) =>
        {
            x.Write(new byte[] { 0x12, 0x13, 0x14 });
        },
        (x, _) =>
        {
            byte[] array = x.ReadBytes(3);

            Assert.Equal(0x12, array[0]);
            Assert.Equal(0x13, array[1]);
            Assert.Equal(0x14, array[2]);
        });
    }
}
