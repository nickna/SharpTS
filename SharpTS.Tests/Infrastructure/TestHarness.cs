using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using SharpTS.Compilation;
using SharpTS.Diagnostics;
using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Test harness utilities for running TypeScript source through the interpreter
/// and compiler, capturing console output for assertions.
/// </summary>
public static class TestHarness
{
    /// <summary>
    /// Default timeout for test execution (30 seconds).
    /// Async iterator bugs can cause infinite loops - this ensures tests fail fast.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    /// <summary>
    /// Legacy lock for tests that still call <see cref="Console.SetOut"/> directly
    /// (e.g., LSP bridge tests, diagnostic reporter tests). New compiled-mode runners
    /// use <see cref="AsyncLocalConsoleRedirector"/> instead, which doesn't need a lock.
    /// </summary>
    internal static readonly object ConsoleLock = new();

    private static readonly TimeSpan InterpreterCleanupTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs an interpreter on a dedicated thread. Interpreter tests are invoked synchronously by
    /// xUnit, so using Task.Run here consumes a second ThreadPool worker that only blocks while the
    /// test worker waits. Under aggressive suite parallelism that can starve the async operations
    /// needed by the interpreter itself (notably socket and TLS continuations).
    /// </summary>
    private static string RunInterpreterWithTimeout(
        TimeSpan timeout,
        string timeoutMessage,
        Action<Interpreter> execute)
    {
        var timeoutCts = new CancellationTokenSource();
        var task = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    var sw = new StringWriter();
                    using var interpreter = new Interpreter(stdout: sw, stderr: TextWriter.Null);
                    interpreter.SetVmTimeoutToken(timeoutCts.Token);
                    using var registration = timeoutCts.Token.Register(interpreter.Shutdown);

                    execute(interpreter);
                    return sw.ToString().Replace("\r\n", "\n");
                }
                finally
                {
                    timeoutCts.Dispose();
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);

        try
        {
            if (task.Wait(timeout))
                return task.GetAwaiter().GetResult();
        }
        catch (AggregateException)
        {
            // Task.Wait wraps faults; GetAwaiter preserves the exception type expected by tests.
            return task.GetAwaiter().GetResult();
        }

        // Cancel synchronously so both statement execution and the event loop observe the
        // deadline even when the ThreadPool is saturated. Give the dedicated worker a bounded
        // opportunity to run its using/finally cleanup before returning control to the suite.
        try { timeoutCts.Cancel(); }
        catch (ObjectDisposedException) { }

        try { task.Wait(InterpreterCleanupTimeout); }
        catch (AggregateException) { /* timeout remains the externally observable failure */ }

