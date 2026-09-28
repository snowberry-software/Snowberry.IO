using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using Snowberry.IO.Common;

namespace Snowberry.IO.Benchmarks.Conversions;

[MemoryDiagnoser]
[HtmlExporter]
[MarkdownExporterAttribute.GitHub]
[MediumRunJob]
public class UInt32ConversionBenchmark
{
    private static byte[] s_Data = [0xD6, 0x87, 0x00, 0x00, 0xD6, 0x87, 0x00, 0x00];

    [Benchmark]
    public uint BitConverter_UInt32()
    {
        return BitConverter.ToUInt32(s_Data);
    }

    [Benchmark]
    public uint BinaryEndianConverter_UInt32()
    {
        return BinaryEndianConverter.ToUInt32(s_Data.AsSpan(), EndianType.LITTLE);
    }

    [Benchmark]
    public uint BitConverter_Offset_UInt32()
    {
        return BitConverter.ToUInt32(s_Data, 4);
    }

    [Benchmark]
    public uint BinaryEndianConverter_Offset_UInt32()
    {
        return BinaryEndianConverter.ToUInt32(s_Data.AsSpan(), 4, EndianType.LITTLE);
    }

    [Benchmark]
    public uint BitConverter_BigE_UInt32()
    {
        var s = s_Data.AsSpan();
        s.Reverse();
        return BitConverter.ToUInt32(s.ToArray(), 0);
    }

    [Benchmark]
    public uint BinaryEndianConverter_BigE_UInt32()
    {
        return BinaryEndianConverter.ToUInt32(s_Data.AsSpan(), EndianType.BIG);
    }

    [Benchmark]
    public uint BitConverter_Offset_BigE_UInt32()
    {
        var s = s_Data.AsSpan();
        s.Reverse();
        return BitConverter.ToUInt32(s.ToArray(), 4);
    }

    [Benchmark]
    public uint BinaryEndianConverter_Offset_BigE_UInt32()
    {
        return BinaryEndianConverter.ToUInt32(s_Data.AsSpan(), 4, EndianType.BIG);
    }
}