using System.Text;
using Snowberry.IO.Reader;
using Snowberry.IO.Writer;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;
using Snowberry.IO.Common.Writer.Interfaces;

namespace Snowberry.IO.Tests;

public class TestHelper
{
    public static void CreateShared(Action<IEndianWriter, MemoryStream> writerAction, Action<IEndianReader, MemoryStream> readerAction, Encoding? encoding = null)
    {
        var mem = new MemoryStream();
        using var writer = new EndianStreamWriter(mem, true, encoding ?? Encoding.UTF8);

        writerAction(writer, mem);

        mem.Position = 0;

        using var reader = new EndianStreamReader(mem, null, 0, encoding ?? Encoding.UTF8);

        readerAction(reader, mem);
    }
}
