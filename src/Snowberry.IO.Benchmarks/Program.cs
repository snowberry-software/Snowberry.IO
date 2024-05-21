using BenchmarkDotNet.Running;
using Snowberry.IO.Benchmarks.Conversions;

internal class Program
{
    private static void Main(string[] args)
    {
        //_ = BenchmarkRunner.Run(typeof(Int16ConversionBenchmark));
        //_ = BenchmarkRunner.Run(typeof(Int32ConversionBenchmark));
        //_ = BenchmarkRunner.Run(typeof(UInt32ConversionBenchmark));
        _ = BenchmarkRunner.Run(typeof(Int64ConversionBenchmark));
    }
}