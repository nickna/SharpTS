using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class LocalRecordNumericArrayTests
{
    private const string Kernel = """
        interface Item { value: number; values: number[]; }
        function checksum(n: number): number {
            const items: Item[] = [];
            for (let i: number = 0; i < n; i++) items.push({ value: i, values: [i, i+1, i+2, i+3] });
            let sum: number = 0;
            for (let i: number = 0; i < items.length; i++) {
                const item: Item = items[i];
                sum = sum + item.value + item.values[0] + item.values[3];
            }
            return sum;
        }
        """;

    [Fact]
    public void LocalRecordKernel_ReducesAllocationWithoutChangingWork()
    {
        var statements = new Parser(new Lexer(Kernel).ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("NumericRecords" + Guid.NewGuid().ToString("N"));
        compiler.Compile(statements, map, new DeadCodeAnalyzer(map).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var fn = assembly.GetType("$Program")!.GetMethod("checksum")!.CreateDelegate<Func<double, double>>();
        for (int i = 0; i < 20; i++) Assert.Equal(1501500, fn(1000));
        long start = GC.GetAllocatedBytesForCurrentThread();
        double sum = fn(1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(1501500, sum);
        // Final records + numeric buffers + array identities + outer list growth.
        // The old boxed-element/copying path exceeds this by a wide margin.
        Assert.InRange(allocated, 100_000, 250_000);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(Kernel));
    }

    [Theory, ModeData]
    public void NumericAnnotation_DoesNotCoerceOpaqueLiteralElements(ExecutionMode mode)
    {
        const string source = """
            interface Input { value: number; }
            interface Item { value: number; values: number[]; }
            function read(input: Input): any {
                const items: Item[] = [];
                items.push({ value: 0, values: [input.value, 2] });
                const item = items[0];
                return item.values[0];
            }
            const input: any = { value: "changed" };
            console.log(read(input), typeof read(input));
            """;
        Assert.Equal("changed string\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("const value: number = 'changed' as any;", "changed string\n")]
    [InlineData("let value: number = 1; value = 'changed' as any;", "changed string\n")]
    [InlineData("let value: number = 1; value = undefined as any;", "undefined undefined\n")]
    [InlineData("for (let value: number = 0; value < 1; value++) {} const value: number = 'changed' as any;", "changed string\n")]
    public void BoxedNumericLocal_PreservesItsRuntimeValue(string declaration, string expected)
    {
        string source = "interface Item { value: number; values: number[]; } function read(): any {" + declaration + """
                const items: Item[] = [];
                items.push({ value: 0, values: [value, 2] });
                const item = items[0];
                return item.values[0];
            }
            console.log(read(), typeof read());
            """;
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory]
    [InlineData("delete item.values[0];", "undefined\n")]
    [InlineData("item.values[0]++;", "2\n")]
    [InlineData("++item.values[0];", "2\n")]
    public void MutationTargets_AreNotMistakenForIndexedReads(string mutation, string expected)
    {
        string source = """
            interface Item { value: number; values: number[]; }
            function read(): any {
                const items: Item[] = [];
                items.push({ value: 0, values: [1, 2] });
                const item = items[0];
            """ + mutation + "return item.values[0]; } console.log(read());";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements);
        Assert.Empty(new RuntimeFeatureDetector().Detect(statements, map).LocalRecordNumericArrays);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Interpreted));
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, ModeData]
    public void EscapingArray_PreservesIdentityAndDynamicMutation(ExecutionMode mode)
    {
        const string source = """
            interface Item { value: number; values: number[]; }
            function make(): number[] {
                const items: Item[] = [];
                items.push({ value: 1, values: [1, 2, 3, 4] });
                const item = items[0];
                return item.values;
            }
            const a = make(); const b: any = a;
            b[0] = "changed";
            console.log(a === b, a[0], a.length);
            delete b[1]; b.length = 6;
            console.log(1 in a, a[1], a.length);
            console.log(JSON.stringify(a));
            """;
        Assert.Equal("true changed 4\nfalse undefined 6\n[\"changed\",null,3,4,null,null]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericReads_PreserveSpecialNumbersAndInvalidIndices(ExecutionMode mode)
    {
        const string source = """
            interface Item { value: number; values: number[]; }
            function read(index: number): any {
                const items: Item[] = [];
                items.push({ value: 0, values: [-0, 0 / 0, 1 / 0, 3] });
                const item = items[0];
                return item.values[index];
            }
            console.log(Object.is(read(0), -0), Number.isNaN(read(1)), read(2));
            console.log(read(-1), read(0.5), read(4), read(4294967296), read(NaN), read(Infinity));
            """;
        Assert.Equal("true true Infinity\nundefined undefined undefined undefined undefined undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CapturedOrMutatedRecords_RetainGeneralArrayBehavior(ExecutionMode mode)
    {
        const string source = """
            interface Item { value: number; values: number[]; }
            function run(): number {
                const items: Item[] = [];
                items.push({ value: 1, values: [1, 2, 3] });
                const item = items[0];
                const mutate = () => { item.values[0] = 9; };
                mutate();
                const mapped = item.values.map((v: number) => v + 1);
                Object.defineProperty(item.values, "1", { get: () => 8 });
                return item.values[0] + item.values[1] + mapped[0];
            }
            console.log(run());
            """;
        Assert.Equal("27\n", TestHarness.Run(source, mode));
    }
}
