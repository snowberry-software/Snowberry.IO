using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Snowberry.IO.Reader;
using Snowberry.IO.Reader.Interfaces;
using Snowberry.IO.Writer;
using Snowberry.IO.Writer.Interfaces;

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
