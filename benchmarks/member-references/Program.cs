using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Services;

const int coldSamples = 11, warmSamples = 31;
string outputRoot = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputRoot);
var reports = new List<object>();
foreach (bool multi in new[] { false, true })
{
    Fixture fixture = Fixture.Create(Path.Combine(outputRoot, "fixtures", multi ? "multi-project" : "small"), multi);
    using (var prewarm = new Session(fixture))
        for (int i = 0; i < 3; i++) await prewarm.Query();
    var cold = new List<Sample>();
    for (int i = 0; i < coldSamples; i++)
    {
        using var session = new Session(fixture);
        cold.Add(await Measure(session));
    }
    var warm = new List<Sample>();
    AnalysisStatistics retained;
    using (var session = new Session(fixture))
    {
        await session.Query();
        for (int i = 0; i < warmSamples; i++) warm.Add(await Measure(session));
        retained = session.Statistics;
    }
    if (warm.Any(sample => sample.Checks != 0)) throw new InvalidOperationException("Unchanged warm query rechecked.");
    if (cold.Concat(warm).Select(sample => sample.Fingerprint).Distinct().Count() != 1)
        throw new InvalidOperationException("Cold/warm references differ.");
    reports.Add(new
    {
        name = multi ? "multi-project" : "small", fixture.SourceFiles, fixture.Projects,
        expectedReferencesIncludingDeclaration = fixture.ExpectedReferences,
        cold = Summarize(cold), warm = Summarize(warm),
        retainedSnapshots = retained.RetainedSnapshots, estimatedRetainedBytes = retained.EstimatedRetainedBytes,
    });
}
var report = new
{
    implementation = args.Length > 2 ? args[2] : "current", timestampUtc = DateTime.UtcNow,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    coldSamples, warmSamples,
    methodology = "Member references including declarations across initialized configured roots. Empty service cache per cold sample; unchanged capture/shared cache for warm samples. Three prewarm queries; JIT/OS file caches warm. noLib/types=[] fixtures, no CLR metadata provider or stdio. Full currentness validation included. Process-wide allocations across asynchronous workers; source-relative result fingerprints. Cache bytes are estimates, not retained heap or a process limit.",
    reports,
};
string output = Path.Combine(outputRoot, args[1]);
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);

static async Task<Sample> Measure(Session session)
{
    long checks = session.Statistics.Checks, before = GC.GetTotalAllocatedBytes(precise: true);
    var watch = Stopwatch.StartNew();
    var result = await session.Query();
    watch.Stop();
    long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
    var proof = session.Fingerprint(result);
    return new(watch.Elapsed.TotalMilliseconds, allocated,
        session.Statistics.Checks - checks, proof.Fingerprint, proof.PayloadBytes);
}

static object Summarize(List<Sample> samples) => new
{
    medianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(samples.Count / 2),
    p90Milliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt((int)Math.Ceiling(samples.Count * .9) - 1),
    medianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(samples.Count / 2),
    checksPerQuery = samples.Select(sample => sample.Checks).Distinct().Order().ToArray(),
    resultFingerprint = samples[0].Fingerprint, relativeLocationPayloadBytes = samples[0].PayloadBytes, samples,
};

record Sample(double Milliseconds, long AllocatedBytes, long Checks, string Fingerprint, int PayloadBytes);

