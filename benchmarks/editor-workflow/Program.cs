using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer;
using SharpTS.LanguageServer.Project;
using SharpTS.LanguageServer.Services;
using SharpTS.Parsing;

const int coldSamples = 11, warmSamples = 31;
string outputRoot = Path.GetFullPath(args[0]);
Directory.CreateDirectory(outputRoot);
var reports = new List<object>();
foreach (string name in new[] { "small", "multi-project", "default-library" })
{
    Fixture fixture = Fixture.Create(Path.Combine(outputRoot, "fixtures", name), name);
    using (var prewarm = new Session(fixture))
        for (int i = 0; i < 3; i++) await prewarm.Sequence();
    var cold = new List<Sample>();
    var construction = new List<(double Milliseconds, long AllocatedBytes)>();
    int count = name == "default-library" ? 3 : coldSamples;
    for (int i = 0; i < count; i++)
    {
        long allocated = GC.GetTotalAllocatedBytes(true);
        var constructing = Stopwatch.StartNew();
        using var session = new Session(fixture);
        constructing.Stop();
        construction.Add((constructing.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(true) - allocated));
        cold.Add(await Measure(session, session.Sequence));
    }
    var warm = new List<Sample>();
    var edits = new List<Sample>();
    object memory;
    AnalysisStatistics retained;
    int documentCount;
    using (var session = new Session(fixture))
    {
        long emptyHeap = CollectHeap();
        await session.Sequence();
        for (int i = 0; i < (name == "default-library" ? 7 : warmSamples); i++)
            warm.Add(await Measure(session, session.Sequence));
        retained = session.Analysis.Statistics;
        documentCount = session.CountDocuments();
        long activeHeap = CollectHeap();
        session.Analysis.InvalidateAll();
        long clearedHeap = CollectHeap();
        Require(session.Analysis.Statistics.RetainedSnapshots == 0, "InvalidateAll retained completed graphs");
        memory = new { emptySessionManagedHeap = emptyHeap, activeCacheManagedHeap = activeHeap,
            afterInvalidationManagedHeap = clearedHeap, activeMinusEmptyBytes = activeHeap - emptyHeap,
            activeMinusInvalidatedBytes = activeHeap - clearedHeap };
        for (int i = 0; i < 7; i++)
        {
            session.SetMain(fixture.Text + $"\n// open version {i}\n");
            edits.Add(await Measure(session, session.Sequence));
        }
    }
    Require(warm.All(sample => sample.Checks == 0), $"{name}: unchanged warm sequence rechecked");
    Require(cold.Concat(warm).Select(sample => sample.Fingerprint).Distinct().Count() == 1,
        $"{name}: cold/warm result fingerprint changed");
    object? dependency = null;
    if (fixture.Dependency is not null)
    {
        using var session = new Session(fixture);
        await session.Sequence();
        var changed = new List<Sample>();
        for (int i = 0; i < 7; i++)
        {
            session.Store.Set(new Uri(fixture.Dependency).AbsoluteUri,
                Fixture.BoxSource.Replace("value = 1", $"value = {i + 2}", StringComparison.Ordinal));
            changed.Add(await Measure(session, session.Sequence));
        }
        dependency = Summary(changed);
    }
    reports.Add(new { name, fixture.SourceFiles, fixture.Projects, fixture.ExpectedReferences,
        freshServiceConstruction = new {
            medianMilliseconds = construction.Select(sample => sample.Milliseconds).Order().ElementAt(construction.Count / 2),
            p90Milliseconds = construction.Select(sample => sample.Milliseconds).Order().ElementAt((int)Math.Ceiling(construction.Count * .9) - 1),
            medianAllocatedBytes = construction.Select(sample => sample.AllocatedBytes).Order().ElementAt(construction.Count / 2) },
        sourceDocumentsInEntrySnapshot = documentCount, cold = Summary(cold), warm = Summary(warm),
        changedOpenVersion = Summary(edits), changedOpenDependency = dependency,
        retained.RetainedSnapshots, retained.EstimatedRetainedBytes, memory });
}
Fixture recoveryFixture = Fixture.Create(Path.Combine(outputRoot, "fixtures", "recovery"), "small");
object recovery;
using (var session = new Session(recoveryFixture))
{
    var kinds = new List<object>();
    foreach (bool signature in new[] { false, true })
    {
        string suffix = signature ? "\ncombine(1,            " : "\nbox.            ";
        session.SetMain(recoveryFixture.Text + suffix);
        int start = (recoveryFixture.Text + suffix).Length - 12;
        var first = await Measure(session, () => session.Recovery(start, signature));
        var same = new List<Sample>();
        for (int i = 0; i < 15; i++) same.Add(await Measure(session, () => session.Recovery(start, signature)));
        Require(same.All(sample => sample.Checks == 0), "same recovery caret rechecked");
        Require(same.All(sample => sample.Fingerprint == first.Fingerprint), "same recovery caret changed its result");
        var changed = new List<Sample>();
        for (int i = 1; i <= 8; i++)
            changed.Add(await Measure(session, () => session.Recovery(start + i, signature)));
        Require(changed.All(sample => sample.Checks > 0), "changed recovery caret unexpectedly reused another caret's graph");
        var changedVersion = new List<Sample>();
        for (int i = 0; i < 7; i++)
        {
            string edited = recoveryFixture.Text + $"\n// recovery edit {i}\n" + suffix;
            session.SetMain(edited);
            changedVersion.Add(await Measure(session, () => session.Recovery(edited.Length - 12, signature)));
        }
        Require(changedVersion.All(sample => sample.Checks > 0), "changed recovery version reused old semantics");
        var cache = Inspect(session.Analysis);
        Require(cache.CursorEntries <= 4 && cache.RetainedEntries <= 8, "cursor retention exceeded defaults");
        kinds.Add(new { kind = signature ? "signature-help" : "member-completion", first,
            unchangedCaret = Summary(same), changedCaret = Summary(changed), changedVersion = Summary(changedVersion), cache });
    }
    recovery = kinds;
}
object burst = await RunBurst(Fixture.Create(Path.Combine(outputRoot, "fixtures", "burst"), "small"));
object metadata = await RunMetadata(Fixture.Create(Path.Combine(outputRoot, "fixtures", "metadata"), "small"));
object retention = RunRetention(Fixture.Create(Path.Combine(outputRoot, "fixtures", "retention"), "small"));
var report = new { timestampUtc = DateTime.UtcNow, sourceCommit = args.Length > 2 ? args[2] : "unspecified",
    binaries = new { languageServerSha256 = HashFile(typeof(SemanticAnalysisService).Assembly.Location),
        compilerSha256 = HashFile(typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location) },
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    coldSamples, warmSamples,
    methodology = "Current-version actual service sequence. Cold means empty service cache in a JIT/OS-cache-warmed process; warm means identical captured overlays and caret. All query currentness checks are included. Allocations are process-wide across async workers. Payload serialization/fingerprinting follows the measured window. Managed heap is a full-GC observation, not a cache/process limit. No prior equivalent for new features; earlier exact-baseline reports remain separate.",
    reports, recovery, burst, metadata, retention };
