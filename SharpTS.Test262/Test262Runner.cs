using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.Test262;

/// <summary>
/// Per-phase Stopwatch counters for diagnostic compiled-mode profiling.
/// Always-on (the Stopwatch.GetTimestamp pair is ~5ns × 7 phases = 35ns per
/// test, vs ~580ms per test in the real workload). Reset before a measurement
/// run, then read at end. Thread-safe via <see cref="Interlocked.Add(ref long, long)"/>.
/// </summary>
public static class CompiledPhaseStats
{
    public static long LexParseTicks;
    public static long TypeCheckTicks;
    public static long DeadCodeTicks;
    public static long ILCompileTicks;
    public static long SaveBytesTicks;
    public static long AlcLoadTicks;
    public static long AlcCtorTicks;
    public static long AlcLoadFromStreamTicks;
    public static long AlcReflectionTicks;
    public static long InvokeTicks;
    public static long UnloadTicks;
    public static long PeriodicGcTicks;
    public static long Count;

    public static void Reset()
    {
        Interlocked.Exchange(ref LexParseTicks, 0);
        Interlocked.Exchange(ref TypeCheckTicks, 0);
        Interlocked.Exchange(ref DeadCodeTicks, 0);
        Interlocked.Exchange(ref ILCompileTicks, 0);
        Interlocked.Exchange(ref SaveBytesTicks, 0);
        Interlocked.Exchange(ref AlcLoadTicks, 0);
        Interlocked.Exchange(ref AlcCtorTicks, 0);
        Interlocked.Exchange(ref AlcLoadFromStreamTicks, 0);
        Interlocked.Exchange(ref AlcReflectionTicks, 0);
        Interlocked.Exchange(ref InvokeTicks, 0);
        Interlocked.Exchange(ref UnloadTicks, 0);
        Interlocked.Exchange(ref PeriodicGcTicks, 0);
        Interlocked.Exchange(ref Count, 0);
    }

    public static double Ms(long ticks) =>
        (double)ticks * 1000.0 / Stopwatch.Frequency;
}

/// <summary>Execution mode for a Test262 run.</summary>
public enum Test262ExecutionMode
{
    Interpreted,
    Compiled,
}

