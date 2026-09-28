using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using Snowberry.IO.Common;

namespace Snowberry.IO.Benchmarks.Conversions;

[MemoryDiagnoser]
[HtmlExporter]
[MarkdownExporterAttribute.GitHub]
[MediumRunJob]
public class Int32ConversionBenchmark
{
    private static byte[] s_Data = [0x12, 0x23, 0x14, 0x19, 0x12, 0x23, 0x14, 0x19];

    [Benchmark]
    public int BitConverter_Int32()
    {
        return BitConverter.ToInt32(s_Data);
    }

    [Benchmark]
    public int BinaryEndianConverter_Int32()
    {
        return BinaryEndianConverter.ToInt32(s_Data.AsSpan(), EndianType.LITTLE);
    }

    [Benchmark]
    public int BitConverter_Offset_Int32()
    {
        return BitConverter.ToInt32(s_Data, 4);
    }

    [Benchmark]
    public int BinaryEndianConverter_Offset_Int32()
    {
        return BinaryEndianConverter.ToInt32(s_Data.AsSpan(), 4, EndianType.LITTLE);
    }

    [Benchmark]
    public int BitConverter_BigE_Int32()
    {
        var s = s_Data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt32(s.ToArray(), 0);
    }

    [Benchmark]
    public int BinaryEndianConverter_BigE_Int32()
    {
        return BinaryEndianConverter.ToInt32(s_Data.AsSpan(), EndianType.BIG);
    }

    [Benchmark]
    public int BitConverter_Offset_BigE_Int32()
    {
        var s = s_Data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt32(s.ToArray(), 4);
    }

    [Benchmark]
    public int BinaryEndianConverter_Offset_BigE_Int32()
    {
        return BinaryEndianConverter.ToInt32(s_Data.AsSpan(), 4, EndianType.BIG);
    }
}