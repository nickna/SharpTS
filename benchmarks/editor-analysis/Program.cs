using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Conversions;
using SharpTS.LanguageServer.Services;
using SharpTS.TypeSystem;

const int coldSamples = 11;
const int warmSamples = 31;
string artifactRoot = Path.GetFullPath(args[0]);
Directory.CreateDirectory(artifactRoot);
var reports = new List<object>();
foreach (string name in new[] { "small", "multi-project" })
{
    Fixture fixture = Fixture.Create(Path.Combine(artifactRoot, "fixtures", name), name == "multi-project");
    // Process-local JIT and embedded resources are deliberately warmed before cache-cold samples.
    using (var prewarm = new QuerySession(fixture))
        for (int i = 0; i < 3; i++) await prewarm.RunAsync();

    var cold = new List<Sample>();
    for (int i = 0; i < coldSamples; i++)
    {
        Collect();
        using var session = new QuerySession(fixture);
        cold.Add(await Measure(session));
    }

    var warm = new List<Sample>();
    object? validation;
    using (var session = new QuerySession(fixture))
    {
        await session.RunAsync();
        foreach (int _ in Enumerable.Range(0, warmSamples)) warm.Add(await Measure(session));
        validation = await session.MeasureValidationAsync(warmSamples);
    }
    Retention retention = MeasureRetention(fixture);
    reports.Add(new
    {
        name,
        fixture.SourceFiles,
        fixture.Projects,
        sourceBytes = fixture.SourceBytes,
        cold = Summarize(cold),
        warm = Summarize(warm),
        retention,
        validation,
    });
}
var result = new
{
#if BASELINE
    implementation = args.Length > 2 ? args[2] : "df4589b7",
#else
    implementation = "shared-snapshots-current",
#endif
    timestampUtc = DateTime.UtcNow,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    processorCount = Environment.ProcessorCount,
    coldSamples,
    warmSamples,
    methodology = "Same definition, references(includeDeclaration=true, workspace roots), statements, full diagnostics sequence. Fresh service cache for each cold sample; repeated unchanged capture for warm samples. Process/JIT/filesystem caches warmed. Deterministic noLib/types=[] fixtures isolate source graph. Services only, no transport or CLR metadata provider. Allocation measured process-wide across async workers. Baseline CheckModules entry counter is the only archived production source change; current uses Statistics.Checks. Retained heap is noisy process-wide full-GC live heap, cache bytes are implementation estimates.",
    reports,
};
string output = Path.Combine(artifactRoot, args[1]);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);

static async Task<Sample> Measure(QuerySession session)
{
    long beforeCount = session.Checks;
    long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var watch = Stopwatch.StartNew();
    string result = await session.RunAsync();
    watch.Stop();
    return new(watch.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore,
        session.Checks - beforeCount, result, session.LastFeatureTimes, session.LastFeatureAllocations);
}

static object Summarize(List<Sample> samples)
{
    if (samples.Select(sample => sample.Result).Distinct().Count() != 1)
        throw new InvalidOperationException("Equivalent requests changed result within a measurement phase.");
    double[] times = samples.Select(sample => sample.Milliseconds).Order().ToArray();
    long[] allocations = samples.Select(sample => sample.AllocatedBytes).Order().ToArray();
    return new
    {
        medianMilliseconds = times[times.Length / 2],
        p90Milliseconds = times[(int)Math.Ceiling(times.Length * .9) - 1],
        medianAllocatedBytes = allocations[allocations.Length / 2],
        checksPerSequence = samples.Select(sample => sample.Checks).Distinct().Order().ToArray(),
        resultFingerprint = samples[0].Result,
        featureMedianMilliseconds = Enumerable.Range(0, 3).Select(index =>
            samples.Select(sample => sample.FeatureMilliseconds[index]).Order().ElementAt(samples.Count / 2)).ToArray(),
        featureMedianAllocatedBytes = Enumerable.Range(0, 3).Select(index =>
            samples.Select(sample => sample.FeatureAllocatedBytes[index]).Order().ElementAt(samples.Count / 2)).ToArray(),
        samples,
    };
}