string output = Path.Combine(outputRoot, args.Length > 1 ? args[1] : "results.json");
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(output);

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static long CollectHeap() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
static async Task<Sample> Measure(Session session, Func<Task<object>> query)
{
    long checks = session.Analysis.Statistics.Checks, builds = session.Analysis.Statistics.Builds;
    long allocated = GC.GetTotalAllocatedBytes(true);
    var watch = Stopwatch.StartNew();
    object response = await query();
    watch.Stop();
    long allocationDelta = GC.GetTotalAllocatedBytes(true) - allocated;
    byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response).Replace(session.Fixture.Root.Replace('\\', '/'), "<fixture>", StringComparison.OrdinalIgnoreCase)
        .Replace(session.Fixture.Root.Replace("\\", "\\\\"), "<fixture>", StringComparison.OrdinalIgnoreCase));
    return new(watch.Elapsed.TotalMilliseconds, allocationDelta, session.Analysis.Statistics.Checks - checks,
        session.Analysis.Statistics.Builds - builds, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
}
static object Summary(List<Sample> samples) => new {
    sampleCount = samples.Count,
    medianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(samples.Count / 2),
    p90Milliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt((int)Math.Ceiling(samples.Count * .9) - 1),
    medianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(samples.Count / 2),
    checks = samples.Select(sample => sample.Checks).Distinct().Order().ToArray(),
    builds = samples.Select(sample => sample.Builds).Distinct().Order().ToArray(),
    fingerprints = samples.Select(sample => sample.Fingerprint).Distinct().ToArray(),
    payloadBytes = samples.Select(sample => sample.PayloadBytes).Distinct().Order().ToArray(), samples };

