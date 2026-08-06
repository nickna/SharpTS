// =============================================================================
// CompilationService.cs - Public embedding API for compile-and-run (issue #171)
// =============================================================================
//
// Library-consumable facade over the Lexer → Parser → TypeChecker → ILCompiler
// pipeline. Unlike the CLI flow in Program.cs, this path never writes to
// Console, never calls Environment.Exit, and never touches the file system:
// source comes in as a string, the assembly comes out as PE bytes, and all
// lex/parse/type/compile failures come back as structured Diagnostics.
//
// Primary consumer: hosts that embed SharpTS (e.g. the website playground's
// compiled mode), which compile small sources per request and execute the
// result in-process with captured output.
//
// =============================================================================

using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Options for <see cref="CompilationService.Compile"/>.
/// </summary>
/// <param name="DecoratorMode">Decorator dialect to parse/compile with.</param>
/// <param name="AssemblyName">
/// Simple name for the emitted assembly. Defaults to a unique GUID-suffixed name so
/// repeated compiles loaded into one host process never collide on simple-name
/// assembly identity.
/// </param>
/// <param name="FileName">
/// Logical file name used as the <see cref="SourceLocation.FilePath"/> on diagnostics.
/// Purely informational for I/O — nothing is read from or written to this path — but a
/// .tsx/.jsx extension switches the parser into the TSX dialect.
/// </param>
/// <param name="Jsx">
/// JSX settings applied when the source parses in the TSX dialect. Null uses
/// <see cref="JsxParseOptions.Default"/> for .tsx/.jsx file names.
/// </param>
public sealed record CompileOptions(
    DecoratorMode DecoratorMode = DecoratorMode.None,
    string? AssemblyName = null,
    string FileName = CompilationService.DefaultSourceFileName,
    JsxParseOptions? Jsx = null);

/// <summary>
/// Result of <see cref="CompilationService.Compile"/>.
/// </summary>
/// <param name="Success">True when an assembly was produced with no error diagnostics.</param>
/// <param name="Diagnostics">
/// All diagnostics from the pipeline. On failure, contains at least one
/// <see cref="DiagnosticSeverity.Error"/>; on success, may contain warnings.
/// </param>
/// <param name="AssemblyBytes">The emitted PE image, or null on failure.</param>
/// <param name="CompileTimeMs">
/// Wall-clock time for compiler backend work, excluding tokenization, parsing, and type-checking.
/// </param>
/// <param name="RequiredSharpTSRuntimeReasons">
/// Non-empty when the program uses a feature whose emitted IL late-binds into
/// SharpTS.dll at runtime (eval, Proxy, Intl, vm, dns, @DotNetType dynamic events).
/// Irrelevant for in-process execution via <see cref="CompilationService.Execute"/> —
/// SharpTS.dll is by definition loaded — but a host that ships the DLL to run
/// elsewhere must co-locate SharpTS.dll when this is non-empty.
/// </param>
public sealed record CompileResult(
    bool Success,
    IReadOnlyList<Diagnostic> Diagnostics,
    byte[]? AssemblyBytes,
    long CompileTimeMs,
    IReadOnlyCollection<string> RequiredSharpTSRuntimeReasons)
{
    /// <summary>Ordered, precise front-end and compiler phase timings.</summary>
    public IReadOnlyList<ExecutionPhaseTiming> Timings { get; init; } = [];

    /// <summary>
    /// Stable deployment capabilities for hosts that persist <see cref="AssemblyBytes"/>.
    /// </summary>
    public SharpTSRuntimeRequirements RuntimeRequirements { get; init; }
}

/// <summary>
/// Result of <see cref="CompilationService.Execute"/>.
/// </summary>
/// <param name="Success">True when the program ran to completion without an unhandled exception.</param>
/// <param name="Error">Message of the unhandled guest exception, or null on success.</param>
/// <param name="ExecuteTimeMs">Wall-clock execution time.</param>
public sealed record RunResult(
    bool Success,
    string? Error,
    long ExecuteTimeMs)
{
    /// <summary>
    /// Ordered load and invocation timings. Invocation can include lazy first-use JIT work.
    /// </summary>
    public IReadOnlyList<ExecutionPhaseTiming> Timings { get; init; } = [];
}

/// <summary>
/// Programmatic compile-and-run facade for embedding SharpTS as a library.
/// </summary>
public static class CompilationService
{
    /// <summary>Logical file name used for in-memory TypeScript source by default.</summary>
    public const string DefaultSourceFileName = "input.ts";