static void Collect()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
}

[MethodImpl(MethodImplOptions.NoInlining)]
static Retention MeasureRetention(Fixture fixture)
{
    using var session = new QuerySession(fixture);
    PopulateAndDropTemporaryReferences(session);
    Collect();
    long withCache = GC.GetTotalMemory(forceFullCollection: true);
    long cacheEstimate = session.CachedBytes;
    int snapshots = session.CachedSnapshots;
    session.ClearCaches();
    Collect();
    long afterClear = GC.GetTotalMemory(forceFullCollection: true);
    GC.KeepAlive(session);
    return new(withCache, afterClear, withCache - afterClear,
        cacheEstimate < 0 ? null : cacheEstimate, snapshots < 0 ? null : snapshots);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void PopulateAndDropTemporaryReferences(QuerySession session)
{
    // Synchronous, separate frame so completed query tasks and AST locals cannot contaminate
    // the full-GC comparison. No lease survives this method.
    session.RunAsync().GetAwaiter().GetResult();
}

record Sample(double Milliseconds, long AllocatedBytes, long Checks, string Result,
    double[] FeatureMilliseconds, long[] FeatureAllocatedBytes);
record Retention(long LiveHeapWithCacheBytes, long LiveHeapAfterCacheClearBytes,
    long CacheRetainedHeapBytes, long? EstimatedCachedBytes, int? CachedSnapshots);

sealed class QuerySession : IDisposable
{
    readonly Fixture _fixture;
    readonly DefinitionService _definitions;
    readonly ReferenceService _references;
    readonly DiagnosticsService _diagnostics;
    public double[] LastFeatureTimes { get; private set; } = [];
    public long[] LastFeatureAllocations { get; private set; } = [];
#if !BASELINE
    readonly SemanticAnalysisService _analysis;
#endif
    public QuerySession(Fixture fixture)
    {
        _fixture = fixture;
#if BASELINE
        _definitions = new();
        _references = new();
        _diagnostics = new();
#else
        var workspace = new NavigationWorkspaceContext();
        workspace.Initialize(new InitializeParams { RootUri = new Uri(fixture.Root).AbsoluteUri });
        _analysis = new(workspace);
        _definitions = new(_analysis);
        _references = new(_analysis);
        _diagnostics = new(analysis: _analysis);
#endif
    }
    public long Checks =>
#if BASELINE
        Interlocked.Read(ref TypeChecker.BenchmarkChecks);
#else
        _analysis.Statistics.Checks;
#endif
    public long CachedBytes =>
#if BASELINE
        -1;
#else
        _analysis.Statistics.EstimatedRetainedBytes;
#endif
    public int CachedSnapshots =>
#if BASELINE
        -1;
#else
        _analysis.Statistics.RetainedSnapshots;
#endif
    public async Task<string> RunAsync()
    {
        var featureTimes = new double[3];
        var featureAllocations = new long[3];
        long featureAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var featureWatch = Stopwatch.StartNew();
#if BASELINE
        var definitions = _definitions.FindDefinitions(_fixture.Path, _fixture.Text,
            _fixture.Position, _fixture.Capture.TextOverlay);
        featureTimes[0] = featureWatch.Elapsed.TotalMilliseconds;
        featureAllocations[0] = GC.GetTotalAllocatedBytes(precise: true) - featureAllocatedBefore;
        featureAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        featureWatch.Restart();
        var references = _references.FindReferenceResult(_fixture.Path, _fixture.Text,
            _fixture.Position, true, _fixture.Capture.TextOverlay, [_fixture.Root]);
        featureTimes[1] = featureWatch.Elapsed.TotalMilliseconds;
        featureAllocations[1] = GC.GetTotalAllocatedBytes(precise: true) - featureAllocatedBefore;
        featureAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        featureWatch.Restart();
        var statements = _diagnostics.GetStatements(_fixture.Capture.Document, CancellationToken.None);
        var diagnostics = _diagnostics.Analyze(_fixture.Capture, _fixture.Capture.Document,
            DiagnosticPublishMode.All, CancellationToken.None);
        await Task.CompletedTask;
#else
        var definitionResult = await _definitions.FindDefinitionsAsync(_fixture.Capture,
            _fixture.Position, CancellationToken.None);
        if (!definitionResult.IsCurrent()) throw new InvalidOperationException("Stale definition");
        var definitions = definitionResult.Locations;
        featureTimes[0] = featureWatch.Elapsed.TotalMilliseconds;
        featureAllocations[0] = GC.GetTotalAllocatedBytes(precise: true) - featureAllocatedBefore;
        featureAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        featureWatch.Restart();
        var references = await _references.FindReferenceResultAsync(_fixture.Capture,
            _fixture.Position, true, [_fixture.Root], cancellationToken: CancellationToken.None);
        if (!references.IsCurrent()) throw new InvalidOperationException("Stale references");
        featureTimes[1] = featureWatch.Elapsed.TotalMilliseconds;
        featureAllocations[1] = GC.GetTotalAllocatedBytes(precise: true) - featureAllocatedBefore;
        featureAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        featureWatch.Restart();
        var statements = await _diagnostics.GetStatementsAsync(_fixture.Capture,
            DiagnosticPublishMode.All, CancellationToken.None);
        var diagnosticsResult = await _diagnostics.AnalyzeResultAsync(_fixture.Capture,
            _fixture.Capture.Document, DiagnosticPublishMode.All, CancellationToken.None);
        if (!diagnosticsResult.IsCurrent(CancellationToken.None)) throw new InvalidOperationException("Stale diagnostics");
        var diagnostics = diagnosticsResult.Diagnostics;
#endif
        featureTimes[2] = featureWatch.Elapsed.TotalMilliseconds;
        featureAllocations[2] = GC.GetTotalAllocatedBytes(precise: true) - featureAllocatedBefore;
        LastFeatureTimes = featureTimes;
        LastFeatureAllocations = featureAllocations;
        if (definitions.Count != 1 || references.Locations.Count < 2 || !references.IsComplete ||
            diagnostics.Count != 0 || statements.Count < 2)
            throw new InvalidOperationException($"Unexpected query output: defs={definitions.Count}; refs={references.Locations.Count}; complete={references.IsComplete}; diagnostics={diagnostics.Count}; stmts={statements.Count}; " + string.Join(" | ", diagnostics.Select(diagnostic => diagnostic.Message)));
        string RelativeLocation(Location location) => string.Join(":", Path.GetRelativePath(_fixture.Root,
            location.Uri.GetFileSystemPath()).Replace('\\', '/'), location.Range.Start.Line,
            location.Range.Start.Character, location.Range.End.Line, location.Range.End.Character);
        string output = string.Join("|", definitions.Select(RelativeLocation)) + ";" +
            string.Join("|", references.Locations.Select(RelativeLocation)) + ";" + references.IsComplete + ";" +
            statements.Count + ";" + diagnostics.Count;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output)));
    }
    public void Dispose()
    {
#if !BASELINE
        _definitions.Dispose(); _references.Dispose(); _diagnostics.Dispose(); _analysis.Dispose();
#endif
    }
    public void ClearCaches()
    {
        _diagnostics.Invalidate(_fixture.Path);
#if !BASELINE
        _analysis.InvalidateAll();
#endif
    }
    public async Task<object?> MeasureValidationAsync(int samples)
    {
#if BASELINE
        await Task.CompletedTask;
        return null;
#else
        using var lease = await _analysis.GetDocumentAsync(_fixture.Capture);
        if (lease is null) throw new InvalidOperationException("Missing validation capture");
        int offset = lease.Model.Document.Lines.ToOffset(
            (int)_fixture.Position.Line + 1, (int)_fixture.Position.Character + 1);
        string anchorPath = lease.Model.Bindings.FindSymbols(lease.Model.Document, offset)
            .SelectMany(symbol => symbol.Declarations).First().Document.Path;
        using var workspace = await _analysis.GetWorkspaceAsync(_fixture.Capture, anchorPath,
            lease, [_fixture.Root], CancellationToken.None);
        if (workspace is null) throw new InvalidOperationException("Missing workspace validation capture");
        object MeasureProof(AnalysisLease captured)
        {
            var durations = new List<double>();
            var allocations = new List<long>();
            for (int i = 0; i < samples; i++)
            {
                long before = GC.GetTotalAllocatedBytes(precise: true);
                var watch = Stopwatch.StartNew();
                if (!captured.Validation.IsCurrent()) throw new InvalidOperationException("Stale validation sample");
                watch.Stop();
                durations.Add(watch.Elapsed.TotalMilliseconds);
                allocations.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
            }
            return new
            {
                inputs = captured.Inputs.Count,
                observedInputEstimatedBytes = captured.Inputs.EstimatedBytes,
                medianMilliseconds = durations.Order().ElementAt(samples / 2),
                p90Milliseconds = durations.Order().ElementAt((int)Math.Ceiling(samples * .9) - 1),
                medianAllocatedBytes = allocations.Order().ElementAt(samples / 2),
            };
        }
        return new { document = MeasureProof(lease), workspace = MeasureProof(workspace) };
#endif
    }
}

