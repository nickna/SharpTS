using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Order;
using SharpTS.Microbenchmarks.Infrastructure;

namespace SharpTS.Microbenchmarks;

/// <summary>
/// Entry point for SharpTS benchmark suite.
/// Configures BenchmarkDotNet with comprehensive diagnostics and reporting.
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        if (args is ["--smoke"])
        {
            CompileEmbeddedTypeScript();
            args = ["--list", "flat"];
        }

        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(HtmlExporter.Default)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(RankColumn.Arabic)
            .AddColumn(StatisticColumn.OperationsPerSecond)
            .WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }

    private static void CompileEmbeddedTypeScript()
    {
        var assembly = typeof(Program).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".ts", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
        {
            throw new InvalidOperationException("No embedded TypeScript benchmark sources were found");
        }

        for (var index = 0; index < resources.Length; index++)
        {
            var resource = resources[index];
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Could not open embedded resource {resource}");
            using var reader = new StreamReader(stream);
            BenchmarkHarness.CompileTypeScript(reader.ReadToEnd(), $"Smoke{index}");
            Console.WriteLine($"Smoke-compiled {resource}");
        }
    }
}
