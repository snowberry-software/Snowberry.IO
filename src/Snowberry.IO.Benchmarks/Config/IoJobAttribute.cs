using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace Snowberry.IO.Benchmarks.Config;

/// <summary>
/// Runs every job of the benchmark class with <see cref="RunStrategy.Monitoring"/>, 2 warmup iterations and 10 measured iterations of one
/// invocation each, so file I/O is measured without a pilot stage and per-iteration cleanup runs after every operation.
/// </summary>
/// <remarks>
/// <para><see cref="RunStrategy.Monitoring"/> has no jitting stage and runs no warmup iterations unless a count is given, so without the
/// warmup iterations the first measured iteration would include the JIT compilation of the benchmarked code.</para>
/// <para>Unlike <see cref="SimpleJobAttribute"/>, this changes the jobs selected on the command line, such as <c>--job dry</c>, instead of
/// adding a second job.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class IoJobAttribute : JobMutatorConfigBaseAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IoJobAttribute"/> class.
    /// </summary>
    public IoJobAttribute()
        : base(new Job().WithStrategy(RunStrategy.Monitoring).WithWarmupCount(2).WithIterationCount(10))
    {
    }
}