using BenchmarkDotNet.Running;
using Snowberry.IO.Benchmarks.Config;

namespace Snowberry.IO.Benchmarks;

// Usage (from src/):
//   dotnet run -c Release -f net10.0 --project Snowberry.IO.Benchmarks -- --filter *
//   dotnet run -c Release -f net10.0 --project Snowberry.IO.Benchmarks -- --filter *Reader* --job short
//   dotnet run -c Release -f net10.0 --project Snowberry.IO.Benchmarks -- --filter *Convert* --runtimes net10.0 net48
//   dotnet run -c Release -f net10.0 --project Snowberry.IO.Benchmarks -- --list flat
//
// --runtimes net10.0 net48 runs every selected benchmark once per runtime, which measures the net10.0 build of Snowberry.IO next to
// its netstandard2.1 build (on .NET 8) and its netstandard2.0 build (on .NET Framework). The SingleFile benchmarks only exist for net10.0,
// so filter them out when net48 is included (for example --filter *Common* *Reader* *Writer* *Process*). Use --job dry to check
// that benchmarks run without measuring them.
//
// BenchmarkDotNet compiles the code that runs a benchmark for every selected runtime from the benchmarks it finds in the net10.0 build, so
// every benchmark method must exist on every target framework: put #if inside a benchmark method, never around one.

/// <summary>
/// The benchmark entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the benchmarks selected by <paramref name="args"/>, or prompts for them when no filter is given.
    /// </summary>
    /// <param name="args">The BenchmarkDotNet command-line arguments, such as <c>--filter</c>, <c>--job</c> and <c>--runtimes</c>.</param>
    public static void Main(string[] args)
    {
        _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, BenchmarkConfig.Create());
    }
}