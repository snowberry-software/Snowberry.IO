using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using Snowberry.IO.Common;
using Snowberry.IO.Common.Reader.Interfaces;

namespace Snowberry.IO.Benchmarks.Conversions;

[MemoryDiagnoser]
[HtmlExporter]
[MarkdownExporterAttribute.GitHub]
[MediumRunJob]
public class Int64ConversionBenchmark
{
    private static byte[] _data = { 0x12, 0x23, 0x14, 0x19, 0x12, 0x23, 0x14, 0x19, 0x12, 0x23, 0x14, 0x19, 0x12, 0x23, 0x14, 0x19 };

    [Benchmark]
    public long BitConverter_Int64()
    {
        return BitConverter.ToInt64(_data);
    }

    [Benchmark]
    public unsafe long BinaryEndianConverter_Int64()
    {
        return BinaryEndianConverter.ToLong(_data.AsSpan(), EndianType.BIG);
    }

    [Benchmark]
    public long BitConverter_Offset_Int64()
    {
        return BitConverter.ToInt64(_data, 4);
    }

    [Benchmark]
    public unsafe long BinaryEndianConverter_Offset_Int64()
    {
        return BinaryEndianConverter.ToLong(_data.AsSpan(), 4, EndianType.LITTLE);
    }

    [Benchmark]
    public long BitConverter_BigE_Int64()
    {
        var s = _data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt64(s.ToArray(), 0);
    }

    [Benchmark]
    public unsafe long BinaryEndianConverter_BigE_Int64()
    {
        return BinaryEndianConverter.ToLong(_data.AsSpan(), EndianType.BIG);
    }

    [Benchmark]
    public long BitConverter_Offset_BigE_Int64()
    {
        var s = _data.AsSpan();
        s.Reverse();
        return BitConverter.ToInt64(s.ToArray(), 4);
    }

    [Benchmark]
    public unsafe long BinaryEndianConverter_Offset_BigE_Int64()
    {
        return BinaryEndianConverter.ToLong(_data.AsSpan(), 4, EndianType.BIG);
    }
}