// Runner-only inspection under the actual service gate; no public protocol/counter is added.
static CacheObservation Inspect(SemanticAnalysisService analysis)
{
    object Read(string name) => typeof(SemanticAnalysisService).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(analysis)!;
    lock (Read("_gate"))
    {
        var inflight = (IDictionary)Read("_inflight");
        var lru = (IEnumerable)Read("_lru");
        int cursor = 0;
        foreach (object entry in lru)
        {
            var keys = (IEnumerable)entry.GetType().GetProperty("Keys")!.GetValue(entry)!;
            if (keys.Cast<object>().Any(key => key.GetType().GetProperty("CursorTarget")!.GetValue(key) is not null)) cursor++;
        }
        return new(inflight.Count, ((SemaphoreSlim)Read("_admission")).CurrentCount,
            ((SemaphoreSlim)Read("_buildSlots")).CurrentCount, analysis.Statistics.RetainedSnapshots,
            cursor, analysis.Statistics.EstimatedRetainedBytes);
    }
}

static async Task<object> RunBurst(Fixture fixture)
{
    using var session = new Session(fixture);
    using var release = new ManualResetEventSlim(false);
    int entered = 0;
    session.Analysis.BeforeCheck = () => { Interlocked.Increment(ref entered); release.Wait(TimeSpan.FromSeconds(20)); };
    var sources = Enumerable.Range(0, 24).Select(_ => new CancellationTokenSource()).ToArray();
    var tasks = new List<Task<bool>>();
    try
    {
    for (int i = 0; i < sources.Length; i++)
    {
        session.SetMain(fixture.Text + $"\n// obsolete {i}\n");
        DocumentRequestSnapshot capture = session.Capture;
        CancellationToken token = sources[i].Token;
        tasks.Add(Wait());
        async Task<bool> Wait()
        {
            try { using var lease = await session.Analysis.GetDocumentAsync(capture, token); return false; }
            catch (OperationCanceledException) { return true; }
        }
    }
    var deadline = Stopwatch.StartNew();
    CacheObservation blocked;
    do { blocked = Inspect(session.Analysis); if (blocked.Inflight == 16 && Volatile.Read(ref entered) == 2) break; await Task.Delay(5); }
    while (deadline.Elapsed < TimeSpan.FromSeconds(15));
    Require(blocked.Inflight == 16 && entered == 2, "burst did not reach the configured build/admission bounds");
    foreach (var source in sources) source.Cancel();
    var settling = Stopwatch.StartNew();
    bool[] settled = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));
    settling.Stop();
    Require(settled.All(value => value), "gated obsolete requests did not cancel");
    session.Analysis.BeforeCheck = null;
    release.Set();
    session.SetMain(fixture.Text + "\n// final fresh request\n");
    Sample fresh = await Measure(session, session.Sequence);
    while (deadline.Elapsed < TimeSpan.FromSeconds(30))
    {
        var observed = Inspect(session.Analysis);
        if (observed.Inflight == 0 && observed.AdmissionAvailable == 16 && observed.BuildSlotsAvailable == 2) break;
        await Task.Delay(5);
    }
    CacheObservation completed = Inspect(session.Analysis);
    Require(completed.Inflight == 0 && completed.AdmissionAvailable == 16 && completed.BuildSlotsAvailable == 2,
        "burst did not release admission/build slots");
    Require(completed.RetainedEntries <= 8 && completed.CursorEntries <= 4, "burst retained too many completed entries");
    return new { requests = tasks.Count, canceledWaiters = settled.Count(value => value), blocked,
        cancellationSettlementMilliseconds = settling.Elapsed.TotalMilliseconds,
        checkedBuildsEnteringGate = entered, fresh, completed, statistics = session.Analysis.Statistics };
    }
    finally
    {
        session.Analysis.BeforeCheck = null;
        release.Set();
        foreach (var source in sources) { source.Cancel(); source.Dispose(); }
    }
}

