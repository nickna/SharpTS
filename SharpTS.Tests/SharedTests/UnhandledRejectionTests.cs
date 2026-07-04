using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// #228: exceptions in async callbacks invoked by built-ins (timers, fs/dns
/// callbacks, EventEmitter listeners) must not be silently swallowed. The
/// built-in invocation sites observe the discarded promise and report a
/// faulted one to stderr, flagging <see cref="Interpreter.HadUnhandledRejection"/>
/// so the CLI exits nonzero — Node's default unhandled-rejection behavior.
/// </summary>
/// <remarks>
/// Interpreter-only by construction: the tests need the interpreter's stderr
/// stream and <c>HadUnhandledRejection</c> flag, which the shared TestHarness
/// (stdout-only) does not expose. Compiled mode has its own callback dispatch
/// and is out of scope for #228.
/// </remarks>
public class UnhandledRejectionTests
{
    private static (string Stdout, string Stderr, bool HadUnhandledRejection) RunCapturingStderr(string source)
    {
        var task = Task.Run(() =>
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.ParseOrThrow();

            var checker = new TypeChecker();
            var typeMap = checker.Check(statements);

            using var interpreter = new Interpreter(stdout: stdout, stderr: stderr);
            interpreter.Interpret(statements, typeMap);

            return (stdout.ToString().Replace("\r\n", "\n"),
                    stderr.ToString().Replace("\r\n", "\n"),
                    interpreter.HadUnhandledRejection);
        });

        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "interpreter run timed out");
        return task.Result;
    }

    private static (string Stdout, string Stderr, bool HadUnhandledRejection) RunModuleCapturingStderr(string source)
    {
        var task = Task.Run(() =>
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();

            // In-memory virtual module fs (same pattern as TestHarness.RunModulesInterpreted);
            // the GUID base path is never created on disk — it just keys the virtual map.
            var virtualBase = Path.Combine(Path.GetTempPath(), $"sharpts_vfs_{Guid.NewGuid():N}");
            var entryPath = Path.GetFullPath(Path.Combine(virtualBase, "main.ts"));
            var virtualFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [entryPath] = source,
            };

            var resolver = new ModuleResolver(entryPath, virtualFiles);
            var entryModule = resolver.LoadModule(entryPath);
            var allModules = resolver.GetModulesInOrder(entryModule);

            var checker = new TypeChecker();
            var typeMap = TestHarness.CheckModulesOrThrow(checker, allModules, resolver);

            using var interpreter = new Interpreter(stdout: stdout, stderr: stderr);
            interpreter.InterpretModules(allModules, resolver, typeMap);

            return (stdout.ToString().Replace("\r\n", "\n"),
                    stderr.ToString().Replace("\r\n", "\n"),
                    interpreter.HadUnhandledRejection);
        });

        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "interpreter run timed out");
        return task.Result;
    }

    [Fact]
    public void AsyncTimerCallback_Throw_IsReportedAsUnhandledRejection()
    {
        var (stdout, stderr, flagged) = RunCapturingStderr("""
            setTimeout(async () => {
                throw new Error("boom from timer");
            }, 0);
            console.log("scheduled");
            """);

        Assert.Contains("scheduled", stdout);
        Assert.Contains("Unhandled promise rejection", stderr);
        Assert.Contains("boom from timer", stderr);
        Assert.True(flagged);
    }

    [Fact]
    public void AsyncTimerCallback_ThrowAfterAwait_IsReportedAsUnhandledRejection()
    {
        // The #207 shape: the rejection happens after a resume, where it used
        // to vanish without any output at all.
        var (_, stderr, flagged) = RunCapturingStderr("""
            setTimeout(async () => {
                await Promise.resolve();
                throw new Error("boom after await");
            }, 0);
            """);

        Assert.Contains("Unhandled promise rejection", stderr);
        Assert.Contains("boom after await", stderr);
        Assert.True(flagged);
    }

    [Fact]
    public void AsyncEventListener_Rejection_IsReportedAsUnhandledRejection()
    {
        var (stdout, stderr, flagged) = RunModuleCapturingStderr("""
            import { EventEmitter } from "events";
            const emitter = new EventEmitter();
            emitter.on("tick", async () => {
                throw new Error("boom from listener");
            });
            emitter.emit("tick");
            console.log("emitted");
            """);

        Assert.Contains("emitted", stdout);
        Assert.Contains("Unhandled promise rejection", stderr);
        Assert.Contains("boom from listener", stderr);
        Assert.True(flagged);
    }

    [Fact]
    public void AsyncTimerCallback_Success_IsNotReported()
    {
        var (stdout, stderr, flagged) = RunCapturingStderr("""
            setTimeout(async () => {
                await Promise.resolve();
                console.log("done");
            }, 0);
            """);

        Assert.Contains("done", stdout);
        Assert.DoesNotContain("Unhandled promise rejection", stderr);
        Assert.False(flagged);
    }

    [Fact]
    public void AsyncCallbackWithInternalCatch_IsNotReported()
    {
        var (stdout, stderr, flagged) = RunCapturingStderr("""
            setTimeout(async () => {
                try {
                    throw new Error("caught internally");
                } catch {
                    console.log("handled");
                }
            }, 0);
            """);

        Assert.Contains("handled", stdout);
        Assert.DoesNotContain("Unhandled promise rejection", stderr);
        Assert.False(flagged);
    }
}
