using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using SharpTS.Compilation;
using SharpTS.Parsing;

const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
string output = args.Length == 0 ? Path.Combine(Path.GetTempPath(), "sharpts-clr-acquisition") : Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var emitter = new RuntimeEmitter(TypeProvider.Runtime);
var owners = new HashSet<EmittedArrayOperationsRuntime>();
string compilerSha256 = Convert.ToHexString(SHA256.HashData(
    File.ReadAllBytes(typeof(RuntimeEmitter).Assembly.Location))).ToLowerInvariant();
var configurations = new (string Name, string Source, bool Hosted)[]
{
    ("minimal_first", "const n=1;", false),
    ("array", "const values=[1,2];", false),
    ("optional", "Buffer.from('x');new Uint8Array(2);new Set([1]);", false),
    ("mutation", "const a:any=[];a[Symbol.iterator]=null;", false),
    ("minimal_again", "const n=2;", false),
    ("hosted", "const n=3;", true)
};
foreach (var configuration in configurations)
{
    var builder = new PersistedAssemblyBuilder(new AssemblyName($"clr_acquisition_{Guid.NewGuid():N}"), typeof(object).Assembly);
    var statements = new Parser(new Lexer(configuration.Source).ScanTokens()).ParseOrThrow();
    var selectedEmitter = configuration.Hosted ? new RuntimeEmitter(TypeProvider.Runtime, emitHosted: true) : emitter;
    var runtime = selectedEmitter.EmitAll(builder.DefineDynamicModule("main"), new RuntimeFeatureDetector().Detect(statements));
    if (!owners.Add(runtime.ArrayOperations) || !runtime.ArrayOperations.IsComplete)
        throw new InvalidOperationException("Generated output reused an array-operation owner.");
    using var bytes = new MemoryStream();
    builder.Save(bytes);
    bytes.Position = 0;
    using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
    var errors = verifier.Verify(bytes);
    if (errors.Count != 0)
        throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    var image = bytes.ToArray();
    File.WriteAllBytes(Path.Combine(output, configuration.Name + ".dll"), image);
    var loaded = Assembly.Load(image);
    var type = loaded.GetType("$Runtime")!;
    var symbol = loaded.GetType(runtime.Symbols.Iterator.DeclaringType!.FullName!)!
        .GetField(runtime.Symbols.Iterator.Name, members)!.GetValue(null)!;
    var normalizer = type.GetMethod("ArrayDestructureSource")!;
    object[] Normalize(object value) => ((IList)normalizer.Invoke(null, [value, symbol, type])!).Cast<object>().ToArray();
    IEnumerable<object> NewSource() => Enumerable.Range(0, 3).Select(n => (object)(double)n);
    var source = NewSource();
    var observed = Normalize(source);
    var initialized = Normalize(NewSource().GetEnumerator());
    var active = NewSource().GetEnumerator();
    active.MoveNext();
    var remaining = Normalize(active);
    var exhausted = NewSource().GetEnumerator();
    while (exhausted.MoveNext()) { }
    var completed = Normalize(exhausted);
    var repeated = NewSource();
    var repeatedFirst = Normalize(repeated);
    var repeatedSecond = Normalize(repeated);
    var enumerableInput = new EnumerableInput(NewSource());
    var explicitEnumerableFirst = Normalize(enumerableInput);
    var explicitEnumerableSecond = Normalize(enumerableInput);
    var explicitActive = NewSource().GetEnumerator();
    explicitActive.MoveNext();
    var explicitActiveRemaining = Normalize(new EnumeratorInput(explicitActive));
    var explicitCompleted = NewSource().GetEnumerator();
    while (explicitCompleted.MoveNext()) { }
    var explicitCompletedRemaining = Normalize(new EnumeratorInput(explicitCompleted));
    var queue = Normalize(new Queue<object>([0d, 1d, 2d]));
    if (!initialized.SequenceEqual(new object[] { 0d, 1d, 2d }) ||
        !remaining.SequenceEqual(new object[] { 1d, 2d }) || completed.Length != 0 ||
        !explicitEnumerableFirst.SequenceEqual(new object[] { 0d, 1d, 2d }) ||
        !explicitEnumerableSecond.SequenceEqual(new object[] { 0d, 1d, 2d }) ||
        !explicitActiveRemaining.SequenceEqual(new object[] { 1d, 2d }) ||
        explicitCompletedRemaining.Length != 0 || enumerableInput.Acquisitions != 2 ||
        !queue.SequenceEqual(new object[] { 0d, 1d, 2d }))
        throw new InvalidOperationException("An independent enumerable/iterator control failed.");
    var references = loaded.GetReferencedAssemblies().Select(a => a.Name).ToArray();
    if (references.Contains("SharpTS") || references.Contains("SharpTS.Hosting.Abstractions") != configuration.Hosted)
        throw new InvalidOperationException("Generated output has incorrect deployment references.");
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Configuration = configuration.Name,
        CompilerSha256 = compilerSha256,
        Expected = new[] { 0d, 1d, 2d },
        Observed = observed,
        InitializedEnumerator = initialized,
        ActiveAfterOneExpected = new[] { 1d, 2d },
        ActiveAfterOne = remaining,
        ExhaustedEnumerator = completed,
        RepeatedEnumerableFirst = repeatedFirst,
        RepeatedEnumerableSecond = repeatedSecond,
        ExplicitEnumerableFirst = explicitEnumerableFirst,
        ExplicitEnumerableSecond = explicitEnumerableSecond,
        ExplicitEnumerableAcquisitions = enumerableInput.Acquisitions,
        ExplicitActiveEnumerator = explicitActiveRemaining,
        ExplicitCompletedEnumerator = explicitCompletedRemaining,
        OriginalMatchesExpected = observed.SequenceEqual(new object[] { 0d, 1d, 2d }),
        Queue = queue,
        ImplementsEnumerable = source is IEnumerable,
        ImplementsEnumerator = source is IEnumerator,
        VerifiedIL = true,
        Sha256 = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(),
        References = references
    }));
}

sealed class EnumerableInput(IEnumerable<object> source) : IEnumerable<object>
{
    public int Acquisitions { get; private set; }
    public IEnumerator<object> GetEnumerator()
    {
        Acquisitions++;
        return source.GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

sealed class EnumeratorInput(IEnumerator<object> source) : IEnumerator<object>
{
    public object Current => source.Current;
    object IEnumerator.Current => Current;
    public bool MoveNext() => source.MoveNext();
    public void Reset() => source.Reset();
    public void Dispose() => source.Dispose();
}