[MethodImpl(MethodImplOptions.NoInlining)]
static object RunRetention(Fixture fixture)
{
    using var session = new Session(fixture);
    long empty = CollectHeap();
    Fill();
    CacheObservation cached = Inspect(session.Analysis);
    Require(cached.RetainedEntries <= 8 && cached.CursorEntries <= 4 && cached.EstimatedBytes <= 64 * 1024 * 1024,
        "multiple-document recovery exceeded completed-cache bounds");
    long active = CollectHeap();
    session.Analysis.InvalidateAll();
    long invalidated = CollectHeap();
    Require(session.Analysis.Statistics.RetainedSnapshots == 0, "retention invalidation did not empty cache");
    return new { documents = 12, cursorRequests = 24, cached, emptyManagedHeap = empty,
        activeManagedHeap = active, invalidatedManagedHeap = invalidated, releasedManagedHeapObservation = active - invalidated };

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Fill()
    {
        for (int document = 0; document < 12; document++)
        {
            string path = Path.Combine(fixture.Root, $"document{document}", "entry.ts");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "tsconfig.json"),
                "{\"compilerOptions\":{\"noLib\":true,\"types\":[]},\"files\":[\"entry.ts\"]}");
            const string text = "class Box { value = 1; } const box = new Box(); box.  ";
            File.WriteAllText(path, text);
            string uri = new Uri(path).AbsoluteUri;
            session.Store.Open(uri, text, 1);
            if (!session.Store.TryCapture(uri, out var capture)) throw new InvalidOperationException();
            using var seed = session.Analysis.GetDocumentAsync(capture).GetAwaiter().GetResult();
            for (int caret = text.Length - 2; caret < text.Length; caret++)
            {
                using var cursor = session.Analysis.GetCursorDocumentAsync(capture, caret, EditorQueryKind.Completion,
                    seed, CancellationToken.None).GetAwaiter().GetResult();
                Require(cursor is not null, "retention cursor build unavailable");
            }
        }
    }
}

static async Task<object> RunMetadata(Fixture fixture)
{
    string reference = Path.Combine(fixture.Root, "reference.dll");
    string first = typeof(SharpTS.TypeSystem.BindingIndex).Assembly.Location;
    string second = typeof(SemanticAnalysisService).Assembly.Location;
    File.Copy(first, reference, overwrite: true);
    using var provider = new AnalysisMetadataProvider(references: [reference], startDirectory: fixture.Root);
    using var session = new Session(fixture, provider);
    await session.Sequence();
    var unchanged = new List<Sample>();
    for (int i = 0; i < 7; i++) unchanged.Add(await Measure(session, session.Sequence));
    int originalGeneration = provider.RefreshGeneration();
    var refreshed = new List<Sample>();
    var generations = new List<int>();
    for (int i = 0; i < 7; i++)
    {
        File.Copy(i % 2 == 0 ? second : first, reference, overwrite: true);
        refreshed.Add(await Measure(session, session.Sequence));
        generations.Add(provider.RefreshGeneration());
    }
    Require(unchanged.All(sample => sample.Checks == 0), "unchanged CLR generation rechecked");
    Require(refreshed.All(sample => sample.Checks > 0), "changed CLR generation reused old checking");
    Require(unchanged.Concat(refreshed).Select(sample => sample.Fingerprint).Distinct().Count() == 1,
        "unrelated CLR reference replacement changed ordinary query output");
    Require(generations.Distinct().Count() == 7 && generations[0] != originalGeneration, "CLR replacement did not refresh generations");
    return new { referenceBytes = new FileInfo(reference).Length, originalGeneration, generations,
        unchangedGeneration = Summary(unchanged), replacedAssembly = Summary(refreshed),
        note = "Real custom CLR reference bytes alternate between core and LS assemblies; BCL remains available. Full metadata capture/hash/currentness cost is included, no restore or executable loading." };
}

record Sample(double Milliseconds, long AllocatedBytes, long Checks, long Builds, string Fingerprint, int PayloadBytes);
record CacheObservation(int Inflight, int AdmissionAvailable, int BuildSlotsAvailable, int RetainedEntries, int CursorEntries, long EstimatedBytes);

