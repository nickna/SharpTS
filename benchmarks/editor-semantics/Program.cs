using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpTS.Diagnostics;
using SharpTS.Configuration;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using TypeInfo = SharpTS.TypeSystem.TypeInfo;

const int sampleCount = 31;
VerifyNominalFingerprint();
var reports = new List<object>();
foreach (bool multi in new[] { false, true })
{
    foreach (CheckMode mode in Enum.GetValues<CheckMode>())
    {
#if BASELINE
        if (mode != CheckMode.Ordinary) continue;
#endif
        int repetitions = multi ? 2 : 12;
        Console.WriteLine($"Starting {(multi ? "25 modules" : "small")}: {mode}");
        for (int index = 0; index < 30; index++) Check(Prepare(multi), mode);
        var times = new List<double>();
        var allocations = new List<long>();
        Graph? lastGraph = null;
        Published? last = null;
        for (int sample = 0; sample < sampleCount; sample++)
        {
            // The checker updates modules and AST-adjacent state. Every timed check receives a
            // fresh editor-enabled parsed graph, with all setup excluded from the counters.
            Graph[] graphs = Enumerable.Range(0, repetitions).Select(_ => Prepare(multi)).ToArray();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            foreach (Graph graph in graphs) last = Check(graph, mode);
            times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds / repetitions);
            allocations.Add((GC.GetAllocatedBytesForCurrentThread() - before) / repetitions);
            lastGraph = graphs[^1];
            GC.KeepAlive(graphs);
        }
        times.Sort(); allocations.Sort();
#if !BASELINE
        if (mode == CheckMode.Editor &&
            (last!.Editor.Occurrences.Count == 0 || last.Editor.Declarations.Count == 0 || last.Editor.Scopes.Count == 0 ||
             !last.Editor.Invocations.Any(fact => fact.Status == EditorInvocationStatus.Selected) ||
             !last.Editor.Invocations.Any(fact => fact.Candidates.Count > 1) ||
             !last.Editor.Invocations.Any(fact => fact.Candidates.Any(candidate => candidate.Instantiated is not null)) ||
             lastGraph!.QueryExpressions.Sum(probe => last.Editor.GetReceiverMembers(probe.Document, probe.Owner).Members.Count) == 0 ||
             lastGraph.QueryScopes.Any(site => last.Editor.GetVisibleBindings(site.Document, site.Offset).Count == 0)))
            throw new InvalidOperationException("Editor fixtures must exercise types, declarations, scopes, receiver members, overloads and instantiated selections.");
#endif
        var fingerprints = Fingerprints(lastGraph!, last!);
        reports.Add(new
        {
            fixture = multi ? "25-module-scopes-overloads-generics" : "small",
            mode = mode.ToString(),
            sourceFiles = lastGraph!.Modules.Count,
            sourceUtf16Bytes = lastGraph.Modules.Sum(module => module.Document!.Text.Length * sizeof(char)),
            repetitionsPerSample = repetitions,
            timedChecks = sampleCount * repetitions,
            medianMilliseconds = times[sampleCount / 2],
            p90Milliseconds = times[(int)Math.Ceiling(sampleCount * .9) - 1],
            medianAllocatedBytes = allocations[sampleCount / 2],
            retainedPublishedHeapDeltaBytes = Retained(multi, mode),
            diagnosticCount = last!.Diagnostics.Count,
            typedExpressions = lastGraph.Expressions.Count(expr => last.Types.Get(expr) is not null),
            lexicalTokenCount = lastGraph.Tokens.Count,
            memberSymbols = last.Members.Symbols.Count,
            memberOccurrences = last.Members.Occurrences.Count,
            memberEstimatedBytes = last.Members.EstimatedBytes,
#if !BASELINE
            editorFacts = last.Editor.Count,
            editorOccurrences = last.Editor.Occurrences.Count,
            editorDeclarations = last.Editor.Declarations.Count,
            editorScopes = last.Editor.Scopes.Count,
            editorInvocations = last.Editor.Invocations.Count,
            selectedInvocations = last.Editor.Invocations.Count(fact => fact.Status == EditorInvocationStatus.Selected),
            candidateOnlyInvocations = last.Editor.Invocations.Count(fact => fact.Status == EditorInvocationStatus.CandidatesOnly),
            editorEstimatedBytes = last.Editor.EstimatedBytes,
            receiverMembersAtQuerySites = lastGraph.QueryExpressions.Sum(probe => last.Editor.GetReceiverMembers(probe.Document, probe.Owner).Members.Count),
            visibleBindingsAtQuerySites = lastGraph.QueryScopes.Sum(site => last.Editor.GetVisibleBindings(site.Document, site.Offset).Count),
            queryReadBatch = mode == CheckMode.Editor ? MeasureQueries(lastGraph, last) : null,
#endif
            fingerprints,
        });
    }
}
var report = new
{
    implementation = args[1],
    timestampUtc = DateTime.UtcNow,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    processorCount = Environment.ProcessorCount,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
    samples = sampleCount,
    methodology = "Fresh pure-VFS editor-enabled parsed module graph for each check; parse/resolution/setup excluded. Timed: checker construction, one CheckModules, frozen lexical/member/editor publication and diagnostics copy. Ordinary/member/editor variants add only their requested capture. 30 warmups, 31 full-GC-isolated batches. Fingerprints outside timing: diagnostics, public structural expression/class types (nominal class IDs normalized to per-graph ordinals, retaining identity partitions), lexical declaration/use identity partitions. Retention: full-GC live heap delta from an already parsed graph to a checked publication with checker released. A separate query-read batch measures immutable lookups only, without checking, resolution, or new capture. Baseline archived without instrumentation; no disk I/O in timed checks.",
    reports,
};
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(args[0]);

