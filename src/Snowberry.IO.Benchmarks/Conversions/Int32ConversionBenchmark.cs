using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Benchmarks.Conversions;

[MemoryDiagnoser]
[HtmlExporter]
[MarkdownExporterAttribute.GitHub]
[MediumRunJob]
public class Int32ConversionBenchmark
{
    private static byte[] _data = { 0x12, 0x23, 0x14, 0x19, 0x12, 0x23, 0x14, 0x19 };

    [Benchmark]
    public int BitConverter_Int32()
    {
        return BitConverter.ToInt32(_data);
    }

    [Benchmark]
    public unsafe int BinaryEndianConverter_Int32()
    {
        return BinaryEndianConverter.ToInt32(_data.AsSpan(), EndianType.LITTLE);
    }

    [Benchmark]
    public int BitConverter_Offset_Int32()
    {
        return BitConverter.ToInt32(_data, 4);
    }

    [Benchmark]
    public unsafe int BinaryEndianConverter_Offset_Int32()
    {
        return BinaryEndianConverter.ToInt32(_data.AsSpan(), 4, EndianType.LITTLE);
    }

    [Benchmark]
    public int BitConverter_BigE_Int32()
    {
        var s = _data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt32(s.ToArray(), 0);
    }

    [Benchmark]
    public unsafe int BinaryEndianConverter_BigE_Int32()
    {
        return BinaryEndianConverter.ToInt32(_data.AsSpan(), EndianType.BIG);
    }

    [Benchmark]
    public int BitConverter_Offset_BigE_Int32()
    {
        var s = _data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt32(s.ToArray(), 4);
    }

    [Benchmark]
    public unsafe int BinaryEndianConverter_Offset_BigE_Int32()
    {
        return BinaryEndianConverter.ToInt32(_data.AsSpan(), 4, EndianType.BIG);
    }
}