sealed class Session : IDisposable
{
    public Fixture Fixture { get; }
    public DocumentStore Store { get; } = new();
    public SemanticAnalysisService Analysis { get; }
    readonly SemanticHoverService _hover;
    readonly SemanticCompletionService _completion;
    readonly SemanticSignatureHelpService _signature;
    readonly DefinitionService _definitions;
    readonly ReferenceService _references;
    readonly RenameService _rename;
    readonly PrivateRenameService _privateRename;
    public Session(Fixture fixture, AnalysisMetadataProvider? metadata = null)
    {
        Fixture = fixture;
        var workspace = new NavigationWorkspaceContext();
        workspace.Initialize(new InitializeParams { RootUri = new Uri(fixture.Root).AbsoluteUri });
        Analysis = new(workspace, metadata);
        _hover = new(Analysis, metadata is null ? null : new MemberHoverService(metadata.Resolve));
        _completion = new(Analysis); _signature = new(Analysis); _definitions = new(Analysis);
        _references = new(Analysis); _rename = new(_references); _privateRename = new(Analysis);
        Store.Open(new Uri(fixture.Path).AbsoluteUri, fixture.Text, 1);
    }
    public DocumentRequestSnapshot Capture
    {
        get { if (!Store.TryCapture(new Uri(Fixture.Path).AbsoluteUri, out var capture)) throw new InvalidOperationException(); return capture; }
    }
    public void SetMain(string text) => Store.Set(new Uri(Fixture.Path).AbsoluteUri, text);
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CountDocuments()
    {
        using var lease = Analysis.GetDocumentAsync(Capture).GetAwaiter().GetResult();
        return lease?.Model.Snapshot.Documents.Count ?? 0;
    }
    public async Task<object> Sequence()
    {
        DocumentRequestSnapshot capture = Capture;
        var hover = await _hover.HoverAsync(capture, At(capture, "/*member*/", 1), MarkupKind.Markdown, default);
        var completion = await _completion.CompletionAsync(capture, At(capture, "/*member*/", 2), default);
        var signatures = await _signature.SignatureHelpAsync(capture, At(capture, "/*signature*/"), true, true, default);
        var definitions = await _definitions.FindDefinitionsAsync(capture, At(capture, "/*member*/", 1), default);
        var references = await _references.FindReferenceResultAsync(capture, At(capture, "/*member*/", 1), true, [Fixture.Root]);
        var lexical = await _rename.PrepareAsync(capture, At(capture, "/*lexical*/", 1), [Fixture.Root], default);
        var privateResult = await _privateRename.PrepareAsync(capture, At(capture, "/*private*/", 2), default);
        if (hover.Hover?.Contents.MarkupContent?.Value != "```typescript\nvalue: any\n```" || !hover.IsCurrent() || !completion.List.Items.Any(item => item.Label == "value") ||
            !completion.IsCurrent() || signatures.Help is null || !signatures.IsCurrent() || definitions.Locations.Count != 1 ||
            !definitions.IsCurrent() || references.Locations.Count != Fixture.ExpectedReferences || !references.IsComplete ||
            !references.IsCurrent() || lexical.Value is null || !lexical.Domain.IsCurrent() || privateResult.Value is null || !privateResult.IsCurrent())
            throw new InvalidOperationException($"Workflow unavailable: hover={hover.Hover is not null}, completion={completion.List.Items.Count()}, sig={signatures.Help is not null}, defs={definitions.Locations.Count}, refs={references.Locations.Count}, lexical={lexical.Value is not null}, private={privateResult.Value is not null}.");
        return new { hover = new { text = hover.Hover.Contents.MarkupContent?.Value, hover.Hover.Range },
            completion = CompletionPayload(completion.List), signatures = SignaturePayload(signatures.Help),
            definitions = Relative(definitions.Locations), references = Relative(references.Locations),
            lexical = lexical.Value, privatePrepare = new { privateResult.Value.PlaceholderRange?.Range,
                privateResult.Value.PlaceholderRange?.Placeholder } };
    }
    object[] Relative(IReadOnlyList<Location> locations) => locations.Select(location => (object)new {
        path = Path.GetRelativePath(Fixture.Root, location.Uri.GetFileSystemPath()).Replace('\\', '/'), location.Range }).ToArray();
    public async Task<object> Recovery(int offset, bool signature)
    {
        DocumentRequestSnapshot capture = Capture;
        var lines = new LineIndex(capture.Document.Text);
        var (line, column) = lines.ToPosition(offset);
        var position = new Position(line - 1, column - 1);
        if (signature)
        {
            var result = await _signature.SignatureHelpAsync(capture, position, true, true, default);
            if (result.Help is null || !result.IsCurrent()) throw new InvalidOperationException("Signature recovery unavailable");
            return SignaturePayload(result.Help);
        }
        else
        {
            var result = await _completion.CompletionAsync(capture, position, default);
            if (!result.List.Items.Any(item => item.Label == "value") || !result.IsCurrent()) throw new InvalidOperationException("Member recovery unavailable");
            return CompletionPayload(result.List);
        }
    }
    static object CompletionPayload(CompletionList list) => new { list.IsIncomplete,
        items = list.Items.Select(item => new { item.Label, item.Kind, item.Detail,
            range = item.TextEdit?.TextEdit?.Range, text = item.TextEdit?.TextEdit?.NewText }).ToArray() };
    static object SignaturePayload(SignatureHelp help) => new { help.ActiveSignature, help.ActiveParameter,
        signatures = help.Signatures.Select(signature => new { signature.Label, signature.ActiveParameter }).ToArray() };
    static Position At(DocumentRequestSnapshot capture, string marker, int delta = 0)
    {
        int offset = capture.Document.Text.IndexOf(marker, StringComparison.Ordinal) + marker.Length + delta;
        var (line, column) = new LineIndex(capture.Document.Text).ToPosition(offset);
        return new(line - 1, column - 1);
    }
    public void Dispose() { _definitions.Dispose(); _references.Dispose(); Analysis.Dispose(); }
}

