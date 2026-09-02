using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>Regression coverage for stable typed destructuring loads (#1502).</summary>
public sealed class StableDestructuringLoadTests
{
    private const string HotSource = """
        type Point = { x: number; y: number };

        function arrayLoop(n: number): number {
            const pair: number[] = [1, 2];
            let total: number = 0;
            for (let i: number = 0; i < n; i++) {
                const [a, b] = pair;
                total = total + a + b;
            }
            return total;
        }

        function objectLoop(n: number): number {
            const point: Point = { x: 3, y: 4 };
            let total: number = 0;
            for (let i: number = 0; i < n; i++) {
                const { x, y } = point;
                total = total + x + y;
            }
            return total;
        }
        """;

    [Fact]
    public void StableArrayAndRecordBindings_AreAllocationFreePerIteration()
    {
        Assembly assembly = Compile(HotSource);
        var arrayLoop = FindFunction(assembly, "arrayLoop")
            .CreateDelegate<Func<double, double>>();
        var objectLoop = FindFunction(assembly, "objectLoop")
            .CreateDelegate<Func<double, double>>();

        Type pointCarrier = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("$CompactObjectRecord", StringComparison.Ordinal));
        Assert.Equal(
            [typeof(double), typeof(double)],
            pointCarrier
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => field.Name.StartsWith("_v", StringComparison.Ordinal))
                .Select(field => field.FieldType)
                .ToArray());

        Assert.Equal(300_000, arrayLoop(100_000));
        Assert.Equal(700_000, objectLoop(100_000));

        long before = GC.GetAllocatedBytesForCurrentThread();
        double arraySmallResult = arrayLoop(1_000);
        long arraySmallAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        double arrayLargeResult = arrayLoop(100_000);
        long arrayLargeAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        double objectSmallResult = objectLoop(1_000);
        long objectSmallAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        double objectLargeResult = objectLoop(100_000);
        long objectLargeAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(3_000, arraySmallResult);
        Assert.Equal(300_000, arrayLargeResult);
        Assert.Equal(7_000, objectSmallResult);
        Assert.Equal(700_000, objectLargeResult);
        // Runtime/JIT bookkeeping varies by platform, but allocations must not scale
        // with the 99,000 additional destructuring bindings.
        Assert.True(arrayLargeAllocated <= arraySmallAllocated + 1_024,
            $"Array destructuring allocations scaled: {arraySmallAllocated} vs {arrayLargeAllocated}.");
        Assert.True(objectLargeAllocated <= objectSmallAllocated + 1_024,
            $"Object destructuring allocations scaled: {objectSmallAllocated} vs {objectLargeAllocated}.");
        Assert.Empty(TestHarness.CompileAndVerifyOnly(HotSource));
    }

    [Theory, ModeData]
    public void DefaultsElisionsNestedPatternsRestAndHoles_RetainSemantics(ExecutionMode mode)
    {
        const string source = """
            const [first = 10, , [nested], ...rest] = [undefined, 2, [3], 4, 5];
            console.log(first, nested, rest.length, rest[0], rest[1]);
            """;

        Assert.Equal("10 3 2 4 5\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ObjectGetters_RetainSourceOrderAndSingleEvaluation(ExecutionMode mode)
    {
        const string source = """
            let trace: string = "";
            const source = {
                get x(): number { trace = trace + "x"; return 1; },
                get y(): number { trace = trace + "y"; return 2; }
            };
            const { x, y } = source;
            console.log(x, y, trace);
            """;

        Assert.Equal("1 2 xy\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StableObjectReduction_FractionalAndOverflowCasesRetainNumberSemantics(
        ExecutionMode mode)
    {
        const string source = """
            type Point = { x: number; y: number };

            function reduce(n: number, start: number, point: Point): number {
                let total: number = start;
                for (let i: number = 0; i < n; i++) {
                    const { x, y } = point;
                    total = total + x + y;
                }
                return total;
            }

            const fractional: Point = { x: 0.5, y: 0.25 };
            const integers: Point = { x: 1, y: 2 };
            console.log(reduce(2.5, 0.5, fractional));
            console.log(reduce(2, 9007199254740991, integers));
            console.log(Object.is(reduce(0, -0, integers), -0));
            """;

        Assert.Equal(
            "2.75\n9007199254740998\ntrue\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReassignedArraySymbolIterator_UsesIteratorProtocol(ExecutionMode mode)
    {
        const string source = """
            function* replacement(): Generator<number> { yield 9; yield 8; }
            const values: number[] = [1, 2];
            (values as any)[Symbol.iterator] = replacement;
            const [a, b] = values;
            console.log(a, b);
            """;

        Assert.Equal("9 8\n", TestHarness.Run(source, mode));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"stable_destructuring_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));
}
