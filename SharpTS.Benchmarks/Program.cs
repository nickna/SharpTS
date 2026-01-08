using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;

namespace SharpTS.Benchmarks;

/// <summary>
/// Entry point for SharpTS benchmark suite.
/// Configures BenchmarkDotNet with comprehensive diagnostics and reporting.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(HtmlExporter.Default)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(RankColumn.Arabic)
            .AddColumn(StatisticColumn.OperationsPerSecond)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
