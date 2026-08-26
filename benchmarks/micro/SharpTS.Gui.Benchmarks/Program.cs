using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;
using SharpTS.Benchmarks;

var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddExporter(JsonExporter.FullCompressed);
var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
StructuredBenchmarkMetadata.Write(summaries);