sealed record Fixture(string Root, string Path, string Text, string? Dependency, int SourceFiles, int Projects, int ExpectedReferences)
{
    public const string BoxSource = "export class Box { value = 1; }\n";
    public static Fixture Create(string root, string name)
    {
        bool multi = name == "multi-project", libraries = name == "default-library";
        void Write(string relative, string source)
        {
            string path = System.IO.Path.Combine(root, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, source);
        }
        string options = libraries ? "\"compilerOptions\":{\"types\":[]}" : "\"compilerOptions\":{\"noLib\":true,\"types\":[]}";
        string header = multi ? "import { Box } from '../../../packages/lib/src/box';\n" : BoxSource;
        string text = header + "class Secret { #serial = 1; read() { return this./*private*/#serial; } }\n" +
            "function combine(left: number, right: string): number { return left; }\n" +
            "const box = new Box();\nconst local: number = 1;\nconst result = box./*member*/value;\n" +
            "combine(/*signature*/local, 'text');\n/*lexical*/local;\n" +
            (libraries ? "const libraryValues: Array<number> = [1, 2];\nconst time = new Date();\n" : "");
        string path, dependency;
        if (multi)
        {
            Write("tsconfig.json", "{" + options + ",\"files\":[],\"references\":[{\"path\":\"packages/lib\"},{\"path\":\"apps/one\"},{\"path\":\"apps/two\"}]}");
            Write("packages/lib/src/box.ts", BoxSource);
            Write("packages/lib/tsconfig.json", "{" + options + ",\"include\":[\"src/**/*.ts\"]}");
            foreach (string app in new[] { "one", "two" })
            {
                Write($"apps/{app}/tsconfig.json", "{" + options + ",\"include\":[\"src/**/*.ts\"],\"references\":[{\"path\":\"../../packages/lib\"}]}");
                for (int file = 0; file < 12; file++)
                    Write($"apps/{app}/src/importer{file}.ts", app == "one" && file == 0 ? text :
                        "import { Box } from '../../../packages/lib/src/box';\nconst box = new Box();\nconst result = box.value;\n" +
                        string.Join("\n", Enumerable.Range(0, 30).Select(index => $"export function compute{index}(value: number): number {{ return value + result; }}")) + "\n");
            }
            path = System.IO.Path.Combine(root, "apps/one/src/importer0.ts");
            dependency = System.IO.Path.Combine(root, "packages/lib/src/box.ts");
        }
        else
        {
            Write("main.ts", text); Write("tsconfig.json", "{" + options + ",\"files\":[\"main.ts\"]}");
            path = System.IO.Path.Combine(root, "main.ts"); dependency = "";
        }
        return new(root, System.IO.Path.GetFullPath(path), text, multi ? dependency : null, multi ? 25 : 1, multi ? 4 : 1, multi ? 25 : 2);
    }
}
