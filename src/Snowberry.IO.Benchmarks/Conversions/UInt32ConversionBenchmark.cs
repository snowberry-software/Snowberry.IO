using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Benchmarks.Conversions;

[MemoryDiagnoser]
[HtmlExporter]
[MarkdownExporterAttribute.GitHub]
[MediumRunJob]
public class UInt32ConversionBenchmark
{
    private static byte[] _data = { 0xD6, 0x87, 0x00, 0x00, 0xD6, 0x87, 0x00, 0x00 };

    [Benchmark]
    public uint BitConverter_UInt32()
    {
        return BitConverter.ToUInt32(_data);
    }

    [Benchmark]
    public unsafe uint BinaryEndianConverter_UInt32()
    {
        return BinaryEndianConverter.ToUInt32(_data.AsSpan(), EndianType.LITTLE);
    }

    [Benchmark]
    public uint BitConverter_Offset_UInt32()
    {
        return BitConverter.ToUInt32(_data, 4);
    }

    [Benchmark]
    public unsafe uint BinaryEndianConverter_Offset_UInt32()
    {
        return BinaryEndianConverter.ToUInt32(_data.AsSpan(), 4, EndianType.LITTLE);
    }

    [Benchmark]
    public uint BitConverter_BigE_UInt32()
    {
        var s = _data.AsSpan();
        s.Reverse();
        return BitConverter.ToUInt32(s.ToArray(), 0);
    }

    [Benchmark]
    public unsafe uint BinaryEndianConverter_BigE_UInt32()
    {
        return BinaryEndianConverter.ToUInt32(_data.AsSpan(), EndianType.BIG);
    }

    [Benchmark]
    public uint BitConverter_Offset_BigE_UInt32()
    {
        var s = _data.AsSpan();
        s.Reverse();
        return BitConverter.ToUInt32(s.ToArray(), 4);
    }

    [Benchmark]
    public unsafe uint BinaryEndianConverter_Offset_BigE_UInt32()
    {
        return BinaryEndianConverter.ToUInt32(_data.AsSpan(), 4, EndianType.BIG);
    }
}