/// <summary>
/// Runs a single Test262 file end-to-end and classifies the outcome.
///
/// Each compiled test loads into its own collectible <see cref="AssemblyLoadContext"/>
/// and unloads after the test runs. Without that, dynamic assemblies accumulate in
/// the default ALC and the testhost OOMs at scale (was 28 GB on 8.7k-test runs
/// before the switch). See issue #109.
/// </summary>
public sealed class Test262Runner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    // Memory watchdog: if an individual test drives process memory growth
    // past this threshold we abort it. Catches runaway allocation paths we
    // haven't explicitly guarded (Array.prototype methods, large string
    // concat, etc.). Chosen to be well above any legitimate test's working
    // set but well below the point where the OS kills the host.
    //
    // Note: this only catches tests whose allocations grow incrementally —
    // single-statement pathological allocators (e.g., `new Array(2**29)`)
    // complete in microseconds, well below the 100ms polling interval, and
    // bypass this entirely. Those need the per-test-isolation work tracked
    // in issue #109 to bound their impact.
    private const long MemoryLimitBytes = 2L * 1024 * 1024 * 1024;

    // How often to force a full GC pass to let unloaded ALCs actually free
    // their memory. Collectible ALCs need GC cycles after Unload() to
    // release; doing it after every test would be wasteful, doing it never
    // means we'd see staircase growth between forced collections. 50 is
    // small enough to keep the staircase low and large enough to amortize
    // the GC cost.
    private const int GcEveryNTests = 50;

    // AsyncLocal-scoped Console.Out / Console.Error redirection. Replaces the prior
    // pattern of `lock(ConsoleLock) { Console.SetOut(sw); … Console.SetOut(orig); }`
    // around every test execution. That pattern had two costs:
    //   1. Console.SetOut wraps in TextWriter.Synchronized — every JS console.log
    //      then takes a process-global lock, capping parallelism even within a worker.
    //   2. The wrap allocation + push/pop happens twice per test × ~10K tests.
    // The AsyncLocal redirector installs a proxy on Console.Out exactly once per
    // process, then per-test scopes are pure AsyncLocal slot writes — no allocation,
    // no global lock.
    private static readonly AsyncLocal<TextWriter?> _outOverride = new();
    private static readonly AsyncLocal<TextWriter?> _errOverride = new();
    private static int _consoleProxyInstalled;
    private static readonly object _consoleProxyLock = new();

    private readonly string _test262Root;
    private readonly Test262HarnessAssembler _assembler;
    private readonly TimeSpan _timeout;
    private readonly HashSet<string> _skipFeatures;

    // Counts compiled tests since the last forced GC. See GcEveryNTests.
    private int _compiledTestsSinceGc;

    /// <summary>
    /// When true, <see cref="RunCompiled"/> loads each emitted assembly into
    /// the default (non-collectible) ALC via <see cref="Assembly.Load(byte[])"/>.
    /// Bypasses the collectibility load-time tax (~25% wall on the Math/200
    /// benchmark; LoadFromStream -15%, Invoke/JIT -71%) but leaks the assembly
    /// for the lifetime of the process — only safe in a process that restarts
    /// before its working set exceeds a budget, or that runs only a small,
    /// bounded set of tests in-process.
    /// <para>
    /// It is also the only <em>stable</em> in-process mode: the collectible
    /// path (false) loads + JIT-compiles + <c>Unload()</c>s a fresh ALC per
    /// test, and that churn — under Server + Concurrent GC and the periodic
    /// forced <c>GC.Collect()</c> — intermittently crashes the test host with
    /// a fatal CLR error (<c>0x80131506</c>) inside the JIT/collectible-ALC
    /// teardown path. See issue #964 and the .NET unloadability docs. The
    /// persistent worker (<c>SharpTS.Test262.Worker</c>) and the in-process
    /// SmokeTest diagnostics therefore set this to true; the in-process
    /// baseline fallback (whole-subset, ~11k tests) leaves it off and relies
    /// on collectible unload to avoid OOM (issue #109).
    /// </para>
    /// <para>
    /// Per-instance (not a process-global static) so concurrently-running
    /// xUnit collections — e.g. SmokeTest (non-collectible) and the baseline
    /// in-process fallback (collectible) — can't clobber each other's mode.
    /// </para>
    /// </summary>
    public bool UseNonCollectibleLoad { get; }

    public Test262Runner(
        string test262Root,
        TimeSpan? timeout = null,
        HashSet<string>? skipFeatures = null,
        bool useNonCollectibleLoad = false)
    {
        _test262Root = test262Root;
        _assembler = new Test262HarnessAssembler(test262Root);
        _timeout = timeout ?? DefaultTimeout;
        _skipFeatures = skipFeatures ?? new HashSet<string>(StringComparer.Ordinal);
        UseNonCollectibleLoad = useNonCollectibleLoad;
        EnsureConsoleProxyInstalled();
    }

    private static IDisposable RedirectConsoleOut(TextWriter writer)
    {
        var prior = _outOverride.Value;
        _outOverride.Value = writer;
        return new ConsoleScope(_outOverride, prior);
    }

    private static IDisposable RedirectConsoleError(TextWriter writer)
    {
        var prior = _errOverride.Value;
        _errOverride.Value = writer;
        return new ConsoleScope(_errOverride, prior);
    }

    /// <summary>
    /// Installs Console.Out/Error proxies once per process. Writes go through the
    /// proxy, which reads the AsyncLocal slot to find the per-context destination.
    /// We bypass <see cref="Console.SetOut"/> by reflecting directly into
    /// <c>System.Console.s_out</c>/<c>s_error</c> — <see cref="Console.SetOut"/>
    /// would wrap our proxy in <see cref="TextWriter.Synchronized"/> and re-introduce
    /// a process-global lock on every <see cref="Console.WriteLine"/>, which is
    /// the exact thing this redirector exists to avoid.
    /// </summary>
    private static void EnsureConsoleProxyInstalled()
    {
        if (Volatile.Read(ref _consoleProxyInstalled) == 1) return;
        lock (_consoleProxyLock)
        {
            if (_consoleProxyInstalled == 1) return;

            var outProxy = new ProxyWriter(Console.Out, _outOverride);
            var errProxy = new ProxyWriter(TextWriter.Null, _errOverride);

            var ok = TrySetConsoleField("s_out", outProxy)
                  && TrySetConsoleField("s_error", errProxy);
            if (!ok)
            {
                // Older runtime — fall back to the synchronized SetOut path.
                // Tests still work, just slightly slower.
                Console.SetOut(outProxy);
                Console.SetError(errProxy);
            }
            Volatile.Write(ref _consoleProxyInstalled, 1);
        }
    }

    private static bool TrySetConsoleField(string fieldName, object proxy)
    {
        var field = typeof(Console).GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Static);
        if (field is null) return false;
        field.SetValue(null, proxy);
        return true;
    }

    private sealed class ConsoleScope : IDisposable
    {
        private readonly AsyncLocal<TextWriter?> _slot;
        private readonly TextWriter? _prior;
        private bool _disposed;
        public ConsoleScope(AsyncLocal<TextWriter?> slot, TextWriter? prior)
        {
            _slot = slot;
            _prior = prior;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _slot.Value = _prior;
        }
    }

    private sealed class ProxyWriter : TextWriter
    {
        private readonly TextWriter _fallback;
        private readonly AsyncLocal<TextWriter?> _slot;
        public ProxyWriter(TextWriter fallback, AsyncLocal<TextWriter?> slot)
        {
            _fallback = fallback;
            _slot = slot;
        }
        public override System.Text.Encoding Encoding => _fallback.Encoding;
        private TextWriter Target => _slot.Value ?? _fallback;
        public override void Write(char value) => Target.Write(value);
        public override void Write(string? value) { if (value is not null) Target.Write(value); }
        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);
        public override void WriteLine() => Target.WriteLine();
        public override void WriteLine(string? value) => Target.WriteLine(value);
        public override void Flush() => Target.Flush();
    }

    /// <summary>
    /// Runs one Test262 file and returns the classified outcome. Does not
    /// throw — all failure modes map to <see cref="Test262Outcome"/> buckets.
    /// </summary>
    public Test262Result RunOne(string testFilePath, Test262ExecutionMode mode)
    {
        string body;
        try
        {
            body = File.ReadAllText(testFilePath);
        }
        catch (Exception ex)
        {
            return new Test262Result(Test262Outcome.HarnessError, $"Failed to read test file: {ex.Message}", null);
        }

        var metadata = Test262MetadataParser.Parse(body);

        // Milestone 1 defers negative tests wholesale — their outcome protocol
        // (parse-phase vs runtime-phase expected errors) is a follow-up.
        if (metadata.IsNegative)
            return new Test262Result(Test262Outcome.Skipped, null, "negative-test-deferred");

        if (metadata.IsModule)
            return new Test262Result(Test262Outcome.Skipped, null, "module-test-deferred");

        // Feature-gated skip so known-unsupported categories don't pollute
        // Fail/RuntimeError buckets. Only one matching tag is reported; the
        // rest are irrelevant once the first has ruled the test out.
        if (_skipFeatures.Count > 0 && metadata.Features.Count > 0)
        {
            foreach (var feature in metadata.Features)
            {
                if (_skipFeatures.Contains(feature))
                    return new Test262Result(Test262Outcome.Skipped, null, $"skip-feature:{feature}");
            }
        }

        string assembled;
        int harnessLength;
        try
        {
            (assembled, harnessLength) = _assembler.Assemble(body, metadata);
        }
        catch (FileNotFoundException ex)
        {
            return new Test262Result(Test262Outcome.HarnessError, ex.Message, null);
        }

        // Each Test262 file is its own realm. The previously process-global
        // mutable built-in state (primitive prototypes' extras, Math, globalThis,
        // the Symbol.for registry) is now owned per-realm by the Interpreter, so
        // every `new Interpreter(...)` below already starts pristine — no reset
        // needed (issue #964 follow-up, since superseded by per-realm-ization).
        return mode switch
        {
            Test262ExecutionMode.Interpreted => RunInterpreted(assembled, harnessLength, metadata.IsAsync),
            Test262ExecutionMode.Compiled => RunCompiled(assembled, harnessLength, metadata.IsAsync),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    private Test262Result RunInterpreted(string source, int harnessLength, bool isAsync)
    {
        // Parse + type-check on the caller's thread — neither honors the VM
        // timeout token so we'd rather not burn a dedicated thread per test
        // on work that doesn't need cancellation.
        SharpTS.Diagnostics.ParseDiagnosticResult parseResult;
        Lexer lexer;
        try
        {
            lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            parseResult = parser.Parse();
        }
        catch (Exception ex)
        {
            // Lexer throws raw Exception on syntax it won't tokenize
            // (legacy octals, bad escapes). Those are parse-phase failures.
            return new Test262Result(Test262Outcome.ParseError, ex.Message, null);
        }
        if (!parseResult.IsSuccess)
            return new Test262Result(Test262Outcome.ParseError, parseResult.Diagnostics.First().ToString(), null);

        // Test262 sources are .js — match tsc's default and skip type-checking
        // unless `// @ts-check` opts a specific test in.
        TypeMap typeMap = new();
        if (TypeCheckPolicy.ShouldTypeCheck(filePath: ".js", lexer.Pragmas, checkJsDefault: false))
        {
            try
            {
                var checker = new TypeChecker();
                typeMap = checker.Check(parseResult.Statements);
            }
            catch (TypeCheckException ex)
            {
                return new Test262Result(Test262Outcome.TypeCheckError, ex.Message, null);
            }
        }

        // Execute on a dedicated thread and signal the interpreter's VM
        // timeout token on deadline. The token is checked between statements
        // and loop iterations, so most runaway tests self-terminate promptly.
        // Pathological tight-loop tests can still run longer than the
        // timeout — we detect and log that leak but don't block.
        Test262Result? result = null;
        Exception? threadException = null;
        using var cts = new CancellationTokenSource();

        // For async-flagged tests, inject a `$DONE` global. The compiled-mode
        // assembler can't poke runtime globals directly, so this path is
        // host-side; the compiled path uses a JS shim instead. See issue #79.
        var doneCallback = isAsync ? new Test262DoneCallback() : null;

        var thread = new Thread(() =>
        {
            var sw = new StringWriter();
            using var interpreter = new Interpreter(stdout: sw, stderr: TextWriter.Null);
            // Plain scripts do not implicitly await promise-valued expression
            // statements. Async Test262 cases still use the legacy wait so their
            // terminal .then($DONE, $DONE) chain completes before classification.
            interpreter.WaitForTopLevelPromises = isAsync;
            interpreter.SetVmTimeoutToken(cts.Token);
            if (doneCallback is not null)
                interpreter.RegisterGlobal("$DONE", doneCallback);
            try
            {
                // Interpret() drives the event loop after running top-level
                // statements, so a Promise chain ending in `.then($DONE, $DONE)`
                // resolves to a $DONE call before this returns.
                interpreter.Interpret(parseResult.Statements, typeMap);
                // Interpret() swallows a top-level guest `throw` (prints "Runtime
                // Error: …" and returns) so the CLI need not surface a .NET stack
                // trace. Without inspecting LastUncaughtError here, a thrown
                // assertion (Test262Error) or runtime TypeError would be scored a
                // Pass — the interp baseline could not report Fail at all. Route
                // the swallowed error through the same classifier the propagated
                // path uses so it buckets as Fail / RuntimeError correctly.
                result = interpreter.LastUncaughtError is { } swallowed
                    ? ClassifyExecutionException(swallowed, harnessLength)
                    : doneCallback is not null
                        ? ClassifyAsyncDone(doneCallback)
                        : new Test262Result(Test262Outcome.Pass, null, null);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        })
        { IsBackground = true };

        thread.Start();
        // Poll for either completion, deadline, or runaway memory. Polling
        // at 100ms costs ~50 wakeups for a test that runs the full 5s; the
        // overhead is trivial compared to the interpreter work.
        var deadline = DateTime.UtcNow + _timeout;
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var baselineMemory = process.WorkingSet64;
        while (thread.IsAlive)
        {
            if (thread.Join(100)) break;
            if (DateTime.UtcNow > deadline)
            {
                cts.Cancel();
                thread.Join(TimeSpan.FromMilliseconds(500));
                return new Test262Result(Test262Outcome.Timeout, $"exceeded {_timeout.TotalSeconds}s", null);
            }
            process.Refresh();
            if (process.WorkingSet64 - baselineMemory > MemoryLimitBytes)
            {
                cts.Cancel();
                thread.Join(TimeSpan.FromMilliseconds(500));
                // Force a GC so we don't carry the bloat into the next test's
                // baseline measurement (and surface a moment of relief to the OS).
                GC.Collect();
                return new Test262Result(Test262Outcome.Timeout,
                    $"memory watchdog fired (grew by {(process.WorkingSet64 - baselineMemory) / 1024 / 1024}MB)", null);
            }
        }

        if (threadException is not null)
            return ClassifyExecutionException(threadException, harnessLength);
        return result ?? new Test262Result(Test262Outcome.RuntimeError, "runner produced no result", null);
    }

    private Test262Result RunCompiled(string source, int harnessLength, bool isAsync)
    {
        // Compile phase may throw parse/type errors before any IL is emitted.
        // Execution phase runs under a timeout because infinite loops in the
        // emitted program would hang the test assembly.
        var assemblyName = $"test262_{Guid.NewGuid():N}";

        // Compiled mode can't reach into the runtime environment to inject a
        // host callable, so async-flagged tests get a JS shim that defines
        // `$DONE` in terms of `console.log` sentinel lines. The runner scans
        // the captured stdout after Main returns. See issue #79.
        if (isAsync)
        {
            const string strictPrefix = "\"use strict\";\n";
            source = source.StartsWith(strictPrefix, StringComparison.Ordinal)
                ? strictPrefix + CompiledAsyncDoneShim + source[strictPrefix.Length..]
                : CompiledAsyncDoneShim + source;
            harnessLength += CompiledAsyncDoneShim.Length;
        }

        SharpTS.Diagnostics.ParseDiagnosticResult parseResult;
        Lexer lexer;
        long _phaseStart = Stopwatch.GetTimestamp();
        try
        {
            lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            parseResult = parser.Parse();
        }
        catch (Exception ex)
        {
            Interlocked.Add(ref CompiledPhaseStats.LexParseTicks, Stopwatch.GetTimestamp() - _phaseStart);
            return new Test262Result(Test262Outcome.ParseError, ex.Message, null);
        }
        Interlocked.Add(ref CompiledPhaseStats.LexParseTicks, Stopwatch.GetTimestamp() - _phaseStart);
        if (!parseResult.IsSuccess)
            return new Test262Result(Test262Outcome.ParseError, parseResult.Diagnostics.First().ToString(), null);

        // Test262 sources are .js — match tsc's default and skip type-checking
        // unless `// @ts-check` opts a specific test in.
        TypeMap typeMap = new();
        _phaseStart = Stopwatch.GetTimestamp();
        if (TypeCheckPolicy.ShouldTypeCheck(filePath: ".js", lexer.Pragmas, checkJsDefault: false))
        {
            try
            {
                var checker = new TypeChecker();
                typeMap = checker.Check(parseResult.Statements);
            }
            catch (TypeCheckException ex)
            {
                Interlocked.Add(ref CompiledPhaseStats.TypeCheckTicks, Stopwatch.GetTimestamp() - _phaseStart);
                return new Test262Result(Test262Outcome.TypeCheckError, ex.Message, null);
            }
        }
        Interlocked.Add(ref CompiledPhaseStats.TypeCheckTicks, Stopwatch.GetTimestamp() - _phaseStart);

        _phaseStart = Stopwatch.GetTimestamp();
        var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
        var deadCodeInfo = deadCodeAnalyzer.Analyze(parseResult.Statements);
        Interlocked.Add(ref CompiledPhaseStats.DeadCodeTicks, Stopwatch.GetTimestamp() - _phaseStart);

        // Compile to bytes — no temp dir, no DLL on disk. ALC loads from
        // an in-memory stream below. On contended file systems (Windows +
        // Defender scanning every freshly written DLL) the disk roundtrip
        // adds 50–100 ms per test, so eliminating it ~halves per-test
        // latency for the Test262 hot path.
        byte[] assemblyBytes;
        try
        {
            _phaseStart = Stopwatch.GetTimestamp();
            var compiler = new ILCompiler(assemblyName);
            compiler.Compile(parseResult.Statements, typeMap, deadCodeInfo);
            Interlocked.Add(ref CompiledPhaseStats.ILCompileTicks, Stopwatch.GetTimestamp() - _phaseStart);

            _phaseStart = Stopwatch.GetTimestamp();
            assemblyBytes = compiler.SaveToBytes();
            Interlocked.Add(ref CompiledPhaseStats.SaveBytesTicks, Stopwatch.GetTimestamp() - _phaseStart);
        }
        catch (Exception ex)
        {
            return new Test262Result(Test262Outcome.HarnessError, $"IL compile failed: {ex.Message}", null);
        }

        // Load into a collectible ALC so the test's dynamic assembly can be
        // released after the test runs (see issue #109). All references to
        // the assembly's reflected members must die before Unload + GC for
        // memory to actually free.
        var _alcLoadStart = Stopwatch.GetTimestamp();
        _phaseStart = Stopwatch.GetTimestamp();
        AssemblyLoadContext? alc = UseNonCollectibleLoad
            ? null
            : new AssemblyLoadContext($"test262_{assemblyName}", isCollectible: true);
        Interlocked.Add(ref CompiledPhaseStats.AlcCtorTicks, Stopwatch.GetTimestamp() - _phaseStart);

        Assembly assembly;
        MethodInfo? mainMethod;
        FieldInfo? cancelField;
        try
        {
            _phaseStart = Stopwatch.GetTimestamp();
            if (alc is not null)
            {
                using var assemblyStream = new MemoryStream(assemblyBytes);
                assembly = alc.LoadFromStream(assemblyStream);
            }
            else
            {
                assembly = Assembly.Load(assemblyBytes);
            }
            Interlocked.Add(ref CompiledPhaseStats.AlcLoadFromStreamTicks, Stopwatch.GetTimestamp() - _phaseStart);

            _phaseStart = Stopwatch.GetTimestamp();
            var programType = assembly.GetType("$Program");
            mainMethod = programType?.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
            if (mainMethod is null)
            {
                Interlocked.Add(ref CompiledPhaseStats.AlcReflectionTicks, Stopwatch.GetTimestamp() - _phaseStart);
                Interlocked.Add(ref CompiledPhaseStats.AlcLoadTicks, Stopwatch.GetTimestamp() - _alcLoadStart);
                Interlocked.Increment(ref CompiledPhaseStats.Count);
                return new Test262Result(Test262Outcome.HarnessError, "$Program.Main not found in compiled assembly", null);
            }

            var runtimeType = assembly.GetType("$Runtime");
            cancelField = runtimeType?.GetField("_cancelRequested",
                BindingFlags.Public | BindingFlags.Static);
            Interlocked.Add(ref CompiledPhaseStats.AlcReflectionTicks, Stopwatch.GetTimestamp() - _phaseStart);
        }
        catch
        {
            Interlocked.Add(ref CompiledPhaseStats.AlcLoadTicks, Stopwatch.GetTimestamp() - _alcLoadStart);
            try { alc?.Unload(); } catch { }
            throw;
        }
        Interlocked.Add(ref CompiledPhaseStats.AlcLoadTicks, Stopwatch.GetTimestamp() - _alcLoadStart);

        try
        {
            _phaseStart = Stopwatch.GetTimestamp();
            var result = InvokeCompiledMain(mainMethod, harnessLength, cancelField, isAsync);
            Interlocked.Add(ref CompiledPhaseStats.InvokeTicks, Stopwatch.GetTimestamp() - _phaseStart);
            return result;
        }
        finally
        {
            _phaseStart = Stopwatch.GetTimestamp();
            alc?.Unload();
            Interlocked.Add(ref CompiledPhaseStats.UnloadTicks, Stopwatch.GetTimestamp() - _phaseStart);
            Interlocked.Increment(ref CompiledPhaseStats.Count);
            MaybeRunPeriodicGc();
        }
    }

    /// <summary>
    /// Forces a GC pass every <see cref="GcEveryNTests"/> compiled tests so unloaded
    /// ALCs actually release their memory. Per .NET docs, collectible ALCs enter
    /// "unloading" state on <c>Unload()</c> but only release after the GC sees no
    /// references — which requires explicit <c>GC.Collect</c> + finalizer drain.
    /// </summary>
    private void MaybeRunPeriodicGc()
    {
        _compiledTestsSinceGc++;
        if (_compiledTestsSinceGc >= GcEveryNTests)
        {
            _compiledTestsSinceGc = 0;
            var gcStart = Stopwatch.GetTimestamp();
            // Drop runtime caches that pin emitted Types by acting as strong
            // back-references through their MethodInfo/FieldInfo values.
            // Without these clears, every emitted $TSFunction / $BoundTSFunction
            // / user-class type is pinned indefinitely, and through them the
            // entire dynamic assembly. See issue #109.
            RuntimeTypes.ClearReflectionCaches();
            SharpTS.Runtime.RuntimeCallableDispatcher.ClearCaches();
            // Two cycles: first to mark, second to clean up references freed by finalizers.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Interlocked.Add(ref CompiledPhaseStats.PeriodicGcTicks, Stopwatch.GetTimestamp() - gcStart);
        }
    }

    private Test262Result InvokeCompiledMain(MethodInfo mainMethod, int harnessLength, FieldInfo? cancelField, bool isAsync)
    {
        // AsyncLocal-scoped redirection. Each Task.Run inherits its parent's
        // ExecutionContext (which carries the AsyncLocal slots), so the per-test
        // override naturally follows the test's logical execution context. No lock,
        // no per-test SetOut wrapping cost.
        Test262Result? result = null;
        StringWriter? capturedStdout = null;
        var task = Task.Run(() =>
        {
            var sw = new StringWriter();
            capturedStdout = sw;
            using var _outScope = RedirectConsoleOut(sw);
            using var _errScope = RedirectConsoleError(TextWriter.Null);
            try
            {
                mainMethod.Invoke(null, null);
                result = new Test262Result(Test262Outcome.Pass, null, null);
            }
            catch (TargetInvocationException tie)
            {
                result = ClassifyExecutionException(tie.InnerException ?? tie, harnessLength);
            }
            catch (Exception ex)
            {
                result = ClassifyExecutionException(ex, harnessLength);
            }
        });

        // Poll for timeout or memory runaway. Compiled-mode tests run through
        // the same process, so the watchdog threshold applies symmetrically.
        // When the deadline hits, we flip $Runtime._cancelRequested (the per-
        // assembly cooperative cancellation flag emitted per issue #74); the
        // next loop backedge in the compiled IL sees it and throws
        // OperationCanceledException, unwinding the task within ~100ms (the
        // event loop's Wait resolution) to a full iteration of the hot loop.
        var deadline = DateTime.UtcNow + _timeout;
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var baselineMemory = process.WorkingSet64;
        while (!task.IsCompleted)
        {
            if (task.Wait(100)) break;
            if (DateTime.UtcNow > deadline)
            {
                TripCancelAndJoin(cancelField, task);
                return new Test262Result(Test262Outcome.Timeout, $"exceeded {_timeout.TotalSeconds}s", null);
            }
            process.Refresh();
            if (process.WorkingSet64 - baselineMemory > MemoryLimitBytes)
            {
                TripCancelAndJoin(cancelField, task);
                GC.Collect();
                return new Test262Result(Test262Outcome.Timeout,
                    $"memory watchdog fired (grew by {(process.WorkingSet64 - baselineMemory) / 1024 / 1024}MB)", null);
            }
        }

        // Async-flagged tests gate Pass on a `$DONE` sentinel emitted by the
        // shim (see CompiledAsyncDoneShim). If Main threw synchronously
        // (Fail/RuntimeError), that bucket already reflects the outcome and
        // we don't override it.
        if (isAsync && result is { Outcome: Test262Outcome.Pass } && capturedStdout is not null)
        {
            var sentinel = ParseDoneSentinel(capturedStdout.ToString());
            return sentinel ?? new Test262Result(
                Test262Outcome.Fail, "async test ended without invoking $DONE", null);
        }

        return result ?? new Test262Result(Test262Outcome.RuntimeError, "runner produced no result", null);
    }

    /// <summary>
    /// Flips <c>$Runtime._cancelRequested</c> on the timed-out test's assembly
    /// and gives the task a short grace period to unwind. If the emitted IL is
    /// at a loop backedge (the common case) or inside the event-loop drain, the
    /// next cancellation-check call will throw <see cref="OperationCanceledException"/>
    /// and the task completes cleanly. Without this, an orphan task keeps
    /// consuming CPU and holding assembly memory until the process exits —
    /// which is what caused the Promise rollout (#69) to hang in compile mode.
    /// </summary>
    private static void TripCancelAndJoin(FieldInfo? cancelField, Task task)
    {
        if (cancelField is null) return;
        try
        {
            cancelField.SetValue(null, true);
            task.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Best-effort; if Wait or SetValue throws we fall through to
            // returning Timeout anyway.
        }
    }

    /// <summary>
    /// Maps an exception thrown from inside the running test to an outcome
    /// bucket. <c>Test262Error</c> (thrown by <c>harness/assert.js</c> on
    /// assertion failure) is the sentinel that distinguishes <see cref="Test262Outcome.Fail"/>
    /// from <see cref="Test262Outcome.RuntimeError"/>.
    /// </summary>
    private static Test262Result ClassifyExecutionException(Exception ex, int harnessLength)
    {
        // Unwrap one layer of TargetInvocationException / AggregateException
        while (ex is TargetInvocationException or AggregateException && ex.InnerException is not null)
            ex = ex.InnerException;

        if (ex is TypeCheckException tce)
            return new Test262Result(Test262Outcome.TypeCheckError, tce.Message, null);

        var msg = ex.Message ?? "";
        // Interpreter/compiler surface user-thrown JS errors with their `name`
        // in the message. `assert.sameValue` etc. throw `new Test262Error(...)`.
        if (msg.Contains("Test262Error", StringComparison.Ordinal))
            return new Test262Result(Test262Outcome.Fail, msg, null);

        // Compiled mode: user-thrown JS values are preserved in Exception.Data["__tsValue"].
        // For `throw new Test262Error(msg)`, the value is a Dictionary or $Object whose
        // `name` field is "Test262Error". Check that before falling back to RuntimeError.
        if (ex.Data.Contains("__tsValue"))
        {
            var tsValue = ex.Data["__tsValue"];
            string? name = null;
            string? userMessage = null;
            if (tsValue is System.Collections.Generic.IDictionary<string, object?> d)
            {
                if (d.TryGetValue("name", out var n) && n is string ns) name = ns;
                if (d.TryGetValue("message", out var m) && m is string ms) userMessage = ms;
            }
            else if (tsValue != null)
            {
                // $Object / $Error subclass — read "name"/"message" via the emitted
                // $IHasFields interface (compiled $Object implements it). Fall back
                // to private `_fields` dictionary, then $Runtime.GetProperty (which
                // walks the prototype chain), then ToString.
                try
                {
                    var t = tsValue.GetType();
                    var iface = t.GetInterface("$IHasFields");
                    System.Reflection.MethodInfo? getProp = iface?.GetMethod("GetProperty", [typeof(string)]);
                    if (getProp != null)
                    {
                        name = getProp.Invoke(tsValue, ["name"]) as string;
                        userMessage = getProp.Invoke(tsValue, ["message"]) as string;
                    }
                    if (name == null)
                    {
                        // Try private _fields dictionary directly.
                        var fieldsField = t.GetField("_fields",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        if (fieldsField?.GetValue(tsValue) is System.Collections.Generic.IDictionary<string, object?> fd)
                        {
                            if (fd.TryGetValue("name", out var fn) && fn is string fns) name = fns;
                            if (fd.TryGetValue("message", out var fm) && fm is string fms) userMessage = fms;
                        }
                    }
                    if (name == null)
                    {
                        // Walk the prototype chain via $Runtime.GetProperty (the
                        // per-test-DLL helper that walks PDS-tracked prototypes).
                        // This catches `Test262Error.prototype.name = "Test262Error"`
                        // and similar harness-side patches.
                        var runtimeType = t.Assembly.GetType("$Runtime");
                        var rtGetProp = runtimeType?.GetMethod("GetProperty",
                            new[] { typeof(object), typeof(string) });
                        if (rtGetProp != null)
                        {
                            name = rtGetProp.Invoke(null, [tsValue, "name"]) as string;
                            if (userMessage == null)
                                userMessage = rtGetProp.Invoke(null, [tsValue, "message"]) as string;
                        }
                    }
                }
                catch { }
                // Last-resort: stringify-like check
                if (name == null)
                {
                    var s = tsValue.ToString();
                    if (s != null && s.StartsWith("Test262Error", StringComparison.Ordinal))
                        name = "Test262Error";
                }
            }
            if (name == "Test262Error")
                return new Test262Result(Test262Outcome.Fail, userMessage ?? msg, null);

            // Heuristic fallback — Test262Error message shapes from the assert
            // harness all start with predictable strings. With Stage 4z22 the
            // assert harness actually runs (PDS-MethodInfo-keyed reads/writes
            // co-locate), so assertion failures throw Test262Error. The
            // instance-side `name` field doesn't always survive the
            // throw-catch cycle reliably, so match on the message shape.
            if (userMessage is not null && IsTest262HarnessMessage(userMessage))
                return new Test262Result(Test262Outcome.Fail, userMessage, null);
        }

        // Last-resort: ex.Message itself looks like an assert-harness Test262Error.
        if (IsTest262HarnessMessage(msg))
            return new Test262Result(Test262Outcome.Fail, msg, null);

        return new Test262Result(Test262Outcome.RuntimeError, msg, null);
    }

    private static bool IsTest262HarnessMessage(string m)
    {
        // assert.js / sta.js error message prefixes (verified against the
        // committed harness files). Conservative match on substrings that
        // are unlikely to appear in plain RuntimeError messages.
        return m.Contains("Expected SameValue", StringComparison.Ordinal)
            || m.Contains("Expected true but got", StringComparison.Ordinal)
            || m.Contains("expected at least", StringComparison.Ordinal)
            || m.Contains("Thrown value was not an object", StringComparison.Ordinal)
            || m.Contains("Expected a ", StringComparison.Ordinal)
                && (m.Contains(" to be thrown", StringComparison.Ordinal)
                    || m.Contains(" but got", StringComparison.Ordinal));
    }

    /// <summary>
    /// Maps the captured state of a <see cref="Test262DoneCallback"/> to the
    /// final outcome bucket once <see cref="Interpreter.Interpret"/> has
    /// returned (event loop drained). Mirrors the harness's truthy-error
    /// convention: any truthy argument is a failure; no call at all is a
    /// failure too (the test resolved without signalling completion).
    /// </summary>
    private static Test262Result ClassifyAsyncDone(Test262DoneCallback done)
    {
        if (!done.Called)
            return new Test262Result(Test262Outcome.Fail, "async test ended without invoking $DONE", null);

        var rv = RuntimeValue.FromBoxed(done.ErrorArg);
        if (!rv.IsTruthy())
            return new Test262Result(Test262Outcome.Pass, null, null);

        // Reuse ThrowException's name/message extractor so we get the same
        // "Test262Error: msg" shape the synchronous classifier sees.
        var ex = ThrowException.FromResult(done.ErrorArg);
        var msg = ex.Message;
        if (msg.StartsWith("Test262Error", StringComparison.Ordinal)
            || IsTest262HarnessMessage(msg))
        {
            const string prefix = "Test262Error: ";
            var trimmed = msg.StartsWith(prefix, StringComparison.Ordinal)
                ? msg[prefix.Length..]
                : msg;
            return new Test262Result(Test262Outcome.Fail, trimmed, null);
        }
        return new Test262Result(Test262Outcome.RuntimeError, msg, null);
    }

    /// <summary>
    /// Scans compiled-mode stdout for the <c>$DONE</c> sentinel emitted by
    /// <see cref="CompiledAsyncDoneShim"/>. Returns null when the test never
    /// hit <c>$DONE</c> (caller maps that to <see cref="Test262Outcome.Fail"/>).
    /// </summary>
    private static Test262Result? ParseDoneSentinel(string stdout)
    {
        const string okSentinel = "$$T262DONE_OK$$";
        const string errSentinelPrefix = "$$T262DONE_ERR$$:";
        using var reader = new StringReader(stdout);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line == okSentinel)
                return new Test262Result(Test262Outcome.Pass, null, null);
            if (!line.StartsWith(errSentinelPrefix, StringComparison.Ordinal)) continue;

            // Format: $$T262DONE_ERR$$:<name>:<message>
            // Message can itself contain ':' so split only on the first one.
            var rest = line[errSentinelPrefix.Length..];
            int colon = rest.IndexOf(':');
            string name = colon < 0 ? "" : rest[..colon];
            string message = colon < 0 ? rest : rest[(colon + 1)..];
            if (name == "Test262Error" || IsTest262HarnessMessage(message))
                return new Test262Result(Test262Outcome.Fail, message, null);
            return new Test262Result(
                Test262Outcome.RuntimeError,
                string.IsNullOrEmpty(name) ? message : $"{name}: {message}",
                null);
        }
        return null;
    }

    /// <summary>
    /// JS shim prepended to async-flagged tests in compiled mode. Defines
    /// <c>$DONE</c> in terms of <c>console.log</c> sentinel lines that the
    /// runner scans after Main returns. The first call wins (matches the
    /// harness convention; subsequent calls are no-ops). See issue #79.
    /// </summary>
    private const string CompiledAsyncDoneShim = @"
var $$T262_done_called = false;
function $DONE(err) {
  if ($$T262_done_called) return;
  $$T262_done_called = true;
  if (arguments.length === 0 || err === undefined || err === null || err === false || err === 0 || err === '') {
    console.log('$$T262DONE_OK$$');
    return;
  }
  var n = '';
  var m = String(err);
  if (typeof err === 'object') {
    if (err.name) n = String(err.name);
    if (err.message) m = String(err.message);
  }
  console.log('$$T262DONE_ERR$$:' + n + ':' + m);
}
// Compiled top-level declarations live in static fields rather than the
// global-object property store. asyncHelpers.js feature-detects the async
// harness with hasOwnProperty(globalThis, ""$DONE""). Define an own global
// descriptor explicitly so the host hook has the same shape in both modes.
Object.defineProperty(globalThis, '$DONE', {
  value: $DONE,
  writable: true,
  configurable: true
});
";

    /// <summary>
    /// Host-side <c>$DONE</c> callable for interpreted mode. Captures the first
    /// argument the test passes (or none) and exposes that to the runner. The
    /// callback ignores subsequent invocations so a misbehaving test that fires
    /// twice doesn't mask an earlier success or failure.
    /// </summary>
    private sealed class Test262DoneCallback : ISharpTSCallable, ITypeCategorized
    {
        public TypeCategory RuntimeCategory => TypeCategory.Function;
        public bool Called { get; private set; }
        public object? ErrorArg { get; private set; }

        public int Arity() => 1;

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            if (Called) return null;
            Called = true;
            ErrorArg = arguments.Count > 0 ? arguments[0] : null;
            return null;
        }

        public override string ToString() => "function $DONE() { [native code] }";
    }
}
