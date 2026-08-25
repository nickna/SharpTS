using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// #858: a non-escaping <c>const NAME = (args) =&gt; …</c> local arrow invoked only by name is compiled
/// to a direct <c>callvirt Invoke</c> on the bare display-class instance (no per-call <c>$TSFunction</c>
/// wrapper / reflective <c>InvokeMethodValue</c> / arg boxing). These tests pin interpreter/compiler
/// parity across the optimized shape AND every escape case that must keep the generic wrapper path:
/// the arrow passed as an argument, returned, stored, recursive (captured), reassigned, or referenced
/// in a non-call position. Correctness — not the codegen shape — is what is asserted; the optimization
/// is transparent, so both modes must agree.
/// </summary>
public class NonEscapingArrowDirectCallTests
{
    [Fact]
    public void StableNumericLoopCapture_UsesUnboxedDisplayField()
    {
        const string source = """
            function work(n: number): number {
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const add = (a: number): number => a + i;
                    sum = sum + add(i);
                }
                return sum;
            }
            console.log(work(1000));
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(typeof(double), FindCaptureField(assembly, "i").FieldType);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("999000\n", TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void BodyMutatedLoopCapture_RetainsSharedObjectCell()
    {
        const string source = """
            function work(n: number): number {
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const read = (): number => i;
                    i = i + 1;
                    sum = sum + read();
                }
                return sum;
            }
            console.log(work(3));
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(typeof(object), FindCaptureField(assembly, "i").FieldType);
        Assert.Equal("4\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void NonLiteralLoopInitializer_RetainsObjectCaptureField()
    {
        const string source = """
            function work(n: number): number {
                const start: number = 0;
                let sum: number = 0;
                for (let i: number = start; i < n; i++) {
                    const add = (a: number): number => a + i;
                    sum = sum + add(i);
                }
                return sum;
            }
            console.log(work(10));
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(typeof(object), FindCaptureField(assembly, "i").FieldType);
        Assert.Equal("90\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void AsyncEnclosingFunction_RetainsObjectCaptureField()
    {
        const string source = """
            async function work(n: number): Promise<number> {
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const add = (a: number): number => a + i;
                    sum = sum + add(i);
                }
                return sum;
            }
            work(10).then((value: number): void => console.log(value));
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(typeof(object), FindCaptureField(assembly, "i").FieldType);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("90\n", TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void DirectEval_RetainsObjectCaptureField()
    {
        const string source = """
            function work(n: number): number {
                eval("");
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const add = (a: number): number => a + i;
                    sum = sum + add(i);
                }
                return sum;
            }
            console.log(work(10));
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(typeof(object), FindCaptureField(assembly, "i").FieldType);
    }

    [Theory, ModeData]
    public void CapturingArrow_InvokedByName_IsCorrect(ExecutionMode mode)
    {
        // The canonical #858 shape: arrow captures the loop var and is called directly each iteration.
        var source = """
            function work(n: number): number {
                let sum = 0;
                for (let i = 0; i < n; i++) {
                    const addCap = (a: number): number => a + i;
                    sum = sum + addCap(i);
                }
                return sum;
            }
            console.log(work(1000));
            """;
        Assert.Equal("999000\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturingArrow_MultipleTypedArgs_IsCorrect(ExecutionMode mode)
    {
        var source = """
            function work(n: number): number {
                let s = 0;
                for (let i = 0; i < n; i++) {
                    const mulCap = (a: number, b: number): number => a * b + i;
                    s = s + mulCap(2, 3);
                }
                return s;
            }
            console.log(work(1000));
            """;
        Assert.Equal("505500\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BooleanReturningArrow_InvokedByName_IsCorrect(ExecutionMode mode)
    {
        var source = """
            function count(n: number): number {
                let c = 0;
                for (let i = 0; i < n; i++) {
                    const isEvenCap = (a: number): boolean => (a + i) % 2 === 0;
                    if (isEvenCap(i)) { c = c + 1; }
                }
                return c;
            }
            console.log(count(10));
            """;
        // i + i is always even -> all 10 iterations counted.
        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void EscapingArrow_PassedAsArgument_KeepsWrapperSemantics(ExecutionMode mode)
    {
        var source = """
            function apply(f: (x: number) => number, v: number): number { return f(v); }
            function run(): number {
                const dbl = (a: number): number => a * 2;
                return apply(dbl, 21);
            }
            console.log(run());
            """;
        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void EscapingArrow_Returned_KeepsWrapperSemantics(ExecutionMode mode)
    {
        var source = """
            function makeAdder(i: number): (a: number) => number {
                const adder = (a: number): number => a + i;
                return adder;
            }
            console.log(makeAdder(10)(5));
            """;
        Assert.Equal("15\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void EscapingArrow_StoredInArray_KeepsWrapperSemantics(ExecutionMode mode)
    {
        var source = """
            function run(): number {
                const stored = (a: number): number => a + 1;
                const arr = [stored];
                return arr[0](9);
            }
            console.log(run());
            """;
        Assert.Equal("10\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RecursiveArrow_ViaName_KeepsWrapperSemantics(ExecutionMode mode)
    {
        // The arrow references itself by name from within its own (nested) body, so the name is
        // captured -> excluded from the optimization, but must still compute correctly.
        var source = """
            function run(): number {
                const fact = (k: number): number => k <= 1 ? 1 : k * fact(k - 1);
                return fact(5);
            }
            console.log(run());
            """;
        Assert.Equal("120\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReassignedBinding_IsDisqualified_AndCorrect(ExecutionMode mode)
    {
        var source = """
            function run(): number {
                let g = (a: number): number => a + 1;
                g = (a: number): number => a + 100;
                return g(7);
            }
            console.log(run());
            """;
        Assert.Equal("107\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SameNameInTwoScopes_IsDisqualified_AndCorrect(ExecutionMode mode)
    {
        // `dup` is declared in two functions; the whole-program single-declaration guard disqualifies
        // it, so both must fall back to the wrapper path and still compute correctly in both modes.
        var source = """
            function a(): number {
                const dup = (x: number): number => x + 1;
                return dup(1);
            }
            function b(): number {
                const dup = (x: number): number => x + 2;
                return dup(1);
            }
            console.log(a() + "," + b());
            """;
        Assert.Equal("2,3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NonCapturingArrow_InvokedByName_IsCorrect(ExecutionMode mode)
    {
        // Non-capturing arrows fall through to the wrapper path (no display class to key on) but
        // must remain correct.
        var source = """
            function run(): number {
                const idPlus = (x: number): number => x + 5;
                return idPlus(37);
            }
            console.log(run());
            """;
        Assert.Equal("42\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DirectCallArrow_CapturesAcrossIterations_FreshBinding(ExecutionMode mode)
    {
        // Per-iteration `let` capture: each arrow must see its own iteration's `i`. A broken
        // direct-call rewrite that hoisted/shared the display instance would compute a wrong sum.
        var source = """
            function run(): number {
                const results: number[] = [];
                for (let i = 0; i < 5; i++) {
                    const grab = (a: number): number => a + i * 10;
                    results.push(grab(1));
                }
                let total = 0;
                for (let j = 0; j < results.length; j++) { total = total + results[j]; }
                return total;
            }
            console.log(run());
            """;
        // (1+0)+(1+10)+(1+20)+(1+30)+(1+40) = 5 + 100 = 105
        Assert.Equal("105\n", TestHarness.Run(source, mode));
    }

    // ---- #858 follow-up: non-capturing arrows compile to a direct static `call` ----

    [Theory, ModeData]
    public void NonCapturingArrow_InLoop_IsCorrect(ExecutionMode mode)
    {
        // The main perf shape: a non-capturing arrow declared and called every iteration. Previously
        // rebuilt a $TSFunction wrapper per iteration; now emits nothing for the binding and a direct
        // static call. Must stay correct.
        var source = """
            function work(n: number): number {
                let sum = 0;
                for (let i = 0; i < n; i++) {
                    const idPlus = (x: number): number => x + 5;
                    sum = sum + idPlus(i);
                }
                return sum;
            }
            console.log(work(1000));
            """;
        // sum(i + 5) for i in 0..999 = (999*1000/2) + 5*1000 = 499500 + 5000 = 504500
        Assert.Equal("504500\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NonCapturingArrow_MultipleArgs_AndBooleanReturn(ExecutionMode mode)
    {
        var source = """
            function run(): number {
                const mulNC = (a: number, b: number): number => a * b;
                const isBigNC = (a: number): boolean => a > 10;
                let total = 0;
                for (let i = 0; i < 5; i++) {
                    const p = mulNC(i, 3);
                    if (isBigNC(p)) { total = total + p; }
                }
                return total;
            }
            console.log(run());
            """;
        // products: 0,3,6,9,12 -> only 12 > 10 -> total 12
        Assert.Equal("12\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NonCapturingAndCapturing_InSameFunction_BothCorrect(ExecutionMode mode)
    {
        // A non-capturing arrow (static call) and a capturing arrow (callvirt Invoke) coexist; the two
        // direct-call shapes must not interfere.
        var source = """
            function work(n: number): number {
                let sum = 0;
                for (let i = 0; i < n; i++) {
                    const constNC = (x: number): number => x * 2;
                    const capCAP = (x: number): number => x + i;
                    sum = sum + constNC(i) + capCAP(i);
                }
                return sum;
            }
            console.log(work(100));
            """;
        // sum over i in 0..99 of (i*2) + (i+i) = sum(4i) = 4 * (99*100/2) = 4 * 4950 = 19800
        Assert.Equal("19800\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NonCapturingArrow_NameAlsoAParameterElsewhere_StaysCorrect(ExecutionMode mode)
    {
        // Scope-collision guard for the non-capturing path: `dup` is an optimized non-capturing arrow in
        // run() AND a function parameter in callIt() that is itself called as `dup(3)`. The analyzer's
        // single-declaration guard counts only the const (params aren't counted), so `dup` IS optimized
        // in run() — but callIt()'s `dup(3)` must dispatch to ITS parameter, not run()'s static method.
        // The call site keys on the scope-managed tag, so callIt (which never tagged `dup`) takes the
        // generic value-call. A regression here would make callIt() return 3+1000=1003 instead of 6.
        var source = """
            function callIt(dup: (n: number) => number): number { return dup(3); }
            function run(): number {
                const dup = (x: number): number => x + 1000;
                return dup(5);
            }
            console.log(run() + "," + callIt((q: number): number => q * 2));
            """;
        Assert.Equal("1005,6\n", TestHarness.Run(source, mode));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"stable_numeric_capture_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static FieldInfo FindCaptureField(Assembly assembly, string name) =>
        Assert.Single(assembly.GetTypes()
            .SelectMany(type => type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)),
            field => field.Name == name
                && field.DeclaringType?.Name.Contains("DisplayClass", StringComparison.Ordinal) == true);
}