static Graph Prepare(bool multi)
{
    string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "SharpTS-Editor-Semantics-Benchmark"));
    int count = multi ? 25 : 1;
    var sources = Enumerable.Range(0, count).ToDictionary(
        index => Path.Combine(folder, $"module{index}.ts"),
        index => multi ? MultiSource(index) : SmallSource(), StringComparer.OrdinalIgnoreCase);
    string entryPath = Path.Combine(folder, $"module{count - 1}.ts");
    var resolver = new ModuleResolver(entryPath, ModuleResolutionOptions.Default, sources,
        new TypeScriptProgramOptions { NoLib = true, Types = [] }) { CaptureEditorSyntax = true };
    var modules = resolver.GetModulesInOrder(resolver.LoadProgram(entryPath));
    var expressions = modules.SelectMany(module => module.Document!.EditorSyntax!.Records)
        .Select(record => record.Node).OfType<Expr>().Distinct<Expr>(ReferenceEqualityComparer.Instance).ToArray();
    var tokens = modules.SelectMany(module => module.Tokens.Select(token => (module.Document!, token)))
        .Where(pair => pair.token.Start >= 0 && pair.token.End > pair.token.Start).ToArray();
    var queryExpressions = modules.SelectMany(module => module.Document!.EditorSyntax!.Records
        .Where(record => record.Node is Expr.Get or Expr.GetPrivate or Expr.Call or Expr.New)
        .DistinctBy(record => record.Node, ReferenceEqualityComparer.Instance).Take(4)
        .Select(record => new QueryProbe(module.Document!, record.Node switch
        {
            Expr.Get access => access.Object,
            Expr.GetPrivate access => access.Object,
            _ => (Expr)record.Node,
        }, record.Span.Start))).ToArray();
    var queryInvocations = modules.SelectMany(module => module.Document!.EditorSyntax!.Invocations.Take(4)
        .Select(invocation => new QueryProbe(module.Document!, invocation.Owner, invocation.OpenParen.End))).ToArray();
    var queryScopes = modules.Select(module => (module.Document!,
        module.Document!.Text.IndexOf("/*scope*/", StringComparison.Ordinal) + "/*scope*/".Length)).ToArray();
    if (queryScopes.Any(site => site.Item2 < "/*scope*/".Length))
        throw new InvalidOperationException("Every fixture module needs a scope query marker.");
    return new(resolver, modules, expressions, tokens, queryExpressions, queryInvocations, queryScopes);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static Published Check(Graph graph, CheckMode mode)
{
    var checker = new TypeChecker();
#if !BASELINE
    if (mode == CheckMode.Members) checker.WithMemberProvenance();
    if (mode == CheckMode.Editor) checker.WithEditorMetadata();
#endif
    TypeMap types = checker.CheckModules(graph.Modules, graph.Resolver);
    var bindings = checker.Bindings.Freeze();
    var diagnostics = checker.GetDiagnostics().ToArray();
    if (diagnostics.Length != 0)
        throw new InvalidOperationException("Benchmark fixtures must remain checker-valid: " +
            string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Message)));
#if BASELINE
    return new(types, bindings, diagnostics, checker.Members.Freeze());