        throw new TimeoutException(timeoutMessage);
    }

    /// <summary>
    /// Runs TypeScript source using the specified execution mode and captures console output.
    /// This is the primary entry point for parameterized tests that should run against
    /// both the interpreter and compiler.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="mode">Execution mode (Interpreted or Compiled)</param>
    /// <returns>Captured console output</returns>
    public static string Run(string source, ExecutionMode mode)
    {
        return mode switch
        {
            ExecutionMode.Interpreted => RunInterpreted(source),
            ExecutionMode.Compiled => RunCompiled(source),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>
    /// Runs TypeScript source using the specified execution mode with decorator support.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="mode">Execution mode (Interpreted or Compiled)</param>
    /// <param name="decoratorMode">Decorator mode (None, Legacy, Stage3)</param>
    /// <returns>Captured console output</returns>
    public static string Run(string source, ExecutionMode mode, DecoratorMode decoratorMode)
    {
        return mode switch
        {
            ExecutionMode.Interpreted => RunInterpreted(source, decoratorMode),
            ExecutionMode.Compiled => RunCompiled(source, decoratorMode),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>
    /// Runs TypeScript modules using the specified execution mode.
    /// This is the primary entry point for parameterized module tests.
    /// Fails with <see cref="TypeCheckDiagnosticException"/> if the program doesn't
    /// type-check (#1226); tests that intentionally run ill-typed programs pass
    /// <paramref name="allowTypeErrors"/>.
    /// </summary>
    /// <param name="files">Dictionary of file paths to file contents</param>
    /// <param name="entryPoint">Path to the entry point module</param>
    /// <param name="mode">Execution mode (Interpreted or Compiled)</param>
    /// <param name="allowTypeErrors">Skip the error-severity diagnostic check after CheckModules</param>
    /// <returns>Captured console output</returns>
    public static string RunModules(Dictionary<string, string> files, string entryPoint, ExecutionMode mode, TimeSpan? timeout = null, bool allowTypeErrors = false)
    {
        return mode switch
        {
            ExecutionMode.Interpreted => RunModulesInterpreted(files, entryPoint, timeout, allowTypeErrors),
            ExecutionMode.Compiled => RunModulesCompiled(files, entryPoint, timeout, allowTypeErrors),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>
    /// Runs <see cref="TypeChecker.CheckModules"/> and throws on error-severity diagnostics,
    /// mirroring the CLI (<c>Program.RunModuleFile</c>). CheckModules records type errors with
    /// recovery instead of throwing, so without this check the module runners silently execute
    /// ill-typed programs and type-check regressions in stdlib facades go unnoticed (#1226).
    /// Warnings (e.g. from lenient CJS modules) don't block, same as the CLI.
    /// </summary>
    internal static TypeMap CheckModulesOrThrow(TypeChecker checker, List<ParsedModule> modules, ModuleResolver resolver, bool allowTypeErrors = false)
    {
        var typeMap = checker.CheckModules(modules, resolver);
        if (!allowTypeErrors)
        {
            var errors = checker.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            if (errors.Count > 0)
                throw new TypeCheckDiagnosticException(errors);
        }
        return typeMap;
    }

    /// <summary>
    /// Parses TypeScript source code and returns the list of statements.
    /// Useful for testing AST structure without execution.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <returns>List of parsed statements</returns>
    public static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var result = parser.Parse();
        return result.Statements;
    }

    /// <summary>
    /// Parses source and throws on the first parse error. The throwing twin of
    /// <see cref="Parse"/>; replaces the byte-identical private helper that had
    /// spread across the parser test files (2026-07 cleanup).
    /// </summary>
    public static List<Stmt> ParseOrThrow(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    /// <summary>
    /// Runs TypeScript source through the interpreter and captures console output.
    /// Uses default timeout to prevent infinite loops from hanging tests.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <returns>Captured console output</returns>
    public static string RunInterpreted(string source)
    {
        return RunInterpreted(source, DecoratorMode.None, DefaultTimeout);
    }

    /// <summary>
    /// Runs TypeScript source through the interpreter with a timeout.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="timeout">Maximum execution time before throwing TimeoutException</param>
    /// <returns>Captured console output</returns>
    public static string RunInterpreted(string source, TimeSpan timeout)
    {
        return RunInterpreted(source, DecoratorMode.None, timeout);
    }

    /// <summary>
    /// Runs TypeScript source through the interpreter with decorator support and captures console output.
    /// Uses default timeout.
    /// </summary>
    public static string RunInterpreted(string source, DecoratorMode decoratorMode)
    {
        return RunInterpreted(source, decoratorMode, DefaultTimeout);
    }

    /// <summary>
    /// Runs TypeScript source through the interpreter with decorator support and timeout.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="decoratorMode">Decorator mode (None, Legacy, Stage3)</param>
    /// <param name="timeout">Maximum execution time before throwing TimeoutException</param>
    /// <returns>Captured console output</returns>
    /// <exception cref="TimeoutException">Thrown if execution exceeds the timeout (likely an infinite loop bug)</exception>
    public static string RunInterpreted(string source, DecoratorMode decoratorMode, TimeSpan timeout)
    {
        return RunInterpreterWithTimeout(
            timeout,
            $"Interpreter execution exceeded {timeout.TotalSeconds}s timeout. " +
            "This likely indicates an infinite loop bug (e.g., Promise double-wrapping in async iterators).",
            interpreter =>
        {
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, decoratorMode);
            var statements = parser.ParseOrThrow();

            var checker = new TypeChecker();
            checker.SetDecoratorMode(decoratorMode);
            var typeMap = checker.Check(statements);

            interpreter.SetDecoratorMode(decoratorMode);
            // Fire Node process lifecycle events (beforeExit/exit at drain) like
            // the CLI does, so dual-mode tests can observe them. Tests that
            // REGISTER lifecycle listeners share the process singleton and must
            // run in a serialized collection + reset via
            // ProcessBuiltIns.ResetProcessState() (see ProcessLifecycleTests).
            interpreter.EmitProcessLifecycleEvents = true;
            interpreter.Interpret(statements, typeMap);
        });
    }

    /// <summary>
    /// Compiles TypeScript source to a .NET DLL, executes it, and captures output.
    /// Uses default timeout to prevent infinite loops from hanging tests.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <returns>Captured console output from the compiled executable</returns>
    public static string RunCompiled(string source)
    {
        return RunCompiled(source, DecoratorMode.None, DefaultTimeout);
    }

    /// <summary>
    /// Compiles TypeScript source to a .NET DLL with a timeout, executes it, and captures output.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="timeout">Maximum execution time before throwing TimeoutException</param>
    /// <returns>Captured console output from the compiled executable</returns>
    public static string RunCompiled(string source, TimeSpan timeout)
    {
        return RunCompiled(source, DecoratorMode.None, timeout);
    }

    /// <summary>
    /// Compiles TypeScript source to a .NET DLL with decorator support, executes it, and captures output.
    /// Uses default timeout.
    /// </summary>
    public static string RunCompiled(string source, DecoratorMode decoratorMode)
    {
        return RunCompiled(source, decoratorMode, DefaultTimeout);
    }

    /// <summary>
    /// Compiles and runs with <c>SharpTS.Tests.dll</c> copied alongside the compiled
    /// output so <c>@DotNetType</c> can resolve test fixture types at runtime (e.g.,
    /// <c>SharpTS.Tests.Infrastructure.CallbackFixture</c>).
    /// </summary>
    public static string RunCompiledWithTestFixtures(string source, DecoratorMode decoratorMode = DecoratorMode.Legacy)
    {
        return RunCompiled(source, decoratorMode, DefaultTimeout, scriptArgs: null, includeTestsAssembly: true);
    }

    /// <summary>
    /// Like <see cref="RunCompiled(string, DecoratorMode)"/> but does NOT copy SharpTS.dll
    /// alongside the output. Simulates the real-world flow of shipping a user's compiled
    /// DLL without the SharpTS runtime — exposes bugs where emitted IL reflects into
    /// SharpTS via <c>Type.GetType("..., SharpTS")</c> and then NREs when that returns null.
    /// </summary>
    public static string RunCompiledStandalone(string source, DecoratorMode decoratorMode = DecoratorMode.Legacy)
    {
        return RunCompiled(source, decoratorMode, DefaultTimeout, scriptArgs: null, includeTestsAssembly: false, copySharpTsRuntime: false);
    }

    /// <summary>
    /// Compiles TypeScript source to a .NET DLL with decorator support and timeout, executes it, and captures output.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="decoratorMode">Decorator mode (None, Legacy, Stage3)</param>
    /// <param name="timeout">Maximum execution time before throwing TimeoutException</param>
    /// <returns>Captured console output from the compiled executable</returns>
    /// <exception cref="TimeoutException">Thrown if execution exceeds the timeout (likely an infinite loop bug)</exception>
    public static string RunCompiled(string source, DecoratorMode decoratorMode, TimeSpan timeout, string[]? scriptArgs = null, bool includeTestsAssembly = false, bool copySharpTsRuntime = true)
    {
        // The default fast path is in-process Assembly.LoadFrom — it skips the ~4s
        // dotnet-startup-plus-SharpTS-JIT cost per test by reusing the testhost's
        // already-warmed runtime. Two cases still need a real subprocess:
        //   1. scriptArgs != null — `process.argv` in compiled mode reads
        //      Environment.GetCommandLineArgs() directly (see RuntimeEmitter.ProcessHelpers),
        //      which would surface the testhost's argv in-process, not the test's.
        //   2. copySharpTsRuntime == false — RunCompiledStandalone simulates a
        //      shipped DLL with no SharpTS.dll alongside; that only repros via spawn.
        // SHARPTS_FORCE_SUBPROCESS=1 forces the spawn path for every compiled
        // test — a diagnostic knob for launch-path-sensitive investigations
        // (the #39 JIT bug only reproduced through the subprocess path).
        if (scriptArgs is not null || !copySharpTsRuntime ||
            Environment.GetEnvironmentVariable("SHARPTS_FORCE_SUBPROCESS") == "1")
            return RunCompiledViaSubprocess(source, decoratorMode, timeout, scriptArgs, includeTestsAssembly, copySharpTsRuntime);

        return RunCompiledInProcess(source, decoratorMode, timeout);
    }

    /// <summary>
    /// In-process compile + load + invoke. Skips the ~4s subprocess startup by reusing
    /// the testhost's already-warm SharpTS.dll. Uses a unique GUID-suffixed assembly
    /// name so concurrent <see cref="Assembly.Load(byte[])"/> calls don't collide on
    /// simple-name identity (see feedback_test_perf_changes.md). Output is captured
    /// per logical-execution-context via <see cref="AsyncLocalConsoleRedirector"/>, so
    /// parallel tests don't fight over <see cref="Console.Out"/>.
    /// </summary>
    private static string RunCompiledInProcess(string source, DecoratorMode decoratorMode, TimeSpan timeout)
    {
        var assemblyName = $"test_{Guid.NewGuid():N}";

        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, decoratorMode);
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        checker.SetDecoratorMode(decoratorMode);
        var typeMap = checker.Check(statements);

        var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
        var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

        var compiler = new ILCompiler(assemblyName);
        compiler.SetDecoratorMode(decoratorMode);
        compiler.Compile(statements, typeMap, deadCodeInfo);

        // Skip the disk roundtrip — load the assembly straight from the in-memory PE bytes.
        var bytes = compiler.SaveToBytes();

        // Systemic guardrail (#886): with SHARPTS_VERIFY_COMPILED=1, run ILVerify on every
        // in-process compiled program so the whole compiled-mode suite becomes a provable check
        // for IL-correctness regressions (stale _stackType, double-box, etc.). Opt-in because the
        // suite may still contain pre-existing unverifiable-but-runnable programs that need triage
        // before this can be enabled by default.
        if (Environment.GetEnvironmentVariable("SHARPTS_VERIFY_COMPILED") == "1")
        {
            var verifyErrors = VerifyILBytes(bytes)
                .Where(e => !e.Contains("Failed to load assembly"))
                .ToList();
            if (verifyErrors.Count > 0)
                throw new InvalidOperationException(
                    "IL verification failed for compiled program:\n  " + string.Join("\n  ", verifyErrors));
        }

        var assembly = Assembly.Load(bytes);
        var programType = assembly.GetType("$Program")
            ?? throw new InvalidOperationException("Compiled assembly has no $Program type");
        var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("$Program has no public static Main method");

        // Run on a worker so we can enforce a timeout. AsyncLocal flows into Task.Run,
        // so the redirector's capture buffer follows the test through the task boundary.
        var task = Task.Run(() =>
        {
            using var capture = AsyncLocalConsoleRedirector.Capture();
            // The compiled Main installs an event-loop SynchronizationContext on
            // this thread; restore the previous one so it doesn't leak onto the
            // recycled Task.Run pool thread and disturb a sibling test.
            var prevCtx = System.Threading.SynchronizationContext.Current;
            try
            {
                mainMethod.Invoke(null, null);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            }
            finally
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(prevCtx);
            }
            return capture.GetOutput().Replace("\r\n", "\n");
        });

        try
        {
            if (task.Wait(timeout))
                return task.Result;

            // Cooperatively cancel: the compiled $Runtime polls _cancelRequested at every
            // loop backedge (issue #74). Tripping it lets a runaway loop unwind itself
            // without orphan-threading the testhost. Best-effort wait for the unwind so
            // the worker thread doesn't stay pinned, but always surface a TimeoutException
            // to callers — that's the contract the subprocess path established and tests
            // assert against.
            try
            {
                var cancelField = assembly.GetType("$Runtime")?.GetField("_cancelRequested",
                    BindingFlags.Public | BindingFlags.Static);
                cancelField?.SetValue(null, true);
            }
            catch { /* best-effort */ }

            try { task.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore — we're throwing TimeoutException anyway */ }

            throw new TimeoutException(
                $"Compiled program execution exceeded {timeout.TotalSeconds}s timeout. " +
                "This likely indicates an infinite loop bug (e.g., Promise double-wrapping in async iterators).");
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
        {
            // Unwrap so Assert.Throws<SpecificException> still works.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            throw; // unreachable
        }
    }

    /// <summary>
    /// Subprocess fallback for the cases the in-process path can't serve: tests passing
    /// <c>scriptArgs</c> (need a real <c>Environment.GetCommandLineArgs()</c>) and
    /// standalone-DLL tests that explicitly omit SharpTS.dll from the output dir.
    /// </summary>
    private static string RunCompiledViaSubprocess(string source, DecoratorMode decoratorMode, TimeSpan timeout, string[]? scriptArgs, bool includeTestsAssembly, bool copySharpTsRuntime)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, decoratorMode);
            var statements = parser.ParseOrThrow();

            var checker = new TypeChecker();
            checker.SetDecoratorMode(decoratorMode);
            var typeMap = checker.Check(statements);

            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

            var compiler = new ILCompiler("test");
            compiler.SetDecoratorMode(decoratorMode);
            compiler.Compile(statements, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            // Copy SharpTS.dll and its dependencies for runtime dependency (needed for Promise.all/race/allSettled/any)
            // Tests that intentionally simulate a standalone deployment pass copySharpTsRuntime: false.
            if (copySharpTsRuntime)
            {
                var sharpTsDll = typeof(RuntimeTypes).Assembly.Location;
                if (!string.IsNullOrEmpty(sharpTsDll) && File.Exists(sharpTsDll))
                {
                    File.Copy(sharpTsDll, Path.Combine(tempDir, "SharpTS.dll"), overwrite: true);

                    // Copy ZstdSharp.dll (required for zstd compression in compiled mode)
                    var zstdDll = Path.Combine(Path.GetDirectoryName(sharpTsDll)!, "ZstdSharp.dll");
                    if (File.Exists(zstdDll))
                    {
                        File.Copy(zstdDll, Path.Combine(tempDir, "ZstdSharp.dll"), overwrite: true);
                    }
                }
            }

            // Optionally copy SharpTS.Tests.dll so `@DotNetType` fixture types (e.g.,
            // CallbackFixture) are loadable by the compiled-out-of-process executable.
            if (includeTestsAssembly)
            {
                var testsDll = typeof(TestHarness).Assembly.Location;
                if (!string.IsNullOrEmpty(testsDll) && File.Exists(testsDll))
                {
                    File.Copy(testsDll, Path.Combine(tempDir, Path.GetFileName(testsDll)), overwrite: true);
                }
            }

            // Write runtimeconfig.json
            var configPath = Path.Combine(tempDir, "test.runtimeconfig.json");
            File.WriteAllText(configPath, """
                {
                  "runtimeOptions": {
                    "tfm": "net10.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "10.0.0"
                    }
                  }
                }
                """);

            // Execute and capture output
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };
            psi.ArgumentList.Add(dllPath);
            if (scriptArgs != null)
            {
                foreach (var arg in scriptArgs)
                    psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Use timeout to catch infinite loop bugs
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                process.Kill();
                throw new TimeoutException(
                    $"Compiled program execution exceeded {timeout.TotalSeconds}s timeout. " +
                    "This likely indicates an infinite loop bug (e.g., Promise double-wrapping in async iterators).");
            }

            var output = outputTask.Result;
            var error = errorTask.Result;

            if (process.ExitCode != 0)
            {
                throw new Exception($"Compiled program exited with code {process.ExitCode}. Stderr: {error}");
            }

            // Normalize line endings for cross-platform test consistency
            return output.Replace("\r\n", "\n");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Runs TypeScript source with command-line arguments available via process.argv.slice(2).
    /// In interpreted mode, uses ProcessBuiltIns.SetScriptArguments.
    /// In compiled mode, passes arguments to the spawned dotnet process.
    /// </summary>
    public static string RunWithArgs(string source, ExecutionMode mode, string[] scriptArgs)
    {
        return mode switch
        {
            ExecutionMode.Interpreted => RunInterpretedWithArgs(source, scriptArgs),
            ExecutionMode.Compiled => RunCompiled(source, DecoratorMode.None, DefaultTimeout, scriptArgs),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private static string RunInterpretedWithArgs(string source, string[] scriptArgs)
    {
        ProcessBuiltIns.SetScriptArguments("script.ts", scriptArgs);
        try
        {
            return RunInterpreted(source);
        }
        finally
        {
            ProcessBuiltIns.ClearScriptArguments();
        }
    }

    /// <summary>
    /// Compiles TypeScript source, loads the assembly in-process, executes it, and returns both the assembly and output.
    /// This allows tests to perform reflection on the compiled types.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="decoratorMode">Decorator mode (None, Legacy, Stage3)</param>
    /// <returns>Tuple of (Assembly, console output)</returns>
    public static (Assembly assembly, string output) CompileAndRun(string source, DecoratorMode decoratorMode)
    {
        // Use unique assembly name to avoid conflicts when loading multiple assemblies
        var assemblyName = $"test_{Guid.NewGuid():N}";
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_{assemblyName}");
        Directory.CreateDirectory(tempDir);

        var dllPath = Path.Combine(tempDir, $"{assemblyName}.dll");

        // Compile
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, decoratorMode);
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        checker.SetDecoratorMode(decoratorMode);
        var typeMap = checker.Check(statements);

        var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
        var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

        var compiler = new ILCompiler(assemblyName);
        compiler.SetDecoratorMode(decoratorMode);
        compiler.Compile(statements, typeMap, deadCodeInfo);
        compiler.Save(dllPath);

        // Load assembly in-process for reflection
        var assembly = Assembly.LoadFrom(dllPath);

        // Capture output via AsyncLocal so concurrent tests don't fight over Console.Out.
        using var capture = AsyncLocalConsoleRedirector.Capture();
        var programType = assembly.GetType("$Program");
        var mainMethod = programType?.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
        var prevCtx = System.Threading.SynchronizationContext.Current;
        try { mainMethod?.Invoke(null, null); }
        finally { System.Threading.SynchronizationContext.SetSynchronizationContext(prevCtx); }

        return (assembly, capture.GetOutput().Replace("\r\n", "\n"));
    }

    /// <summary>
    /// Compiles TypeScript source to a .NET DLL with reference assembly mode enabled.
    /// Returns the path to the compiled DLL (in a temp directory that caller should clean up).
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <returns>Tuple of (tempDir, dllPath) - caller must clean up tempDir</returns>
    public static (string tempDir, string dllPath) CompileWithRefAsm(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_refasm_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var dllPath = Path.Combine(tempDir, "test.dll");

        // Compile
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        var typeMap = checker.Check(statements);

        var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
        var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

        // Use reference assembly mode
        var compiler = new ILCompiler("test", preserveConstEnums: false, useReferenceAssemblies: true, sdkPath: null);
        compiler.Compile(statements, typeMap, deadCodeInfo);
        compiler.Save(dllPath);

        // Copy SharpTS.dll and its dependencies for runtime dependency
        var sharpTsDll = typeof(RuntimeTypes).Assembly.Location;
        if (!string.IsNullOrEmpty(sharpTsDll) && File.Exists(sharpTsDll))
        {
            File.Copy(sharpTsDll, Path.Combine(tempDir, "SharpTS.dll"), overwrite: true);

        }

        // Write runtimeconfig.json
        var configPath = Path.Combine(tempDir, "test.runtimeconfig.json");
        File.WriteAllText(configPath, """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "10.0.0"
                }
              }
            }
            """);

        return (tempDir, dllPath);
    }

    /// <summary>
    /// Executes a compiled DLL and returns its console output.
    /// </summary>
    /// <param name="dllPath">Path to the DLL</param>
    /// <returns>Console output</returns>
    public static string ExecuteCompiledDll(string dllPath)
    {
        var workingDir = Path.GetDirectoryName(dllPath)!;
        var psi = new ProcessStartInfo("dotnet", dllPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir
        };

        using var process = Process.Start(psi)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)DefaultTimeout.TotalMilliseconds))
        {
            process.Kill();
            throw new TimeoutException(
                $"Compiled DLL execution exceeded {DefaultTimeout.TotalSeconds}s timeout.");
        }

        var output = outputTask.Result;
        var error = errorTask.Result;

        if (process.ExitCode != 0)
        {
            throw new Exception($"Compiled program exited with code {process.ExitCode}. Stderr: {error}");
        }

        return output.Replace("\r\n", "\n");
    }

    /// <summary>
    /// Runs multiple TypeScript modules through the interpreter and captures console output.
    /// </summary>
    /// <param name="files">Dictionary mapping file paths to source code</param>
    /// <param name="entryPoint">The entry point file path (e.g., "./main.ts")</param>
    /// <returns>Captured console output</returns>
    public static string RunModulesInterpreted(Dictionary<string, string> files, string entryPoint, TimeSpan? timeout = null, bool allowTypeErrors = false)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

        // Tests using __dirname / __filename / cluster.fork need files to exist on a real
        // disk path that the runtime can stat / spawn workers against; route those through
        // the disk-based path. Everything else uses the in-memory virtual file system.
        if (RequiresRealDisk(files.Values))
            return RunModulesInterpretedOnDisk(files, entryPoint, effectiveTimeout, allowTypeErrors);

        // Build an in-memory virtual file system instead of writing to %TEMP%. Concurrent
        // disk operations on Windows serialize through the kernel/AV — measured 1.4× speedup
        // at 12 threads vs ideal 12×, which capped testhost CPU at ~10% during the run.
        var (virtualFiles, entryPath) = BuildVirtualModuleFs(files, entryPoint);

        return RunInterpreterWithTimeout(
            effectiveTimeout,
            $"Interpreted module execution exceeded {effectiveTimeout.TotalSeconds}s timeout.",
            interpreter =>
        {
            var resolver = new ModuleResolver(entryPath, virtualFiles);
            var entryModule = resolver.LoadModule(entryPath);
            var allModules = resolver.GetModulesInOrder(entryModule);

            var checker = new TypeChecker();
            var typeMap = CheckModulesOrThrow(checker, allModules, resolver, allowTypeErrors);

            // Match the CLI: fire process lifecycle events at loop drain (#1080).
            interpreter.EmitProcessLifecycleEvents = true;
            interpreter.InterpretModules(allModules, resolver, typeMap);
        });
    }

    /// <summary>
    /// Materializes a test's <c>files</c> dictionary as an in-memory file system. Returns
    /// (virtualFiles map, absolute virtual entry path). The virtual base directory is a
    /// per-call GUID-named path under <see cref="Path.GetTempPath"/> — that directory is
    /// never actually created on disk; the path is just a unique key for the virtual map
    /// so the resolver's <c>Path.GetFullPath</c> normalization yields stable keys.
    /// </summary>
    private static (Dictionary<string, string> Files, string EntryPath) BuildVirtualModuleFs(
        Dictionary<string, string> files, string entryPoint)
    {
        var virtualBase = Path.Combine(Path.GetTempPath(), $"sharpts_vfs_{Guid.NewGuid():N}");
        var virtualFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, content) in files)
        {
            var fullPath = Path.GetFullPath(Path.Combine(virtualBase, path.TrimStart('.', '/', '\\')));
            virtualFiles[fullPath] = content;
        }
        var entryPath = Path.GetFullPath(Path.Combine(virtualBase, entryPoint.TrimStart('.', '/', '\\')));
        return (virtualFiles, entryPath);
    }

    /// <summary>
    /// Compiles multiple TypeScript modules to a .NET DLL, executes it, and captures output.
    /// Defaults to in-process <see cref="Assembly.Load(byte[])"/> for speed (mirrors the
    /// single-source <see cref="RunCompiledInProcess"/> path). Falls back to a real subprocess
    /// for tests whose source touches process-global state the testhost can't isolate:
    ///   - <c>process.exit(...)</c> would terminate the testhost
    ///   - <c>process.chdir(...)</c> mutates <see cref="Environment.CurrentDirectory"/> and
    ///     races with parallel tests
    ///   - <c>process.cwd()</c> / <c>process.argv</c> read <see cref="Environment"/> directly
    ///     (see <c>RuntimeEmitter.ProcessHelpers</c>) and would return the testhost's view
    /// </summary>
    public static string RunModulesCompiled(Dictionary<string, string> files, string entryPoint, TimeSpan? timeout = null, bool allowTypeErrors = false)
    {
        if (RequiresSubprocess(files.Values))
            return RunModulesCompiledViaSubprocess(files, entryPoint, allowTypeErrors);
        if (RequiresRealDisk(files.Values))
            return RunModulesCompiledInProcessOnDisk(files, entryPoint, timeout, allowTypeErrors);
        return RunModulesCompiledInProcess(files, entryPoint, timeout, allowTypeErrors);
    }

    private static bool RequiresSubprocess(IEnumerable<string> sources)
    {
        foreach (var s in sources)
        {
            if (s.Contains("process.exit(") ||
                s.Contains("process.chdir(") ||
                s.Contains("process.cwd(") ||
                s.Contains("process.argv") ||
                // child_process.fork() spawns a real `dotnet exec SharpTS.dll <module>` child;
                // the compiled test must run as a real subprocess with the SharpTS runtime present.
                UsesChildProcessFork(s))
                return true;
        }
        return false;
    }

    /// <summary>True for a source that calls child_process.fork (imports it from 'child_process'
    /// and invokes fork(...)). Distinguished from cluster.fork by the import.</summary>
    private static bool UsesChildProcessFork(string s) =>
        s.Contains("'child_process'") && s.Contains("fork(") && !s.Contains("cluster.fork(");

    /// <summary>
    /// Some test sources need the file to exist on a real disk path:
    /// <list type="bullet">
    ///   <item><c>__dirname</c> / <c>__filename</c> — the runtime returns the script's
    ///   path; with the virtual FS that path doesn't exist on disk, so any subsequent
    ///   <c>fs.existsSync</c> / <c>fs.readFileSync</c> against it fails.</item>
    ///   <item><c>cluster.fork(</c> — workers spawn as separate dotnet processes that
    ///   load the source from disk. Virtual FS isn't visible across processes.</item>
    /// </list>
    /// </summary>
    private static bool RequiresRealDisk(IEnumerable<string> sources)
    {
        foreach (var s in sources)
        {
            if (s.Contains("__dirname") ||
                s.Contains("__filename") ||
                s.Contains("cluster.fork(") ||
                // child_process.fork() loads the child .ts module from disk in a separate process.
                UsesChildProcessFork(s))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Materializes the test's <c>files</c> dict on real disk under <see cref="Path.GetTempPath"/>,
    /// then runs the interpreter in-process. Used for tests that depend on real-disk paths
    /// (<c>__dirname</c>, <c>cluster.fork</c>) — see <see cref="RequiresRealDisk"/>.
    /// </summary>
    private static string RunModulesInterpretedOnDisk(Dictionary<string, string> files, string entryPoint, TimeSpan? timeout = null, bool allowTypeErrors = false)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_module_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var (path, content) in files)
            {
                var fullPath = Path.Combine(tempDir, path.TrimStart('.', '/', '\\'));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, content);
            }

            string entryPath = Path.Combine(tempDir, entryPoint.TrimStart('.', '/', '\\'));

            return RunInterpreterWithTimeout(
                effectiveTimeout,
                $"Interpreted module execution exceeded {effectiveTimeout.TotalSeconds}s timeout.",
                interpreter =>
            {
                var resolver = new ModuleResolver(entryPath);
                var entryModule = resolver.LoadModule(entryPath);
                var allModules = resolver.GetModulesInOrder(entryModule);

                var checker = new TypeChecker();
                var typeMap = CheckModulesOrThrow(checker, allModules, resolver, allowTypeErrors);

                interpreter.InterpretModules(allModules, resolver, typeMap);
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Disk-backed in-process compiled path for tests that need real-disk paths
    /// (<c>__dirname</c>, <c>cluster.fork</c>) — see <see cref="RequiresRealDisk"/>.
    /// </summary>
    private static string RunModulesCompiledInProcessOnDisk(Dictionary<string, string> files, string entryPoint, TimeSpan? timeout = null, bool allowTypeErrors = false)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_module_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var (path, content) in files)
            {
                var fullPath = Path.Combine(tempDir, path.TrimStart('.', '/', '\\'));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, content);
            }

            var entryPath = Path.Combine(tempDir, entryPoint.TrimStart('.', '/', '\\'));
            var assemblyName = $"test_modules_{Guid.NewGuid():N}";

            var resolver = new ModuleResolver(entryPath);
            var entryModule = resolver.LoadModule(entryPath);
            var allModules = resolver.GetModulesInOrder(entryModule);

            var checker = new TypeChecker();
            var typeMap = CheckModulesOrThrow(checker, allModules, resolver, allowTypeErrors);

            var allStatements = allModules.SelectMany(m => m.Statements).ToList();
            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(allStatements);

            var compiler = new ILCompiler(assemblyName);
            compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);

            var bytes = compiler.SaveToBytes();
            var assembly = Assembly.Load(bytes);
            var programType = assembly.GetType("$Program")
                ?? throw new InvalidOperationException("Compiled assembly has no $Program type");
            var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("$Program has no public static Main method");

            var task = Task.Run(() =>
            {
                using var capture = AsyncLocalConsoleRedirector.Capture();
                var prevCtx = System.Threading.SynchronizationContext.Current;
                try { mainMethod.Invoke(null, null); }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                }
                finally { System.Threading.SynchronizationContext.SetSynchronizationContext(prevCtx); }
                return capture.GetOutput().Replace("\r\n", "\n");
            });

            try
            {
                if (task.Wait(effectiveTimeout))
                    return task.Result;
                throw new TimeoutException(
                    $"Compiled module execution exceeded {effectiveTimeout.TotalSeconds}s timeout.");
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                throw;
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
        }
    }

    private static string RunModulesCompiledInProcess(Dictionary<string, string> files, string entryPoint, TimeSpan? timeout = null, bool allowTypeErrors = false)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        // In-memory virtual file system — see BuildVirtualModuleFs and the comment on
        // RunModulesInterpreted for the rationale.
        var (virtualFiles, entryPath) = BuildVirtualModuleFs(files, entryPoint);

        try
        {
            var assemblyName = $"test_modules_{Guid.NewGuid():N}";

            var resolver = new ModuleResolver(entryPath, virtualFiles);
            var entryModule = resolver.LoadModule(entryPath);
            var allModules = resolver.GetModulesInOrder(entryModule);

            var checker = new TypeChecker();
            var typeMap = CheckModulesOrThrow(checker, allModules, resolver, allowTypeErrors);

            var allStatements = allModules.SelectMany(m => m.Statements).ToList();
            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(allStatements);

            var compiler = new ILCompiler(assemblyName);
            compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);

            var bytes = compiler.SaveToBytes();
            var assembly = Assembly.Load(bytes);
            var programType = assembly.GetType("$Program")
                ?? throw new InvalidOperationException("Compiled assembly has no $Program type");
            var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("$Program has no public static Main method");

            var task = Task.Run(() =>
            {
                using var capture = AsyncLocalConsoleRedirector.Capture();
                // The compiled Main installs an event-loop SynchronizationContext on
                // this thread; restore the previous one so it doesn't leak onto the
                // recycled Task.Run pool thread and disturb a sibling test.
                var prevCtx = System.Threading.SynchronizationContext.Current;
                try
                {
                    mainMethod.Invoke(null, null);
                }
                catch (TargetInvocationException tie) when (tie.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                }
                finally
                {
                    System.Threading.SynchronizationContext.SetSynchronizationContext(prevCtx);
                }
                return capture.GetOutput().Replace("\r\n", "\n");
            });

            try
            {
                if (task.Wait(effectiveTimeout))
                    return task.Result;

                try
                {
                    var cancelField = assembly.GetType("$Runtime")?.GetField("_cancelRequested",
                        BindingFlags.Public | BindingFlags.Static);
                    cancelField?.SetValue(null, true);
                }
                catch { /* best-effort */ }

                try { task.Wait(TimeSpan.FromSeconds(2)); } catch { /* surfacing TimeoutException below */ }

                throw new TimeoutException(
                    $"Compiled module execution exceeded {effectiveTimeout.TotalSeconds}s timeout. " +
                    "This likely indicates an infinite loop bug.");
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                throw;
            }
        }
        finally
        {
            // No-op: virtual file system, nothing to clean on disk.
        }
    }

    /// <summary>
    /// Subprocess fallback for <see cref="RunModulesCompiled"/>. Used when a module's source
    /// touches <c>process.exit/chdir/cwd/argv</c> — the in-process path can't isolate those.
    /// </summary>
    private static string RunModulesCompiledViaSubprocess(Dictionary<string, string> files, string entryPoint, bool allowTypeErrors = false)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_module_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write all files to temp directory
            foreach (var (path, content) in files)
            {
                var fullPath = Path.Combine(tempDir, path.TrimStart('.', '/', '\\'));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(fullPath, content);
            }

            string entryPath = Path.Combine(tempDir, entryPoint.TrimStart('.', '/', '\\'));
            var dllPath = Path.Combine(tempDir, "test.dll");

            // Load and resolve modules
            var resolver = new ModuleResolver(entryPath);
            var entryModule = resolver.LoadModule(entryPath);
            var allModules = resolver.GetModulesInOrder(entryModule);

            // Type check
            var checker = new TypeChecker();
            var typeMap = CheckModulesOrThrow(checker, allModules, resolver, allowTypeErrors);

            // Dead code analysis across all modules
            var allStatements = allModules.SelectMany(m => m.Statements).ToList();
            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(allStatements);

            // Compile
            var compiler = new ILCompiler("test");
            compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            // Copy SharpTS.dll and its dependencies for runtime dependency
            var sharpTsDll = typeof(RuntimeTypes).Assembly.Location;
            if (!string.IsNullOrEmpty(sharpTsDll) && File.Exists(sharpTsDll))
            {
                File.Copy(sharpTsDll, Path.Combine(tempDir, "SharpTS.dll"), overwrite: true);

                var sharpTsDir = Path.GetDirectoryName(sharpTsDll)!;
                // Copy ZstdSharp.dll (required for zstd compression in compiled mode)
                var zstdDll = Path.Combine(sharpTsDir, "ZstdSharp.dll");
                if (File.Exists(zstdDll))
                    File.Copy(zstdDll, Path.Combine(tempDir, "ZstdSharp.dll"), overwrite: true);

                // child_process.fork spawns `dotnet exec SharpTS.dll <module>` — that separate
                // process needs SharpTS's full runtime closure (runtimeconfig + deps + dep DLLs),
                // mirroring Program.CopySharpTSRuntimeIfNeeded.
                if (files.Values.Any(UsesChildProcessFork))
                {
                    foreach (var src in Directory.EnumerateFiles(sharpTsDir))
                    {
                        var name = Path.GetFileName(src);
                        var ext = Path.GetExtension(name);
                        bool isRuntimeFile = ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("SharpTS.runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("SharpTS.deps.json", StringComparison.OrdinalIgnoreCase);
                        if (isRuntimeFile)
                            File.Copy(src, Path.Combine(tempDir, name), overwrite: true);
                    }
                }
            }

            // Write runtimeconfig.json
            var configPath = Path.Combine(tempDir, "test.runtimeconfig.json");
            File.WriteAllText(configPath, """
                {
                  "runtimeOptions": {
                    "tfm": "net10.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "10.0.0"
                    }
                  }
                }
                """);

            // Execute and capture output
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)DefaultTimeout.TotalMilliseconds))
            {
                var partialOut = outputTask.IsCompleted ? outputTask.Result : "(reading)";
                var partialErr = errorTask.IsCompleted ? errorTask.Result : "(reading)";
                process.Kill();
                throw new TimeoutException(
                    $"Compiled module execution exceeded {DefaultTimeout.TotalSeconds}s timeout. " +
                    $"Stdout: [{partialOut}] Stderr: [{partialErr}]");
            }

            var output = outputTask.Result;
            var error = errorTask.Result;

            if (process.ExitCode != 0)
            {
                throw new Exception($"Compiled program exited with code {process.ExitCode}. Stderr: {error}");
            }

            // Normalize line endings for cross-platform test consistency
            return output.Replace("\r\n", "\n");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Verifies the IL in a compiled assembly.
    /// </summary>
    /// <param name="dllPath">Path to the compiled DLL</param>
    /// <returns>List of verification error messages (empty if valid)</returns>
    public static List<string> VerifyIL(string dllPath)
    {
        using var verifier = new ILVerifier();
        using var stream = File.OpenRead(dllPath);
        return verifier.Verify(stream);
    }

    /// <summary>
    /// Verifies IL from in-memory PE bytes (no disk roundtrip). Used by the opt-in
    /// suite-wide verification guardrail in <see cref="RunCompiledInProcess"/>.
    /// </summary>
    private static List<string> VerifyILBytes(byte[] bytes)
    {
        using var verifier = new ILVerifier();
        using var stream = new MemoryStream(bytes);
        return verifier.Verify(stream);
    }

    /// <summary>
    /// Compiles TypeScript source and verifies the generated IL without running.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="decoratorMode">Decorator mode</param>
    /// <returns>List of verification errors</returns>
    public static List<string> CompileAndVerifyOnly(string source, DecoratorMode decoratorMode = DecoratorMode.None)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, decoratorMode);
            var statements = parser.ParseOrThrow();

            var checker = new TypeChecker();
            checker.SetDecoratorMode(decoratorMode);
            var typeMap = checker.Check(statements);

            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

            // Use reference assemblies for IL verification compatibility
            var sdkPath = SdkResolver.FindReferenceAssembliesPath();
            var compiler = new ILCompiler("test", preserveConstEnums: false, useReferenceAssemblies: true, sdkPath: sdkPath);
            compiler.SetDecoratorMode(decoratorMode);
            compiler.Compile(statements, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            // Verify IL - filter out expected "Failed to load assembly 'SharpTS'" errors
            var allErrors = VerifyIL(dllPath);
            return allErrors.Where(e => !e.Contains("Failed to load assembly")).ToList();
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Compiles a multi-module program (via the <see cref="ILCompiler.CompileModules"/> pipeline,
    /// the same path the CLI uses for any file with imports) and verifies the generated IL without
    /// running it. Use this — not <see cref="CompileAndVerifyOnly"/> — whenever the program has
    /// <c>import</c>s (including stdlib modules like <c>timers/promises</c>), since the single-file
    /// <see cref="ILCompiler.Compile"/> path does not resolve module dependencies. Catches bad IL in
    /// emitted module helpers that still runs under the JIT (e.g. #393), which the run-only
    /// <see cref="RunModules"/> path cannot detect.
    /// </summary>
    /// <param name="files">Map of module path → source.</param>
    /// <param name="entryPoint">Entry module path (key into <paramref name="files"/>).</param>
    /// <returns>List of verification errors (empty when the IL is valid).</returns>
    public static List<string> CompileModulesAndVerifyOnly(Dictionary<string, string> files, string entryPoint, bool allowTypeErrors = false)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_module_verify_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var (path, content) in files)
            {
                var fullPath = Path.Combine(tempDir, path.TrimStart('.', '/', '\\'));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(fullPath, content);
            }

            var entryPath = Path.Combine(tempDir, entryPoint.TrimStart('.', '/', '\\'));
            var dllPath = Path.Combine(tempDir, "test_modules.dll");

            var resolver = new ModuleResolver(entryPath);
            var entryModule = resolver.LoadModule(entryPath);
            var allModules = resolver.GetModulesInOrder(entryModule);

            var checker = new TypeChecker();
            var typeMap = CheckModulesOrThrow(checker, allModules, resolver, allowTypeErrors);

            var allStatements = allModules.SelectMany(m => m.Statements).ToList();
            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(allStatements);

            // Use reference assemblies for IL verification compatibility (matches CompileAndVerifyOnly).
            var sdkPath = SdkResolver.FindReferenceAssembliesPath();
            var compiler = new ILCompiler("test_modules", preserveConstEnums: false, useReferenceAssemblies: true, sdkPath: sdkPath);
            compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            // Filter out expected "Failed to load assembly 'SharpTS'" errors (the verifier can't
            // resolve the SharpTS runtime, which standalone output late-binds anyway).
            return VerifyIL(dllPath).Where(e => !e.Contains("Failed to load assembly")).ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Compiles TypeScript source and verifies the generated IL.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="decoratorMode">Decorator mode</param>
    /// <returns>Tuple of (verification errors, console output)</returns>
    public static (List<string> errors, string output) CompileVerifyAndRun(string source, DecoratorMode decoratorMode = DecoratorMode.None)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, decoratorMode);
            var statements = parser.ParseOrThrow();

            var checker = new TypeChecker();
            checker.SetDecoratorMode(decoratorMode);
            var typeMap = checker.Check(statements);

            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

            // Use reference assemblies for IL verification compatibility
            var sdkPath = SdkResolver.FindReferenceAssembliesPath();
            var compiler = new ILCompiler("test", preserveConstEnums: false, useReferenceAssemblies: true, sdkPath: sdkPath);
            compiler.SetDecoratorMode(decoratorMode);
            compiler.Compile(statements, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            // Verify IL - filter out expected "Failed to load assembly 'SharpTS'" errors
            var allErrors = VerifyIL(dllPath);
            var errors = allErrors.Where(e => !e.Contains("Failed to load assembly")).ToList();

            // Copy SharpTS.dll and its dependencies for runtime dependency
            var sharpTsDll = typeof(RuntimeTypes).Assembly.Location;
            if (!string.IsNullOrEmpty(sharpTsDll) && File.Exists(sharpTsDll))
            {
                File.Copy(sharpTsDll, Path.Combine(tempDir, "SharpTS.dll"), overwrite: true);

                // Copy ZstdSharp.dll (required for zstd compression in compiled mode)
                var zstdDll = Path.Combine(Path.GetDirectoryName(sharpTsDll)!, "ZstdSharp.dll");
                if (File.Exists(zstdDll))
                    File.Copy(zstdDll, Path.Combine(tempDir, "ZstdSharp.dll"), overwrite: true);
            }

            // Write runtimeconfig.json
            var configPath = Path.Combine(tempDir, "test.runtimeconfig.json");
            File.WriteAllText(configPath, """
                {
                  "runtimeOptions": {
                    "tfm": "net10.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "10.0.0"
                    }
                  }
                }
                """);

            // Execute and capture output
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Compiled program exited with code {process.ExitCode}. Stderr: {error}");
            }

            return (errors, output.Replace("\r\n", "\n"));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
