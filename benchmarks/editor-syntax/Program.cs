using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpTS.Parsing;

const int samples = 31;
var reports = new List<object>();
foreach (bool multi in new[] { false, true })
{
    string[] sources = Enumerable.Range(0, multi ? 25 : 1).Select(CreateSource).ToArray();
    foreach (bool editor in new[] { false, true })
    {
#if BASELINE
        if (editor) continue;
#endif
        for (int index = 0; index < 30; index++) Parse(sources, editor);
        int repetitions = multi ? 3 : 40;
        var times = new List<double>();
        var allocations = new List<long>();
        List<Parsed> last = [];
        for (int sample = 0; sample < samples; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            for (int iteration = 0; iteration < repetitions; iteration++) last = Parse(sources, editor);
            watch.Stop();
            times.Add(watch.Elapsed.TotalMilliseconds / repetitions);
            allocations.Add((GC.GetAllocatedBytesForCurrentThread() - before) / repetitions);
        }
        var ast = last.SelectMany(parsed => parsed.Statements).ToArray();
        string fingerprint = Fingerprint(ast);
        long retained = Retained(sources, editor);
        times.Sort(); allocations.Sort();
        reports.Add(new
        {
            fixture = multi ? "25-module" : "small",
            editorCapture = editor,
            sourceFiles = sources.Length,
            sourceUtf16Bytes = sources.Sum(source => source.Length * sizeof(char)),
            repetitionsPerSample = repetitions,
            medianMilliseconds = times[samples / 2],
            p90Milliseconds = times[(int)Math.Ceiling(samples * .9) - 1],
            medianAllocatedBytes = allocations[samples / 2],
            retainedHeapDeltaBytes = retained,
            spanCount = last.Sum(parsed => parsed.Document.Spans.Count),
#if BASELINE
            syntaxRecords = 0,
            syntaxEstimatedBytes = 0L,
#else
            syntaxRecords = last.Sum(parsed => parsed.Document.EditorSyntax?.Count ?? 0),
            syntaxEstimatedBytes = last.Sum(parsed => parsed.Document.EditorSyntax?.EstimatedBytes ?? 0),
#endif
            resultFingerprint = fingerprint,
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
    samples,
    methodology = "Synchronous lexer + SourceDocument/line index + parser/normal lowerings per identical source sequence. JIT warmed; 31 batches; per-sequence latency/allocation divided by repetitions. No resolver, checker, I/O or cursor repair. Full public AST property graph fingerprint (tokens, type nodes, values and flags) computed outside timing, with no fields excluded. Retained heap is noisy full-GC live delta with final documents and AST roots alive; index estimate excludes base text/tokens/AST. Baseline archived without source instrumentation.",
    reports,
};
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(args[0]);

static List<Parsed> Parse(string[] sources, bool editor)
{
    var parsed = new List<Parsed>(sources.Length);
    for (int index = 0; index < sources.Length; index++)
    {
        var document = new SourceDocument($"fixture{index}.ts", sources[index], isVirtual: true);
        var parser = new Parser(new Lexer(document.Text).ScanTokens()).WithSourceDocument(document);
#if !BASELINE
        if (editor) parser.WithEditorSyntax();
#endif
        parsed.Add(new(document, parser.ParseOrThrow()));
    }
    return parsed;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static long Retained(string[] sources, bool editor)
{
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    long before = GC.GetTotalMemory(forceFullCollection: true);
    var held = Parse(sources, editor);
    long after = GC.GetTotalMemory(forceFullCollection: true);
    GC.KeepAlive(held);
    return after - before;
}

static string CreateSource(int index) => $$"""
    {{(index == 0 ? "" : $"import {{ Box as Previous }} from './fixture{index - 1}';")}}
    export interface Shape<T> { value: T; update(next: T): void; }
    export type Nested<T> = Array<Array<Array<T>>>;
    export type Choice<T> = T extends string ? { text: T } : { count: number };
    export class Box<T> implements Shape<T> {
        #serial: number = {{index}};
        value: T;
        constructor(value: T) { this.value = value; }
        update(next: T): void { this.value = next; this.#serial = this.#serial + 1; }
        map<U>(f: (value: T) => U): Box<U> { return new Box<U>(f(this.value)); }
    }
    export function run{{index}}(input: number, label: string = "😀"): number {
        const box = new Box<number>(input);
        const mapped = box.map<string>((value: number): string => `${label}:${value}`);
        const table = { result: mapped.value, [label]: input + 1 };
        if (input > 0) { box.update(input * 2); }
        return box.value + table[label] + (input ?? 0);
    }
    """;

static string Fingerprint(object root)
{
    var result = new StringBuilder();
    var visited = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
    Write(root);
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.ToString())));

    void Write(object? value)
    {
        if (value is null) { result.Append("null;"); return; }
        Type type = value.GetType();
        if (value is string text) { result.Append(text.Length).Append(':').Append(text).Append(';'); return; }
        if (type.IsPrimitive || type.IsEnum || value is decimal or System.Numerics.BigInteger)
        { result.Append(type.Name).Append(':').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(';'); return; }
        if (visited.TryGetValue(value, out int identity)) { result.Append('@').Append(identity).Append(';'); return; }
        visited[value] = visited.Count;
        result.Append(type.FullName).Append('{');
        if (value is IEnumerable sequence)
            foreach (object? element in sequence) Write(element);
        else
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.GetIndexParameters().Length == 0).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                result.Append(property.Name).Append('='); Write(property.GetValue(value));
            }
        result.Append('}');
    }
}

sealed record Parsed(SourceDocument Document, IReadOnlyList<Stmt> Statements);
