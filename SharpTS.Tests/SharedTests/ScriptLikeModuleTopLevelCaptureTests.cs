using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Top-level <c>let</c>/<c>var</c>/<c>const</c> bindings of a <em>script-like</em> module
/// (one with no imports or exports) must be reachable from declarations in that module
/// when it is compiled through the multi-module pipeline.
/// </summary>
/// <remarks>
/// <para>
/// A script-like module shares the global scope, so
/// <c>AnalyzeCapturedTopLevelVarsAcrossModules</c> registers its captured top-level vars
/// under the <c>null</c> (shared) bucket rather than a per-module one, and
/// <c>EmitScriptInit</c> nulls <c>CurrentPath</c> to match. Any other per-module walk that
/// resolves top-level storage has to normalize the same way — that is what
/// <c>NormalizeToEmissionPath</c> is for.
/// </para>
/// <para>
/// Phases 5, 7, and 8 used <c>module.Path</c> raw, so function declarations were defined
/// and emitted against a per-module bucket their captures did not live in. Reads failed at
/// runtime with <c>ReferenceError: Undefined variable</c> and writes emitted IL the CLR
/// rejected outright (<c>InvalidProgramException</c>). Arrow functions were unaffected —
/// Phase 6 and the body emitters already normalized — which is what made this look narrower
/// than it was. (#1282)
/// </para>
/// <para>
/// Every case here runs through <c>RunModules</c>, which always drives
/// <c>CompileModules</c>; a single-file script compiled via the CLI takes the separate
/// script path and never showed the bug. That also means this defect sat under the whole
/// test suite's compiled-mode coverage: any test whose fixture mutated a top-level
/// <c>let</c> from a function hit it.
/// </para>
/// </remarks>
public class ScriptLikeModuleTopLevelCaptureTests
{
    private static void Expect(string source, string expected, ExecutionMode mode)
    {
        var files = new Dictionary<string, string> { ["main.ts"] = source };
        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal(Normalize(expected), Normalize(output));
    }

    // Both sides need normalizing, not just the output: these source files are checked out
    // with CRLF on Windows, so the expected raw-string literals carry \r\n of their own.
    private static string Normalize(string s) =>
        string.Join("\n", s.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim();

    [Theory, ModeData]
    public void FunctionDeclaration_ReadsTopLevelLet(ExecutionMode mode)
    {
        // Previously: ReferenceError: Undefined variable 'v'.
        Expect("""
            let v = 7;
            function show(): void { console.log('v=' + v); }
            show();
            """, "v=7", mode);
    }

    [Theory, ModeData]
    public void FunctionDeclaration_MutatesTopLevelLet(ExecutionMode mode)
    {
        // Previously: InvalidProgramException, for each of the three write forms.
        Expect("""
            let a = 0;
            let b = 0;
            let c = 0;
            function bump(): void { a++; b += 2; c = c + 3; }
            bump();
            bump();
            console.log(a + ',' + b + ',' + c);
            """, "2,4,6", mode);
    }

    [Theory, ModeData]
    public void FunctionDeclaration_MutatesTopLevelVarAndString(ExecutionMode mode)
    {
        Expect("""
            var n = 0;
            let s = 'hi';
            const tag = '!';
            function go(): void { n++; s = s + tag; }
            go();
            go();
            console.log(n + ' ' + s);
            """, "2 hi!!", mode);
    }

    /// <summary>
    /// Function declarations hoist, so a function defined above the binding it captures
    /// still has to resolve to the same storage once called.
    /// </summary>
    [Theory, ModeData]
    public void HoistedFunction_CapturesLaterDeclaredTopLevelLet(ExecutionMode mode)
    {
        Expect("""
            function bump(): void { count++; }
            let count = 0;
            bump();
            bump();
            bump();
            console.log('count=' + count);
            """, "count=3", mode);
    }

    [Theory, ModeData]
    public void ClassMethod_CapturesTopLevelLet(ExecutionMode mode)
    {
        Expect("""
            let hits = 0;
            class Counter {
                hit(): void { hits++; }
                total(): number { return hits; }
            }
            const c = new Counter();
            c.hit();
            c.hit();
            console.log('hits=' + c.total());
            """, "hits=2", mode);
    }

    /// <summary>
    /// Arrows already worked (Phase 6 normalized), and a real module already worked
    /// (it never enters the shared bucket). Both are kept so a future change to the
    /// normalization can't fix one shape by breaking another.
    /// </summary>
    [Theory, ModeData]
    public void ArrowAndRealModule_StillCaptureTopLevelLet(ExecutionMode mode)
    {
        Expect("""
            let viaArrow = 0;
            const bump = (): void => { viaArrow++; };
            bump();
            console.log('arrow=' + viaArrow);
            """, "arrow=1", mode);

        Expect("""
            export {};
            let inModule = 0;
            function bump(): void { inModule++; }
            bump();
            console.log('module=' + inModule);
            """, "module=1", mode);
    }

    /// <summary>
    /// Two script-like modules genuinely share one global scope, so a function in the
    /// entry file must see a binding the imported-by-side-effect script declared.
    /// </summary>
    [Theory, ModeData]
    public void FunctionDeclaration_MutatesAcrossScriptMergedFiles(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let total = 0;
                function add(n: number): void { total += n; }
                add(4);
                add(5);
                console.log('total=' + total);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("total=9", output.Replace("\r\n", "\n").Trim());
    }
}