sealed class Session : IDisposable
{
    readonly Fixture _fixture;
    readonly SemanticAnalysisService _analysis;
    readonly ReferenceService _references;
    public Session(Fixture fixture)
    {
        _fixture = fixture;
        var workspace = new NavigationWorkspaceContext();
        workspace.Initialize(new InitializeParams { RootUri = new Uri(fixture.Root).AbsoluteUri });
        _analysis = new(workspace);
        _references = new(_analysis);
    }
    public AnalysisStatistics Statistics => _analysis.Statistics;
    public async Task<NavigationReferenceResult> Query()
    {
        var result = await _references.FindReferenceResultAsync(_fixture.Capture, _fixture.Position,
            includeDeclaration: true, workspaceRoots: [_fixture.Root], cancellationToken: CancellationToken.None);
        if (!result.IsCurrent() || !result.IsComplete || result.IsRenameEligible ||
            result.Locations.Count != _fixture.ExpectedReferences)
            throw new InvalidOperationException($"Unexpected member references: count={result.Locations.Count}, complete={result.IsComplete}, rename={result.IsRenameEligible}.");
        return result;
    }
    public (string Fingerprint, int PayloadBytes) Fingerprint(NavigationReferenceResult result)
    {
        var relative = result.Locations.Select(location => new
        {
            path = Path.GetRelativePath(_fixture.Root, location.Uri.GetFileSystemPath()).Replace('\\', '/'),
            location.Range.Start.Line, location.Range.Start.Character,
            endLine = location.Range.End.Line, endCharacter = location.Range.End.Character,
        }).ToArray();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(relative);
        return (Convert.ToHexString(SHA256.HashData(payload)), payload.Length);
    }
    public void Dispose() { _references.Dispose(); _analysis.Dispose(); }
}

sealed record Fixture(string Root, DocumentRequestSnapshot Capture, Position Position,
    int SourceFiles, int Projects, int ExpectedReferences)
{
    public static Fixture Create(string root, bool multi)
    {
        void Write(string relative, string source)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path) || File.ReadAllText(path) != source) File.WriteAllText(path, source);
        }
        const string options = "\"compilerOptions\":{\"noLib\":true,\"types\":[]}";
        string path, text;
        if (!multi)
        {
            text = "class Box { value = 1; }\nconst box = new Box();\nconst first = box.value;\nconst second = box.value;\n";
            path = Path.Combine(root, "main.ts");
            Write("main.ts", text);
            Write("tsconfig.json", "{" + options + ",\"files\":[\"main.ts\"]}");
        }
        else
        {
            Write("tsconfig.json", "{" + options + ",\"files\":[],\"references\":[{\"path\":\"packages/lib\"},{\"path\":\"apps/one\"},{\"path\":\"apps/two\"}]}");
            Write("packages/lib/src/box.ts", "export class Box { value = 1; }\n");
            Write("packages/lib/tsconfig.json", "{" + options + ",\"include\":[\"src/**/*.ts\"]}");
            foreach (string app in new[] { "one", "two" })
            {
                Write($"apps/{app}/tsconfig.json", "{" + options + ",\"include\":[\"src/**/*.ts\"],\"references\":[{\"path\":\"../../packages/lib\"}]}");
                for (int file = 0; file < 12; file++)
                    Write($"apps/{app}/src/importer{file}.ts", "import { Box } from '../../../packages/lib/src/box';\nconst box = new Box();\nconst result = box.value;\n" +
                        string.Join("\n", Enumerable.Range(0, 30).Select(index => $"export function compute{index}(value: number): number {{ return value + result; }}")) + "\n");
            }
            path = Path.Combine(root, "apps/one/src/importer0.ts");
            text = File.ReadAllText(path);
        }
        path = Path.GetFullPath(path);
        var store = new DocumentStore();
        string uri = new Uri(path).AbsoluteUri;
        if (!store.Open(uri, text, 1) || !store.TryCapture(uri, out var capture))
            throw new InvalidOperationException("Cannot capture benchmark document.");
        if (Directory.GetFiles(root, "*.ts", SearchOption.AllDirectories).Length != (multi ? 25 : 1) ||
            Directory.GetFiles(root, "tsconfig.json", SearchOption.AllDirectories).Length != (multi ? 4 : 1))
            throw new InvalidOperationException("Fixture directory has unexpected source/config files; use a fresh OutputDirectory.");
        return new(root, capture, new Position(2, (multi ? "const result = box." : "const first = box.").Length + 1),
            multi ? 25 : 1, multi ? 4 : 1, multi ? 25 : 3);
    }
}
