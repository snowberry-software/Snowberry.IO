using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;

namespace Snowberry.IO.Benchmarks.Config;

/// <summary>
/// Provides the configuration shared by every benchmark in this assembly.
/// </summary>
public static class BenchmarkConfig
{
    /// <summary>
    /// Creates the shared configuration: the memory diagnoser, the GitHub Markdown, HTML and full JSON exporters, and the ratio and
    /// standard deviation columns.
    /// </summary>
    /// <remarks>
    /// No job is declared, so the job and runtimes come from the command line (for example <c>--job short</c> or
    /// <c>--runtimes net10.0 net48</c>), falling back to the default job.
    /// </remarks>
    /// <returns>The configuration to pass to the benchmark runner.</returns>
    public static IConfig Create()
    {
        var defaults = DefaultConfig.Instance;

        return ManualConfig.CreateEmpty()
            .AddLogger(ConsoleLogger.Default)
            .AddColumnProvider(DefaultColumnProviders.Instance)
            .AddColumn(StatisticColumn.StdDev, BaselineRatioColumn.RatioMean, BaselineRatioColumn.RatioStdDev)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(MarkdownExporter.GitHub, HtmlExporter.Default, JsonExporter.Full)
            .AddAnalyser(defaults.GetAnalysers().ToArray())
            .AddValidator(defaults.GetValidators().ToArray())
            .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40));
    }
}