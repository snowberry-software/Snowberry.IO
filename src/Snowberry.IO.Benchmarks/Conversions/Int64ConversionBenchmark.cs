using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using Snowberry.IO.Common;

namespace Snowberry.IO.Benchmarks.Conversions;

[MemoryDiagnoser]
[HtmlExporter]
[MarkdownExporterAttribute.GitHub]
[MediumRunJob]
public class Int64ConversionBenchmark
{
    private static byte[] s_Data = [0x12, 0x23, 0x14, 0x19, 0x12, 0x23, 0x14, 0x19, 0x12, 0x23, 0x14, 0x19, 0x12, 0x23, 0x14, 0x19];

    [Benchmark]
    public long BitConverter_Int64()
    {
        return BitConverter.ToInt64(s_Data);
    }

    [Benchmark]
    public long BinaryEndianConverter_Int64()
    {
        return BinaryEndianConverter.ToInt64(s_Data.AsSpan(), EndianType.LITTLE);
    }

    [Benchmark]
    public long BitConverter_Offset_Int64()
    {
        return BitConverter.ToInt64(s_Data, 4);
    }

    [Benchmark]
    public long BinaryEndianConverter_Offset_Int64()
    {
        return BinaryEndianConverter.ToInt64(s_Data.AsSpan(), 4, EndianType.LITTLE);
    }

    [Benchmark]
    public long BitConverter_BigE_Int64()
    {
        var s = s_Data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt64(s.ToArray(), 0);
    }

    [Benchmark]
    public long BinaryEndianConverter_BigE_Int64()
    {
        return BinaryEndianConverter.ToInt64(s_Data.AsSpan(), EndianType.BIG);
    }

    [Benchmark]
    public long BitConverter_Offset_BigE_Int64()
    {
        var s = s_Data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt64(s.ToArray(), 4);
    }

    [Benchmark]
    public long BinaryEndianConverter_Offset_BigE_Int64()
    {
        return BinaryEndianConverter.ToInt64(s_Data.AsSpan(), 4, EndianType.BIG);
    }
}