#else
    return new(types, bindings, diagnostics, checker.Members.Freeze(), checker.EditorFacts.Freeze());
#endif
}

[MethodImpl(MethodImplOptions.NoInlining)]
static long Retained(bool multi, CheckMode mode)
{
    Graph graph = Prepare(multi);
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long before = GC.GetTotalMemory(forceFullCollection: true);
    Published held = Check(graph, mode);
    long after = GC.GetTotalMemory(forceFullCollection: true);
    GC.KeepAlive(graph); GC.KeepAlive(held);
    return after - before;
}

#if !BASELINE
static QueryReadReport MeasureQueries(Graph graph, Published published)
{
    const int querySamples = 31;
    const int repetitions = 10;
    for (int warmup = 0; warmup < 30; warmup++) ReadQueryBatch(graph, published);
    var times = new List<double>();
    var allocations = new List<long>();
    long checksum = 0;
    for (int sample = 0; sample < querySamples; sample++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int repetition = 0; repetition < repetitions; repetition++) checksum += ReadQueryBatch(graph, published);
        times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds / repetitions);
        allocations.Add((GC.GetAllocatedBytesForCurrentThread() - before) / repetitions);
    }
    times.Sort(); allocations.Sort();
    GC.KeepAlive(graph); GC.KeepAlive(published);
    return new(graph.QueryExpressions.Count * 3 + graph.QueryInvocations.Count * 2 + graph.QueryScopes.Count,
        querySamples * repetitions, times[querySamples / 2], times[(int)Math.Ceiling(querySamples * .9) - 1],
        allocations[querySamples / 2], checksum);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static long ReadQueryBatch(Graph graph, Published published)
{
    long checksum = 0;
    foreach (QueryProbe probe in graph.QueryExpressions)
    {
        checksum += published.Editor.GetOccurrence(probe.Document, probe.Owner)?.Type.Text.Length ?? 0;
        checksum += published.Editor.FindOccurrence(probe.Document, probe.Offset)?.Type.Text.Length ?? 0;
        checksum += published.Editor.GetReceiverMembers(probe.Document, probe.Owner).Members.Count;
    }
    foreach (QueryProbe probe in graph.QueryInvocations)
    {
        checksum += published.Editor.GetInvocation(probe.Document, probe.Owner)?.Candidates.Count ?? 0;
        checksum += published.Editor.FindInvocation(probe.Document, probe.Offset)?.SelectedOrdinal ?? 0;
    }
    foreach ((SourceDocument document, int offset) in graph.QueryScopes)
        checksum += published.Editor.GetVisibleBindings(document, offset).Count;
    return checksum;
}
#endif

static object Fingerprints(Graph graph, Published published)
{
    var diagnosticText = new StringBuilder();
    foreach (Diagnostic diagnostic in published.Diagnostics.OrderBy(item => item.FilePath).ThenBy(item => item.Line)
        .ThenBy(item => item.Column).ThenBy(item => item.Code).ThenBy(item => item.Message))
        diagnosticText.Append(Path.GetFileName(diagnostic.FilePath)).Append(':').Append(diagnostic.Line)
            .Append(':').Append(diagnostic.Column).Append(':').Append(diagnostic.Severity).Append(':')
            .Append(diagnostic.Code).Append(':').Append(diagnostic.TsCode).Append(':').Append(diagnostic.Message).AppendLine();

    var types = new List<object?>();
    foreach (ParsedModule module in graph.Modules)
    {
        types.Add(module.ModuleName);
        foreach (EditorSyntaxRecord syntax in module.Document!.EditorSyntax!.Records
            .Where(record => record.Node is Expr).DistinctBy(record => record.Node, ReferenceEqualityComparer.Instance))
        {
            types.Add(syntax.Span.Start); types.Add(syntax.Span.End); types.Add(syntax.Node.GetType().Name);
            types.Add(published.Types.Get((Expr)syntax.Node));
        }
    }
    types.Add(published.Types.ClassTypes);

