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
using System.Text.RegularExpressions;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
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
    string FileName = "input.ts",
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
/// <param name="CompileTimeMs">Wall-clock time for the full pipeline (lex through PE emit).</param>
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
    IReadOnlyCollection<string> RequiredSharpTSRuntimeReasons);

/// <summary>
/// Result of <see cref="CompilationService.Execute"/>.
/// </summary>
/// <param name="Success">True when the program ran to completion without an unhandled exception.</param>
/// <param name="Error">Message of the unhandled guest exception, or null on success.</param>
/// <param name="ExecuteTimeMs">Wall-clock execution time.</param>
public sealed record RunResult(
    bool Success,
    string? Error,
    long ExecuteTimeMs);

/// <summary>
/// Programmatic compile-and-run facade for embedding SharpTS as a library.
/// </summary>
public static class CompilationService
{
    /// <summary>
    /// Compiles a TypeScript source string to an in-memory .NET assembly.
    /// Never writes to <see cref="Console"/>, never calls <see cref="Environment.Exit"/>,
    /// never touches the file system. All source-input problems (lex, parse, type,
    /// compile) are returned as <see cref="CompileResult.Diagnostics"/>, with the same
    /// multi-error recovery behavior as the CLI (<c>CheckWithRecovery</c>) and the same
    /// <c>// @ts-ignore</c> / <c>// @ts-expect-error</c> line-directive handling.
    /// </summary>
    public static CompileResult Compile(string source, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var assemblyName = options.AssemblyName ?? $"ts_{Guid.NewGuid():N}";
        var stopwatch = Stopwatch.StartNew();

        CompileResult Fail(IEnumerable<Diagnostic> diagnostics) =>
            new(false, diagnostics.ToList(), null, stopwatch.ElapsedMilliseconds, Array.Empty<string>());

        try
        {
            // Lex. The Lexer reports errors by throwing raw Exceptions with the line
            // embedded in the message ("... at line N") — convert to a ParseError
            // diagnostic instead of letting it escape.
            bool isTsx = options.FileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                || options.FileName.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
                || options.Jsx is not null;
            Lexer lexer;
            List<Token> tokens;
            try
            {
                lexer = new Lexer(source) { JsxTolerant = isTsx };
                tokens = lexer.ScanTokens();
            }
            catch (Exception ex)
            {
                return Fail([LexErrorToDiagnostic(ex.Message, options.FileName)]);
            }

            // Parse, with recovery — surfaces all parse errors, not just the first.
            var parser = new Parser(tokens, options.DecoratorMode)
                .WithFilePath(options.FileName);
            if (isTsx)
                parser.WithJsx(source, options.Jsx ?? JsxParseOptions.Default);
            var parseResult = parser.Parse();
            if (!parseResult.IsSuccess)
                return Fail(parseResult.Diagnostics);

            // Type check, with recovery, honoring // @ts-ignore / @ts-expect-error.
            var checker = new TypeChecker().WithFilePath(options.FileName);
            checker.SetDecoratorMode(options.DecoratorMode);
            var typeResult = checker.CheckWithRecovery(parseResult.Statements);
            var diagnostics = TypeCheckPolicy.ApplyLineDirectives(typeResult.Diagnostics, lexer.Pragmas);
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                return Fail(diagnostics);

            var deadCodeInfo = new DeadCodeAnalyzer(typeResult.TypeMap).Analyze(parseResult.Statements);

            var compiler = new ILCompiler(assemblyName);
            compiler.SetDecoratorMode(options.DecoratorMode);
            compiler.Compile(parseResult.Statements, typeResult.TypeMap, deadCodeInfo);
            var bytes = compiler.SaveToBytes();

            stopwatch.Stop();
            return new CompileResult(
                Success: true,
                Diagnostics: diagnostics,
                AssemblyBytes: bytes,
                CompileTimeMs: stopwatch.ElapsedMilliseconds,
                RequiredSharpTSRuntimeReasons: compiler.RequiredSharpTSRuntimeReasons);
        }
        catch (SharpTSException ex)
        {
            return Fail([ex.Diagnostic]);
        }
        catch (Exception ex)
        {
            // Internal compiler defect surfaced by this source. A web host is better
            // served by a CompileError diagnostic than an exception across the API.
            return Fail([Diagnostic.CompileError(ex.Message)]);
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
    public static RunResult Execute(byte[] assemblyBytes, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(output);

        var alc = new AssemblyLoadContext($"SharpTS.Execute_{Guid.NewGuid():N}", isCollectible: true);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var stream = new MemoryStream(assemblyBytes, writable: false);
            var assembly = alc.LoadFromStream(stream);

            var programType = assembly.GetType("$Program")
                ?? throw new InvalidOperationException("Compiled assembly has no $Program type");
            var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("$Program has no public static Main method");

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
            try
            {
                mainMethod.Invoke(null, null);
                stopwatch.Stop();
                return new RunResult(true, null, stopwatch.ElapsedMilliseconds);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                stopwatch.Stop();
                return new RunResult(false, tie.InnerException.Message, stopwatch.ElapsedMilliseconds);
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
            stopwatch.Stop();
            return new RunResult(false, ex.Message, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            // Best-effort: actual collection waits for the GC, and statics pinned by
            // the default ALC (timers, event-loop threads) can delay or prevent it.
            alc.Unload();
        }
    }

    /// <summary>
    /// Converts a thrown Lexer message into a ParseError diagnostic, recovering the
    /// line number from the conventional "... at line N" message suffix when present.
    /// </summary>
    private static Diagnostic LexErrorToDiagnostic(string message, string fileName)
    {
        var match = Regex.Match(message, @"\bat line (\d+)\b");
        SourceLocation? location = match.Success && int.TryParse(match.Groups[1].Value, out var line)
            ? new SourceLocation(fileName, line)
            : null;
        return Diagnostic.ParseError(message, location);
    }
}