    /// <summary>
    /// Compiles a TypeScript source string to an in-memory .NET assembly.
    /// Never writes to <see cref="Console"/>, never calls <see cref="Environment.Exit"/>,
    /// never touches the file system. All source-input problems (lex, parse, type,
    /// compile) are returned as <see cref="CompileResult.Diagnostics"/>, with the same
    /// multi-error recovery behavior as the CLI (<c>CheckWithRecovery</c>) and the same
    /// <c>// @ts-ignore</c> / <c>// @ts-expect-error</c> line-directive handling. This API has
    /// no module resolver, so all static, dynamic, and CommonJS module-loading forms are rejected
    /// with a module diagnostic; use the module compilation pipeline for dependency graphs.
    /// </summary>
    public static CompileResult Compile(string source, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var assemblyName = options.AssemblyName ?? $"ts_{Guid.NewGuid():N}";
        try
        {
            var analysis = SingleSourceAnalyzer.Analyze(
                source,
                new SingleSourceAnalysisOptions(
                    options.DecoratorMode,
                    options.FileName,
                    options.Jsx));
            if (!analysis.Success)
            {
                return new CompileResult(
                    false,
                    analysis.Diagnostics,
                    null,
                    0,
                    Array.Empty<string>())
                {
                    Timings = analysis.Timings
                };
            }

            var timings = analysis.Timings.ToList();
            var compileStartedAt = Stopwatch.GetTimestamp();
            try
            {
                var deadCodeInfo = new DeadCodeAnalyzer(analysis.TypeMap!).Analyze(analysis.Statements);

                var compiler = new ILCompiler(assemblyName);
                compiler.SetDecoratorMode(options.DecoratorMode);
                compiler.Compile(analysis.Statements, analysis.TypeMap!, deadCodeInfo);
                var bytes = compiler.SaveToBytes();

                var compileDurationMs = Stopwatch.GetElapsedTime(compileStartedAt).TotalMilliseconds;
                timings.Add(ExecutionPhaseTiming.Completed("compile", compileDurationMs));
                return new CompileResult(
                    Success: true,
                    Diagnostics: analysis.Diagnostics,
                    AssemblyBytes: bytes,
                    CompileTimeMs: (long)compileDurationMs,
                    RequiredSharpTSRuntimeReasons: compiler.RequiredSharpTSRuntimeReasons)
                {
                    Timings = timings,
                    RuntimeRequirements = compiler.RequiredSharpTSRuntimeRequirements
                };
            }
            catch (SharpTSException ex)
            {
                var compileDurationMs = Stopwatch.GetElapsedTime(compileStartedAt).TotalMilliseconds;
                timings.Add(ExecutionPhaseTiming.Failed("compile", compileDurationMs));
                return new CompileResult(
                    false,
                    [ex.Diagnostic],
                    null,
                    (long)compileDurationMs,
                    Array.Empty<string>())
                {
                    Timings = timings
                };
            }
            catch (Exception ex)
            {
                var compileDurationMs = Stopwatch.GetElapsedTime(compileStartedAt).TotalMilliseconds;
                timings.Add(ExecutionPhaseTiming.Failed("compile", compileDurationMs));
                return new CompileResult(
                    false,
                    [Diagnostic.CompileError(ex.Message)],
                    null,
                    (long)compileDurationMs,
                    Array.Empty<string>())
                {
                    Timings = timings
                };
            }
        }
        catch (SharpTSException ex)
        {
            return new CompileResult(false, [ex.Diagnostic], null, 0, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            // Internal compiler defect surfaced by this source. A web host is better
            // served by a CompileError diagnostic than an exception across the API.
            return new CompileResult(
                false,
                [Diagnostic.CompileError(ex.Message)],
                null,
                0,
                Array.Empty<string>());
        }
    }

    /// <summary>
    /// Loads a compiled assembly (from <see cref="CompileResult.AssemblyBytes"/>) into a
    /// collectible <see cref="AssemblyLoadContext"/> and invokes its entry point in-process,
    /// routing the program's <see cref="Console.Out"/>/<see cref="Console.Error"/> writes to
    /// <paramref name="output"/>. An unhandled guest exception is returned as a failed
    /// <see cref="RunResult"/>, not thrown.
    ///
    /// <para><b>Console contract:</b> emitted IL writes directly to <see cref="Console"/>;
    /// this method swaps <see cref="Console.SetOut"/>/<see cref="Console.SetError"/> for the
    /// duration of the run and restores them afterward. That swap is process-global — this
    /// method is safe for one execution at a time per process (e.g. a process-per-request
    /// worker), not for concurrent in-process tenants.</para>
    ///
    /// <para><b>Cancellation:</b> <paramref name="cancellationToken"/> trips the emitted
    /// program's cooperative-cancel flag (<c>$Runtime._cancelRequested</c>), which every loop
    /// backedge polls — a runaway loop unwinds itself without killing the host. There is no
    /// hard wall-clock kill; the host owns that.</para>
    ///
    /// <para><b>Limitation:</b> guest <c>process.exit(n)</c> compiles to
    /// <see cref="Environment.Exit"/> and terminates the host process.</para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The method rejects Native AOT before loading or reflecting over emitted IL; in-process IL execution is a managed-host-only API.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "The method rejects Native AOT before loading or reflecting over emitted IL; in-process IL execution is a managed-host-only API.")]
    public static RunResult Execute(byte[] assemblyBytes, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(output);

        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            return new RunResult(
                false,
                "In-process compiled assembly execution is not available in a native SharpTS build — use a managed host.",
                0)
            {
                Timings = [ExecutionPhaseTiming.Failed("load", 0)]
            };
        }

        var alc = new AssemblyLoadContext($"SharpTS.Execute_{Guid.NewGuid():N}", isCollectible: true);
        var timings = new List<ExecutionPhaseTiming>();
        try
        {
            Assembly assembly;
            Type programType;
            MethodInfo mainMethod;
            var loadStartedAt = Stopwatch.GetTimestamp();
            try
            {
                using var stream = new MemoryStream(assemblyBytes, writable: false);
                assembly = alc.LoadFromStream(stream);

                programType = assembly.GetType("$Program")
                    ?? throw new InvalidOperationException("Compiled assembly has no $Program type");
                mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("$Program has no public static Main method");
                timings.Add(ExecutionPhaseTiming.Completed(
                    "load", Stopwatch.GetElapsedTime(loadStartedAt).TotalMilliseconds));
            }
            catch (Exception ex)
            {
                timings.Add(ExecutionPhaseTiming.Failed(
                    "load", Stopwatch.GetElapsedTime(loadStartedAt).TotalMilliseconds));
                return new RunResult(false, ex.Message, 0) { Timings = timings };
            }

            // Cooperative cancellation: the emitted $Runtime polls _cancelRequested at
            // every loop backedge (issue #74).
            using var cancelRegistration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() =>
                {
                    try
                    {
                        assembly.GetType("$Runtime")
                            ?.GetField("_cancelRequested", BindingFlags.Public | BindingFlags.Static)
                            ?.SetValue(null, true);
                    }
                    catch { /* best-effort */ }
                })
                : default;

            var priorOut = Console.Out;
            var priorErr = Console.Error;
            Console.SetOut(output);
            Console.SetError(output);
            // The emitted Main installs $EventLoopSyncContext on the current thread
            // (issues #319/#320/#381). For in-process embedding the calling thread is
            // long-lived and may invoke Execute repeatedly, so restore the ambient
            // context afterward rather than leaking the loop context onto the host.
            var priorSyncContext = System.Threading.SynchronizationContext.Current;
            var executeStartedAt = Stopwatch.GetTimestamp();
            try
            {
                mainMethod.Invoke(null, null);
                var executeDurationMs = Stopwatch.GetElapsedTime(executeStartedAt).TotalMilliseconds;
                timings.Add(ExecutionPhaseTiming.Completed("execute", executeDurationMs));
                return new RunResult(true, null, (long)executeDurationMs) { Timings = timings };
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                var executeDurationMs = Stopwatch.GetElapsedTime(executeStartedAt).TotalMilliseconds;
                timings.Add(ExecutionPhaseTiming.Failed("execute", executeDurationMs));
                return new RunResult(false, tie.InnerException.Message, (long)executeDurationMs)
                {
                    Timings = timings
                };
            }
            catch (Exception ex)
            {
                var executeDurationMs = Stopwatch.GetElapsedTime(executeStartedAt).TotalMilliseconds;
                timings.Add(ExecutionPhaseTiming.Failed("execute", executeDurationMs));
                return new RunResult(false, ex.Message, (long)executeDurationMs) { Timings = timings };
            }
            finally
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(priorSyncContext);
                Console.SetOut(priorOut);
                Console.SetError(priorErr);
            }
        }
        catch (Exception ex)
        {
            if (timings.Count == 0 || timings[^1].Name != "execute")
                timings.Add(ExecutionPhaseTiming.Failed("execute", 0));
            return new RunResult(false, ex.Message, 0) { Timings = timings };
        }
        finally
        {
            // Best-effort: actual collection waits for the GC, and statics pinned by
            // the default ALC (timers, event-loop threads) can delay or prevent it.
            alc.Unload();
        }
    }

}