    var lexical = new StringBuilder();
    foreach ((SourceDocument document, Token token) in graph.Tokens)
    {
        var symbols = published.Bindings.FindSymbols(document, token.Start)
            .OrderBy(symbol => symbol.Namespace).ThenBy(symbol => symbol.Name).ToArray();
        if (symbols.Length == 0) continue;
        lexical.Append(Path.GetFileName(document.Path)).Append(':').Append(token.Start).Append(':').Append(token.End);
        foreach (FrozenBindingSymbol symbol in symbols)
        {
            lexical.Append('|').Append(symbol.Namespace).Append(':').Append(symbol.Name).Append(" declarations[");
            foreach (BindingDeclaration declaration in symbol.Declarations.OrderBy(item => item.Document.Path).ThenBy(item => item.Name.Start))
                lexical.Append(Path.GetFileName(declaration.Document.Path)).Append(':').Append(declaration.Name.Start).Append(',');
            lexical.Append("] uses[");
            foreach (BindingOccurrence occurrence in published.Bindings.FindReferences([symbol], true))
                lexical.Append(Path.GetFileName(occurrence.Document.Path)).Append(':').Append(occurrence.Name.Start)
                    .Append(':').Append(occurrence.IsDeclaration).Append(',');
            lexical.Append(']');
        }
        lexical.AppendLine();
    }
    return new { diagnostics = Hash(diagnosticText.ToString()), types = StructuralHash(types), lexical = Hash(lexical.ToString()) };
}

static string StructuralHash(object root)
{
    var text = new StringBuilder();
    var visited = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
    var nominalClasses = new Dictionary<int, int>();
    Write(root);
    return Hash(text.ToString());
    void Write(object? value)
    {
        if (value is null) { text.Append("null;"); return; }
        Type type = value.GetType();
        if (value is string word) { text.Append(word.Length).Append(':').Append(word).Append(';'); return; }
        if (type.IsPrimitive || type.IsEnum || value is decimal or System.Numerics.BigInteger)
        { text.Append(type.Name).Append(':').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';'); return; }
        if (visited.TryGetValue(value, out int identity)) { text.Append('@').Append(identity).Append(';'); return; }
        visited[value] = visited.Count;
        // Concrete frozen dictionary implementation names/bucket order are runtime details.
        bool dictionary = type.GetInterfaces().Any(item => item.IsGenericType &&
            item.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));
        text.Append(dictionary ? "dictionary" : type.FullName).Append('{');
        if (value is IEnumerable sequence)
        {
            object?[] items = sequence.Cast<object?>().ToArray();
            if (dictionary) items = items.OrderBy(item => item!.GetType().GetProperty("Key")!.GetValue(item)?.ToString(), StringComparer.Ordinal).ToArray();
            else if (type.GetInterfaces().Any(item => item.IsGenericType && item.GetGenericTypeDefinition() == typeof(ISet<>)))
                items = items.OrderBy(item => item?.ToString(), StringComparer.Ordinal).ToArray();
            foreach (object? item in items) Write(item);
        }
        else
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0)
                .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                text.Append(property.Name).Append('=');
                if ((property.Name == "DeclarationId" && value is ClassMetadataCore or TypeInfo.MutableClass) ||
                    (property.Name == "DeclaringClassId" && value is MemberAccessBrand))
                {
                    int nominalId = (int)property.GetValue(value)!;
                    // Global numeric IDs differ between fresh checks/processes. Keep zero as
                    // locationless and preserve every nonzero identity's equivalence partition.
                    if (nominalId == 0) text.Append("class:0;");
                    else
                    {
                        if (!nominalClasses.TryGetValue(nominalId, out int ordinal))
                            nominalClasses.Add(nominalId, ordinal = nominalClasses.Count + 1);
                        text.Append("class:").Append(ordinal).Append(';');
                    }
                }
                else Write(property.GetValue(value));
            }
        text.Append('}');
    }
}

static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

static void VerifyNominalFingerprint()
{
    object[] distinct = [new TypeInfo.MutableClass("Same", 101), new TypeInfo.MutableClass("Same", 102),
        new MemberAccessBrand(AccessModifier.Private, 101), new MemberAccessBrand(AccessModifier.Protected, 102)];
    object[] remapped = [new TypeInfo.MutableClass("Same", 901), new TypeInfo.MutableClass("Same", 902),
        new MemberAccessBrand(AccessModifier.Private, 901), new MemberAccessBrand(AccessModifier.Protected, 902)];
    object[] merged = [new TypeInfo.MutableClass("Same", 901), new TypeInfo.MutableClass("Same", 901),
        new MemberAccessBrand(AccessModifier.Private, 901), new MemberAccessBrand(AccessModifier.Protected, 901)];
    object[] locationless = [new TypeInfo.MutableClass("Same", 0), new TypeInfo.MutableClass("Same", 902),
        new MemberAccessBrand(AccessModifier.Private, 0), new MemberAccessBrand(AccessModifier.Protected, 902)];
    object[] wrongBrands = [new TypeInfo.MutableClass("Same", 901), new TypeInfo.MutableClass("Same", 902),
        new MemberAccessBrand(AccessModifier.Private, 902), new MemberAccessBrand(AccessModifier.Protected, 901)];
    string expected = StructuralHash(distinct);
    if (expected != StructuralHash(remapped) || expected == StructuralHash(merged) ||
        expected == StructuralHash(locationless) || expected == StructuralHash(wrongBrands))
        throw new InvalidOperationException("Nominal type fingerprint must preserve identity partitions and locationless zero.");
}