sealed record Fixture(string Root, string Path, string Text, Position Position,
    DocumentRequestSnapshot Capture, int SourceFiles, int Projects, long SourceBytes)
{
    public static Fixture Create(string root, bool multi)
    {
        Directory.CreateDirectory(root);
        void Write(string path, string text)
        {
            string absolute = System.IO.Path.Combine(root, path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(absolute)!);
            if (!File.Exists(absolute) || File.ReadAllText(absolute) != text) File.WriteAllText(absolute, text);
        }
        const string options = "\"compilerOptions\":{\"noLib\":true,\"types\":[]}";
        string path;
        string text;
        if (!multi)
        {
            text = "export const original = 1;\nconst result: number = original;\n";
            path = System.IO.Path.Combine(root, "main.ts");
            Write("main.ts", text);
            Write("tsconfig.json", "{" + options + ",\"files\":[\"main.ts\"]}");
        }
        else
        {
            Write("tsconfig.json", "{" + options + ",\"files\":[],\"references\":[{\"path\":\"packages/lib\"},{\"path\":\"apps/one\"},{\"path\":\"apps/two\"}]}");
            Write("packages/lib/src/dependency.ts", "export const original = 1;\n");
            Write("packages/lib/tsconfig.json", "{" + options + ",\"include\":[\"src/**/*.ts\"]}");
            for (int app = 1; app <= 2; app++)
            {
                string directory = app == 1 ? "apps/one" : "apps/two";
                Write(directory + "/tsconfig.json", "{" + options + ",\"include\":[\"src/**/*.ts\"],\"references\":[{\"path\":\"../../packages/lib\"}]}");
                for (int file = 0; file < 12; file++)
                    Write(directory + $"/src/importer{file}.ts", "import { original as first } from '../../../packages/lib/src/dependency';\nconst result: number = first;\n" +
                        string.Join("\n", Enumerable.Range(0, 30).Select(index => $"export function compute{index}(value: number): number {{ return value + result; }}")) + "\n");
            }
            path = System.IO.Path.Combine(root, "apps/one/src/importer0.ts");
            text = File.ReadAllText(path);
        }
        // Match DocumentStore's canonical filesystem identities, including separator spelling.
        path = System.IO.Path.GetFullPath(path);
        string uri = new Uri(path).AbsoluteUri;
        var snapshot = new DocumentSnapshot(uri, text, 1, path);
        var capture = new DocumentRequestSnapshot(snapshot, 1,
            new Dictionary<string, DocumentSnapshot>(StringComparer.OrdinalIgnoreCase) { [path] = snapshot });
        string[] sources = Directory.GetFiles(root, "*.ts", SearchOption.AllDirectories);
        return new(root, path, text, new Position(1, "const result: number = ".Length + 1),
            capture, sources.Length, multi ? 4 : 1, sources.Sum(source => new FileInfo(source).Length));
    }
}
