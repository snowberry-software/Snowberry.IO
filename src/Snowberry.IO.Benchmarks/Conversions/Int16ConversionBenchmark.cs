using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using Snowberry.IO.Common;

namespace Snowberry.IO.Benchmarks.Conversions;

[MemoryDiagnoser]
[HtmlExporter]
[MarkdownExporterAttribute.GitHub]
[MediumRunJob]
public class Int16ConversionBenchmark
{
    private static byte[] s_Data = [0x12, 0x23, 0x14, 0x19];

    [Benchmark]
    public short BitConverter_Int16()
    {
        return BitConverter.ToInt16(s_Data);
    }

    [Benchmark]
    public short BinaryEndianConverter_Int16()
    {
        return BinaryEndianConverter.ToInt16(s_Data.AsSpan(), EndianType.LITTLE);
    }

    [Benchmark]
    public short BitConverter_Offset_Int16()
    {
        return BitConverter.ToInt16(s_Data, 2);
    }

    [Benchmark]
    public short BinaryEndianConverter_Offset_Int16()
    {
        return BinaryEndianConverter.ToInt16(s_Data.AsSpan(), 2, EndianType.LITTLE);
    }

    [Benchmark]
    public short BitConverter_BigE_Int16()
    {
        var s = s_Data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt16(s.ToArray(), 0);
    }

    [Benchmark]
    public short BinaryEndianConverter_BigE_Int16()
    {
        return BinaryEndianConverter.ToInt16(s_Data.AsSpan(), EndianType.BIG);
    }

    [Benchmark]
    public short BitConverter_Offset_BigE_Int16()
    {
        var s = s_Data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt16(s.ToArray(), 2);
    }

    [Benchmark]
    public short BinaryEndianConverter_Offset_BigE_Int16()
    {
        return BinaryEndianConverter.ToInt16(s_Data.AsSpan(), 2, EndianType.BIG);
    }
}