static string SmallSource() => """
    export class Counter {
        value: number = 1;
        #secret: number = 2;
        static total: number = 0;
        get doubled(): number { return this.value * 2; }
        set doubled(next: number) { this.value = next / 2; }
        increment(delta: number): number { this.value = this.value + delta; return this.#secret + this.value; }
        has(other: Counter): boolean { return #secret in other; }
        #read(): number { return this.#secret; }
        readSecret(): number { return this.#read(); }
    }
    function identity<T>(value: T): T { return value; }
    function select(value: number): number;
    function select(value: string): number;
    function select(value: any): number { return 1; }
    const counter = new Counter();
    counter.value = counter.increment(3);
    counter.doubled = counter["value"];
    const result: number = counter.doubled + Counter.total;
    function scoped(input: number): number {
        let value: number = identity<number>(input);
        { const value: string = 'inner'; /*scope*/select(value); }
        return select(value) + counter.readSecret();
    }
    const checked: number = scoped(result);
    """;

static string MultiSource(int index) => $$"""
    {{(index == 0 ? "" : $"import {{ Box{index - 1} as Previous }} from './module{index - 1}';")}}
    export class Base{{index}} {
        value: number = {{index}};
        #secret: number = {{index + 1}};
        static total: number = 0;
        get doubled(): number { return this.value * 2; }
        set doubled(next: number) { this.value = next / 2; }
        increment(delta: number): number { this.value = this.value + delta; return this.#secret + this.value; }
        has(other: Base{{index}}): boolean { return #secret in other; }
        #read(): number { return this.#secret; }
        readSecret(): number { return this.#read(); }
    }
    export class Derived{{index}} extends Base{{index}} {
        own: number = 2;
        sum(): number { return this.increment(this.own) + this.value; }
    }
    export class Box{{index}}<T> {
        value: T;
        constructor(value: T) { this.value = value; }
        read(): T { return this.value; }
        update(next: T): void { this.value = next; }
    }
    export function identity{{index}}<T>(value: T): T { return value; }
    export function select{{index}}(value: number): number;
    export function select{{index}}(value: string): number;
    export function select{{index}}(value: any): number { return 1; }
    export function run{{index}}(): number {
        const derived = new Derived{{index}}();
        derived.value = derived.increment(3);
        derived.doubled = derived["value"];
        const box = new Box{{index}}<number>(derived.sum());
        box.update(box.read() + Base{{index}}.total);
        {{(index == 0 ? "" : "const previous = new Previous<number>(1); box.update(previous.read());")}}
        let selected: number = identity{{index}}<number>(select{{index}}(box.value));
        { const selected: string = 'inner'; /*scope*/select{{index}}(selected); }
        return selected + derived.doubled + derived.readSecret();
    }
    """;

sealed record Graph(ModuleResolver Resolver, List<ParsedModule> Modules, IReadOnlyList<Expr> Expressions,
    IReadOnlyList<(SourceDocument Document, Token Token)> Tokens, IReadOnlyList<QueryProbe> QueryExpressions,
    IReadOnlyList<QueryProbe> QueryInvocations, IReadOnlyList<(SourceDocument Document, int Offset)> QueryScopes);
sealed record QueryProbe(SourceDocument Document, Expr Owner, int Offset);
sealed record Published(TypeMap Types, FrozenBindingIndex Bindings, IReadOnlyList<Diagnostic> Diagnostics,
    FrozenMemberIndex Members
#if !BASELINE
    , FrozenEditorSemanticIndex Editor
#endif
);
enum CheckMode { Ordinary, Members, Editor }
#if !BASELINE
sealed record QueryReadReport(int QueriesPerBatch, int TimedBatches, double MedianMilliseconds,
    double P90Milliseconds, long MedianAllocatedBytes, long Checksum);
#endif
