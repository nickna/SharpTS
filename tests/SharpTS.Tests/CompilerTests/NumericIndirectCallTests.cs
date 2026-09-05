using System.Reflection;
using SharpTS.Tests.Infrastructure;
using Xunit;
using static SharpTS.Tests.CompilerTests.StableNumericHotPathTests;

namespace SharpTS.Tests.CompilerTests;

public class NumericIndirectCallTests
{
    private const string AlternatingSource = """
        function add(...v: number[]): number { return v[0] + v[1] + v[2] + v[3]; }
        function extra(...v: number[]): number { return v[0] + v[1] + v[2] + v[3] + 1; }
        function mutate(...v: number[]): number { v[0]++; return v[0] + v[1] + v[2] + v[3]; }
        function first(): (...v: number[]) => number { return add; }
        function second(): (...v: number[]) => number { return extra; }
        function third(): (...v: number[]) => number { return mutate; }
        function run(n: number, first: (...v: number[]) => number, second: (...v: number[]) => number): number {
            let sum: number = 0.5;
            for (let i: number = 0; i < n; i++) {
                const fn = i % 2 === 0 ? first : second;
                sum = sum + fn(i, 1, 2, 3);
            }
            return sum;
        }
        """;

    [Fact]
    public void IndirectOnlyTargets_AlternateWithoutPerCallAllocationAndKeepFallback()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(AlternatingSource));
        var assembly = Compile(AlternatingSource);
        var field = assembly.GetType("$TSFunction")!.GetField("_numericRest4", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object first = FindFunction(assembly, "first").Invoke(null, null)!;
        // Exercise the two-argument constructor as well as GetOrCreate's
        // constructor with cached name/length used for the first target.
        object second = Activator.CreateInstance(assembly.GetType("$TSFunction")!,
            [null, FindFunction(assembly, "extra")])!;
        object third = FindFunction(assembly, "third").Invoke(null, null)!;
        Assert.NotNull(field.GetValue(first));
        Assert.NotNull(field.GetValue(second));
        Assert.Null(field.GetValue(third));
        Assert.Same(first, ((Delegate)field.GetValue(first)!).Target);
        Assert.Same(second, ((Delegate)field.GetValue(second)!).Target);
        var method = FindFunction(assembly, "run");
        Assert.Contains(ReadInstructions(method), i => i.Operand is MethodBase { Name: "InvokeMethodValue" });
        Assert.Contains(ReadInstructions(method), i => i.Operand is MethodBase called
            && called.DeclaringType == typeof(Func<double, double, double, double, double>));
        var run = method.CreateDelegate<Func<double, object, object, double>>();
        for (int i = 0; i < 30; i++) run(1000, first, second);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double actual = run(10000, first, second);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(50060000.5, actual);
        Assert.Equal(0, allocated);
        Assert.Equal(actual, run(10000, first, third));
    }

    [Theory, ModeData]
    public void CapturesCalleeBeforeArgumentsReplaceItsBinding(ExecutionMode mode)
    {
        const string source = """
            function first(...v: number[]): number { return v[0] + v[1] + v[2] + v[3]; }
            function second(...v: number[]): number { return v[0] + v[1] + v[2] + v[3] + 100; }
            let fn: (...v: number[]) => number = first;
            let trace = "";
            function argument(value: number): number { trace = trace + value; fn = second; return value; }
            function run(): number { return fn(argument(1), 2, 3, 4); }
            console.log(run(), fn(1, 2, 3, 4), trace);
            """;
        Assert.Equal("10 110 1\n", TestHarness.Run(source, mode));
        if (mode == ExecutionMode.Compiled)
        {
            Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
            Assert.Equal("10 110 1\n", TestHarness.RunCompiledStandalone(source));
        }
    }

    [Theory, ModeData]
    public void FallbackPreservesArgumentsBoundFunctionsCapturesAndRegularParameters(ExecutionMode mode)
    {
        const string source = """
            function add(...v: number[]): number { return v[0] + v[1] + v[2] + v[3]; }
            function observe(...v: number[]): number { return arguments.length + v.length; }
            function defaults(prefix: number = 2, ...v: number[]): number { return prefix + v.length; }
            function fixed(a: number, b: number, c: number, d: number): number { return a * b + c * d; }
            function choose(fn: (...v: number[]) => number): number { return fn(1, 2, 3, 4); }
            function capture(value: number): (...v: number[]) => number {
                return (...v: number[]): number => value + v[0];
            }
            const bound = add.bind(null, 10);
            console.log(choose(observe), choose(defaults), choose(fixed), choose(capture(5)), choose(bound));
            """;
        Assert.Equal("8 4 14 6 16\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void UnsupportedArgumentsAndAritiesRetainRawValuesAndRestLength(ExecutionMode mode)
    {
        const string source = """
            function raw(...v: number[]): number { return typeof v[0] === "string" ? 1 : 0; }
            function length(...v: number[]): number { return v.length; }
            function foreign(fn: (...v: number[]) => number, value: any): number { return fn(value, 1, 2, 3); }
            function lengths(fn: (...v: number[]) => number): void { console.log(fn(1, 2, 3), fn(1, 2, 3, 4, 5)); }
            console.log(foreign(raw, "x"));
            lengths(length);
            """;
        Assert.Equal("1\n3 5\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void IndirectCapabilities_RespectCompilationBudgetAndFallBackWhenExhausted()
    {
        string declarations = string.Join("\n", Enumerable.Range(0, 65)
            .Select(i => $"function target{i}(...v: number[]): number {{ return v[0]; }}"));
        var assembly = Compile(declarations + """

            function last(): (...v: number[]) => number { return target64; }
            function run(fn: (...v: number[]) => number): number { return fn(9, 1, 2, 3); }
            """);
        Assert.Equal(64, assembly.GetType("$Program")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Count(m => m.Name.Contains("$rest$arity")));
        Assert.Equal(64, assembly.GetType("$Program")!.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Count(m => m.Name.EndsWith("$numericRest4<Entry>")));
        var target = FindFunction(assembly, "last").Invoke(null, null)!;
        var field = assembly.GetType("$TSFunction")!.GetField("_numericRest4", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Null(field.GetValue(target));
        Assert.Equal(9d, FindFunction(assembly, "run").CreateDelegate<Func<object, double>>()(target));
    }

    [Theory, ModeData]
    public void NativeArguments_PreserveSpecialNumbersAndAbruptEvaluation(ExecutionMode mode)
    {
        const string source = """
            function first(...v: number[]): number { return v[0]; }
            let later = 0;
            function fail(): number { throw new Error("stop"); }
            function mark(): number { later++; return 2; }
            function run(fn: (...v: number[]) => number): void {
                const zero: number = fn(-0, 0, 0, 0);
                const nan: number = fn(NaN, 1, 2, 3);
                const inf: number = fn(Infinity, 1, 2, 3);
                console.log(1 / zero, Number.isNaN(nan), inf);
                try { fn(fail(), mark(), 3, 4); } catch (e) { console.log(e.message, later); }
            }
            run(first);
            """;
        Assert.Equal("-Infinity true Infinity\nstop 0\n", TestHarness.Run(source, mode));
    }
}
