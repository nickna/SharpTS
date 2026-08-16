using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Tests.Compilation;

/// <summary>
/// Validates that emitted runtime types (produced by RuntimeEmitter for standalone DLLs)
/// stay in sync with their corresponding runtime types (Runtime/Types/SharpTS*.cs).
///
/// Compiles a minimal TypeScript program to produce a standalone DLL, then uses reflection
/// to compare the emitted types against the actual runtime types. This catches the most
/// common failure mode: a method/constructor is added or changed on a runtime type but
/// the corresponding emission code in RuntimeEmitter is not updated.
/// </summary>
public class RuntimeTypeSyncTests : IClassFixture<RuntimeTypeSyncTests.CompiledAssemblyFixture>, IDisposable
{
    private readonly CompiledAssemblyFixture _fixture;
    private readonly ITestOutputHelper _output;

    public RuntimeTypeSyncTests(CompiledAssemblyFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public void Dispose() { }

    /// <summary>
    /// Compiles a trivial TypeScript program once to produce a DLL with all emitted runtime types.
    /// </summary>
    public class CompiledAssemblyFixture : IDisposable
    {
        public Assembly? CompiledAssembly { get; }
        public string? Error { get; }
        private readonly string _tempDir;

        public CompiledAssemblyFixture()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_sync_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);

            try
            {
                const string source = "const x: number = 1;";
                var dllPath = Path.Combine(_tempDir, "sync_test.dll");

                var lexer = new Lexer(source);
                var tokens = lexer.ScanTokens();
                var parser = new Parser(tokens);
                var statements = parser.ParseOrThrow();
                var checker = new TypeChecker();
                var typeMap = checker.Check(statements);
                var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
                var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

                var compiler = new ILCompiler("sync_test");
                // Sync test compiles a trivial program (`const x = 1;`) but asserts that
                // every $Date/$RegExp/$Buffer/etc. emitted type exists in the output. With
                // tree-shaking enabled, those types are skipped when the source doesn't use
                // them. Force-emit everything so the sync invariants still hold.
                compiler.SetRuntimeFeatures(RuntimeFeatureSet.EmitEverything());
                compiler.Compile(statements, typeMap, deadCodeInfo);
                compiler.Save(dllPath);

                var bytes = File.ReadAllBytes(dllPath);
                CompiledAssembly = Assembly.Load(bytes);
            }
            catch (Exception ex)
            {
                Error = ex.ToString();
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    // Base ignored methods common to all types (inherited from System.Object)
    private static readonly string[] BaseIgnored =
        ["Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Finalize"];

    /// <summary>
    /// Sync pairs: runtime type → emitted type name suffix → methods intentionally not emitted.
    /// The emitted type name is looked up by suffix match (e.g., "Date" matches "$Date").
    /// </summary>
    private static readonly SyncPair[] SyncPairs =
    [
        new(typeof(SharpTSDate), "Date",
            [.. BaseIgnored, "SetFromEpochMilliseconds", "ToLocalTime"]),

        new(typeof(SharpTSRegExp), "RegExp",
            [.. BaseIgnored,
             // Per-instance accessor map (interpreter-only path). Compiled $RegExp
             // routes defineProperty accessors through the central PropertyDescriptor
             // Store (PDS), so emitting equivalent instance methods would just be
             // dead code. See SharpTSRegExp.DefineAccessor / TryGetAccessor
             // (added in ad935b84 for ECMA-262 §22.2 configurable-accessor parity).
             "DefineAccessor", "TryGetAccessor"]),

        new(typeof(SharpTSError), "Error",
            [.. BaseIgnored, "GetMember"]),

        new(typeof(SharpTSArray), "Array",
            [.. BaseIgnored,
             // Internal helpers accessed via GetFieldsProperty dispatch, not direct calls
             "GetMember", "GetEnumerator", "get_RawLength", "set_RawLength",
             "DefineProperty", "GetNamedProperty", "GetOwnPropertyDescriptor",
             "HasNamedProperty", "PreventExtensions", "SetNamedProperty",
             "TryAdd", "TryAddStrict", "TryPop", "TryPopStrict",
             "TryReverse", "TryReverseStrict", "TryShift", "TryShiftStrict",
             "TryUnshift", "TryUnshiftStrict",
             // RuntimeValue overloads used only by interpreter, not emitted IL
             "GetRV",
             // Encapsulation API introduced for issue #73 Stage A (sparse-array migration).
             // These collapse direct .Elements access in the interpreter; compiled-mode
             // emission still operates on List<object?> backing, so $Array has no need
             // for them. Stage B added SetLength for `array.length = N`. Stage C added
             // GetRaw/HasIndex for hole-aware built-ins.
             "Add", "AddRange", "AddFirst", "Insert", "InsertRange",
             "RemoveLast", "RemoveFirst", "RemoveAt", "RemoveRange",
             "Clear", "ReverseInPlace", "GetRange",
             "PeekLast", "PeekFirst",
             "ContainsElement", "IndexOfElement",
             "SetLength", "GetRaw", "HasIndex", "DeleteAt"]),

        new(typeof(SharpTSObject), "Object",
            [.. BaseIgnored, "GetMember",
             // Symbol-based operations handled via separate dispatch
             "GetBySymbol", "SetBySymbol", "SetBySymbolStrict",
             "DeleteBySymbol", "DeleteBySymbolStrict",
             "HasSymbolProperty", "GetSymbolPropertyNames",
             // Object internals accessed via runtime helpers
             "DefineProperty", "GetOwnPropertyDescriptor", "GetPropertyFlags",
             "PreventExtensions",
             // RuntimeValue overloads used only by interpreter, not emitted IL
             "GetPropertyRV"]),

        new(typeof(SharpTSBuffer), "Buffer",
            [.. BaseIgnored, "GetMember"]),

        new(typeof(SharpTSEventEmitter), "EventEmitter",
            [.. BaseIgnored, "GetMember",
             // C#-specific optimization methods, not part of JS EventEmitter API.
             // EmitWith is interp-only: async built-ins emit lifecycle events on the
             // event-loop thread with a real interpreter (compiled $TSFunction listeners
             // need no interpreter, so the emitted $EventEmitter has no equivalent).
             "AddListenerDirect", "EmitDirect", "EmitWith", "RemoveListenerDirect"]),
    ];

    private record SyncPair(Type RuntimeType, string EmittedNameSuffix, string[] IgnoredMethods);

    /// <summary>
    /// Finds an emitted type by name suffix (e.g., "Date" finds "$Date" or "$TSDate").
    /// </summary>
    private Type? FindEmittedType(string nameSuffix)
    {
        if (_fixture.CompiledAssembly == null) return null;
        return _fixture.CompiledAssembly.GetTypes()
            .FirstOrDefault(t => t.Name.EndsWith(nameSuffix) && t.Name.StartsWith("$"));
    }

    [Fact]
    public void Fixture_CompiledSuccessfully()
    {
        Assert.Null(_fixture.Error);
        Assert.NotNull(_fixture.CompiledAssembly);
    }

    [Theory]
    [MemberData(nameof(GetSyncPairData))]
    public void EmittedType_Exists(string runtimeTypeName, string emittedNameSuffix)
    {
        var emittedType = FindEmittedType(emittedNameSuffix);
        Assert.NotNull(emittedType);
        _output.WriteLine($"Found emitted type for {runtimeTypeName}: {emittedType!.Name} ({emittedType.GetMethods().Length} methods)");
    }

    [Theory]
    [MemberData(nameof(GetSyncPairData))]
    public void EmittedType_HasAllPublicMethods(string runtimeTypeName, string emittedNameSuffix)
    {
        var pair = SyncPairs.First(p => p.RuntimeType.Name == runtimeTypeName);
        var runtimeType = pair.RuntimeType;
        var emittedType = FindEmittedType(emittedNameSuffix);
        if (emittedType == null) return;

        var ignored = new HashSet<string>(pair.IgnoredMethods);

        var runtimeMethods = runtimeType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !ignored.Contains(m.Name))
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var emittedMethods = emittedType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var missingMethods = runtimeMethods
            .Where(m => !emittedMethods.Contains(m))
            .ToList();

        _output.WriteLine($"{runtimeTypeName} → {emittedType.Name}:");
        _output.WriteLine($"  Runtime: {string.Join(", ", runtimeMethods)}");
        _output.WriteLine($"  Emitted: {string.Join(", ", emittedMethods)}");

        if (missingMethods.Count > 0)
        {
            _output.WriteLine($"  MISSING:");
            foreach (var m in missingMethods)
                _output.WriteLine($"    - {m}");
        }

        Assert.Empty(missingMethods);
    }

    [Theory]
    [MemberData(nameof(GetSyncPairData))]
    public void EmittedType_HasConstructors(string runtimeTypeName, string emittedNameSuffix)
    {
        var emittedType = FindEmittedType(emittedNameSuffix);
        Assert.NotNull(emittedType);

        var ctors = emittedType!.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        _output.WriteLine($"{runtimeTypeName} → {emittedType.Name}: {ctors.Length} public constructors");
        foreach (var ctor in ctors)
        {
            var sig = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            _output.WriteLine($"  ctor({sig})");
        }

        Assert.True(ctors.Length >= 1,
            $"Emitted type for {runtimeTypeName} ({emittedType.Name}) has no public constructors");
    }

    [Fact]
    public void EmittedErrorSubtypes_Exist()
    {
        if (_fixture.CompiledAssembly == null) return;

        var expectedSuffixes = new[] { "TypeError", "RangeError", "ReferenceError", "SyntaxError", "URIError", "EvalError" };
        foreach (var suffix in expectedSuffixes)
        {
            var errorType = FindEmittedType(suffix);
            Assert.NotNull(errorType);
            _output.WriteLine($"{errorType!.Name}: base = {errorType.BaseType?.Name}");
        }
    }

    [Fact]
    public void EmittedType_ErrorHasRequiredMembers()
    {
        var errorType = FindEmittedType("Error");
        Assert.NotNull(errorType);

        var allMembers = errorType!
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _output.WriteLine($"Error members: {string.Join(", ", allMembers.OrderBy(n => n))}");

        Assert.True(allMembers.Contains("message") || allMembers.Contains("Message"),
            "Emitted Error type missing 'message' member");
        Assert.True(allMembers.Contains("name") || allMembers.Contains("Name"),
            "Emitted Error type missing 'name' member");
        Assert.True(allMembers.Contains("stack") || allMembers.Contains("Stack"),
            "Emitted Error type missing 'stack' member");
    }

    [Fact]
    public void EmittedTypes_AllExpectedTypesPresent()
    {
        if (_fixture.CompiledAssembly == null) return;

        // List all emitted types (starting with $) for visibility
        var emittedTypes = _fixture.CompiledAssembly.GetTypes()
            .Where(t => t.Name.StartsWith("$"))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        _output.WriteLine($"Total emitted types: {emittedTypes.Count}");
        foreach (var t in emittedTypes)
            _output.WriteLine($"  {t}");

        // Minimum expected count — if this drops, something was accidentally removed
        Assert.True(emittedTypes.Count >= 20,
            $"Expected at least 20 emitted runtime types, found {emittedTypes.Count}");
    }

    /// <summary>
    /// Per-method shaking sync: under EmitEverything(), $Runtime must contain
    /// every method that any feature flag could need. Catches the case where a
    /// per-method gate accidentally drops a method from the everything-emit path.
    /// Add new methods here as Phase 5+ gates them.
    /// </summary>
    [Fact]
    public void EmittedRuntime_HasAllGatedMethods()
    {
        if (_fixture.CompiledAssembly == null) return;

        var runtime = _fixture.CompiledAssembly.GetType("$Runtime");
        Assert.NotNull(runtime);

        var actualMethods = runtime!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet();

        // Methods that Phase 5+ gates conditionally. Under EmitEverything()
        // (used by the fixture), every name listed here must exist.
        var gatedMethods = new[]
        {
            // Phase 5a: BigInt family
            "CreateBigInt", "BigIntAdd", "BigIntSubtract", "BigIntMultiply",
            "BigIntDivide", "BigIntRemainder", "BigIntPow",
            "BigIntLessThan", "BigIntLessThanOrEqual", "BigIntGreaterThan",
            "BigIntGreaterThanOrEqual", "BigIntEquals",
            "BigIntBitwiseAnd", "BigIntBitwiseOr", "BigIntBitwiseXor",
            "BigIntBitwiseNot", "BigIntLeftShift", "BigIntRightShift",
            // Phase 5e: Os family
            "OsFreemem", "OsLoadavg", "OsNetworkInterfaces",
            // Phase 5f: ChildProcess family
            "ChildProcessExec", "ChildProcessExecFile", "ChildProcessExecFileSync",
            "ChildProcessExecSync", "ChildProcessFork", "ChildProcessSpawn",
            "ChildProcessSpawnSync",
            // Phase 5g/h: Vm / Tty / Perf primitives
            "VmRunInNewContext", "VmRunInThisContext", "VmCompileFunction",
            "VmCreateContext", "VmIsContext", "VmNewScript", "VmGetScriptConstructor",
            "Tty_isatty",  // emitted method name uses underscore, not PascalCase
            "PerfPrimitiveNow",  // PerfPrimitiveStartTicks/TicksPerMs are fields, not methods
            // Phase 5i: AbortController / Proxy / DynamicImport / AsyncGenerator
            "CreateAbortController", "AbortControllerAbort", "AbortControllerGetSignal",
            "AbortSignalAbort", "AbortSignalAddEventListener", "AbortSignalAny",
            "AbortSignalGetAborted", "AbortSignalGetReason", "AbortSignalThrowIfAborted",
            "AbortSignalTimeout", "FireAbortEvent",
            "CreateProxy",
            "DynamicImportModule",
            "AsyncGeneratorAwaitContinue",
            // Phase 5j: WeakRef / WeakMap / WeakSet
            "CreateWeakRef", "WeakRefDeref",
            "WeakMapGet", "WeakMapSet", "WeakMapHas", "WeakMapDelete",
            "WeakSetAdd", "WeakSetHas", "WeakSetDelete",
        };

        var missing = gatedMethods.Where(m => !actualMethods.Contains(m)).ToList();
        if (missing.Count > 0)
        {
            _output.WriteLine($"$Runtime methods (sample): {string.Join(", ", actualMethods.OrderBy(n => n).Take(40))}");
            _output.WriteLine($"MISSING gated methods: {string.Join(", ", missing)}");
        }

        Assert.Empty(missing);
    }

    public static IEnumerable<object[]> GetSyncPairData()
    {
        return SyncPairs.Select(p => new object[] { p.RuntimeType.Name, p.EmittedNameSuffix });
    }
}
