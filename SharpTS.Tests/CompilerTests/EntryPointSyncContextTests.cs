using System.Reflection;
using System.Threading;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Guards that every compiled entry point installs the emitted
/// <c>$EventLoopSyncContext</c> before any top-level statement runs (issues
/// #319/#320/#381). The install is what keeps a top-level <c>await fetch(...)</c>
/// continuation on the event-loop thread instead of escaping to the thread pool —
/// where it is invisible to the entry point's <c>WaitForTask</c> quiescence check
/// and the still-settling promise gets abandoned under pool pressure.
///
/// Verified structurally rather than by timing: the emitted <c>Main</c> installs the
/// context on its running thread and never restores it, so invoking <c>Main</c> and
/// reading <see cref="SynchronizationContext.Current"/> immediately afterward — on the
/// same thread — proves the install deterministically. A behavioral "did the
/// continuation escape" test would depend on inducing thread-pool starvation and is
/// inherently flaky (see the #295/#325 flake-class notes).
///
/// #381 specifically: the multi-module entry point (<c>EmitModulesEntryPoint</c>) was
/// the one entry point that omitted the install, so a multi-file program with a
/// top-level await lost the protection the single-file path already had.
/// </summary>
public class EntryPointSyncContextTests
{
    private const string EmittedSyncContextTypeName = "$EventLoopSyncContext";

    [Fact]
    public void SingleFileEntryPoint_InstallsEventLoopSyncContext()
    {
        var lexer = new Lexer("console.log('hi');");
        var parser = new Parser(lexer.ScanTokens());
        var statements = parser.ParseOrThrow();

        var checker = new TypeChecker();
        var typeMap = checker.Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);

        var compiler = new ILCompiler($"synccontext_single_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);

        AssertMainInstallsEventLoopSyncContext(compiler);
    }

    [Fact]
    public void MultiModuleEntryPoint_InstallsEventLoopSyncContext()
    {
        // Regression for #381: this entry point used to skip the install.
        var virtualBase = Path.Combine(Path.GetTempPath(), $"sharpts_vfs_{Guid.NewGuid():N}");
        var libPath = Path.GetFullPath(Path.Combine(virtualBase, "lib.ts"));
        var mainPath = Path.GetFullPath(Path.Combine(virtualBase, "main.ts"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [libPath] = "export const x = 42;\n",
            [mainPath] = "import { x } from './lib';\nconsole.log('entry ' + x);\n",
        };

        var resolver = new ModuleResolver(mainPath, files);
        var entryModule = resolver.LoadModule(mainPath);
        var modules = resolver.GetModulesInOrder(entryModule);

        var checker = new TypeChecker();
        var typeMap = TestHarness.CheckModulesOrThrow(checker, modules, resolver);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap)
            .Analyze(modules.SelectMany(m => m.Statements).ToList());

        var compiler = new ILCompiler($"synccontext_modules_{Guid.NewGuid():N}");
        compiler.CompileModules(modules, resolver, typeMap, deadCodeInfo);

        AssertMainInstallsEventLoopSyncContext(compiler);
    }

    /// <summary>
    /// Loads the compiled assembly, invokes <c>$Program.Main</c> on a dedicated thread,
    /// and asserts the emitted event-loop SynchronizationContext is current on that
    /// thread after Main returns. A dedicated thread (not a pool thread) keeps the
    /// leaked-by-design install off recycled threads, so the test cannot pollute siblings.
    /// </summary>
    private static void AssertMainInstallsEventLoopSyncContext(ILCompiler compiler)
    {
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var programType = assembly.GetType("$Program")
            ?? throw new InvalidOperationException("Compiled assembly has no $Program type");
        var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("$Program has no public static Main method");

        string? observedContextTypeName = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                mainMethod.Invoke(null, null);
                observedContextTypeName = SynchronizationContext.Current?.GetType().Name;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Compiled Main did not return within 30s.");

        Assert.Null(failure);
        Assert.Equal(EmittedSyncContextTypeName, observedContextTypeName);
    }
}
