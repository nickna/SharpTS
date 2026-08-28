using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>Regression coverage for numeric-storage safety issue #1517.</summary>
public sealed class NumericStorageSafetyTests
{
    [Theory, ModeData]
    public void SwitchScopedShadow_IsNotPromotedAsOuterNumericCapture(ExecutionMode mode)
    {
        const string source = """
            function run(): string {
                let result: string = "";
                for (let i: number = 0; i < 1; i++) {
                    switch (i) {
                        case 0:
                            let i: string = "shadow";
                            const read = (): string => i;
                            result = read();
                            break;
                    }
                }
                return result;
            }
            console.log(run());
            """;

        AssertOutput(source, "shadow\n", mode);
    }

    [Theory, ModeData]
    public void GroupedDirectEval_DisablesNumericCapturePromotion(ExecutionMode mode)
    {
        const string source = """
            function run(): number {
                let total: number = 0;
                for (let i: number = 0; i < 2; i++) {
                    const read = (): number => i;
                    (eval)("0");
                    total = total + read();
                }
                return total;
            }
            console.log(run());
            """;

        AssertOutput(source, "1\n", mode);
        if (mode == ExecutionMode.Compiled)
            AssertCaptureFieldsHaveType(source, "i", typeof(object));
    }

    [Theory, ModeData]
    public void HoistedOuterBinding_WinsOverSameNamedNumericLoopLocal(ExecutionMode mode)
    {
        const string source = """
            async function run(n: number): Promise<number> {
                let i: number = 7;
                await Promise.resolve(0);
                let chain: Promise<number> = Promise.resolve(0);
                {
                    for (let i: number = 0; i < n; i++) {
                        chain = chain.then((sum: number): number => sum + i);
                    }
                }
                return (await chain) + i * 0;
            }
            run(3).then((value: number): void => console.log(value));
            """;

        AssertOutput(source, "3\n", mode);
    }

    [Theory, ModeData]
    public void ForOfRebinding_DisablesNumericAsyncParameterField(ExecutionMode mode)
    {
        const string source = """
            async function run(n: number): Promise<number> {
                let chain: Promise<number> = Promise.resolve(0);
                for (let i: number = 0; i < n; i++) {
                    chain = chain.then((sum: number): number => sum + i);
                }
                for (n of [1]) { }
                return await chain;
            }
            run(3).then((value: number): void => console.log(value));
            """;

        AssertOutput(source, "3\n", mode);
    }

    [Theory, ModeData]
    public void ForInRebinding_DisablesNumericAsyncParameterField(ExecutionMode mode)
    {
        const string source = """
            async function run(n: number): Promise<number> {
                let chain: Promise<number> = Promise.resolve(0);
                for (let i: number = 0; i < n; i++) {
                    chain = chain.then((sum: number): number => sum + i);
                }
                for (n in { only: 1 }) { }
                return await chain;
            }
            run(3).then((value: number): void => console.log(value));
            """;

        AssertOutput(source, "3\n", mode);
    }

    [Theory, ModeData]
    public void DuplicateAsyncParameterNames_DoNotCrashPromotionAnalysis(ExecutionMode mode)
    {
        const string source = """
            async function run(n: number, n: number): Promise<number> {
                let chain: Promise<number> = Promise.resolve(0);
                for (let i: number = 0; i < n; i++) {
                    chain = chain.then((sum: number): number => sum + i);
                }
                return await chain;
            }
            run(2, 3).then((value: number): void => console.log(value));
            """;

        AssertOutput(source, "3\n", mode);
    }

    [Theory, ModeData]
    public void TryCatchMergeLabels_ResetPrimitiveStackMetadata(ExecutionMode mode)
    {
        const string source = """
            function run(shouldThrow: boolean): string {
                try {
                    if (shouldThrow) throw "boom";
                    1;
                } catch {
                    2;
                }
                return "done";
            }
            console.log(run(false) + "," + run(true));
            """;

        AssertOutput(source, "done,done\n", mode);
    }

    [Theory, ModeData]
    public void NumericFunctionCaptureParameter_IsInitializedToDoubleField(ExecutionMode mode)
    {
        const string source = """
            function iterate(n: number): number {
                let current: number = 0;
                const iterable = {
                    [Symbol.iterator]() { return this; },
                    next() {
                        if (current < n) {
                            const value: number = current;
                            current = current + 1;
                            return { value, done: false };
                        }
                        return { value: 0, done: true };
                    }
                };
                let total: number = 0;
                for (const value of iterable) total = total + value;
                return total;
            }
            console.log(iterate(3));
            """;

        AssertOutput(source, "3\n", mode);
    }

    [Theory, ModeData]
    public void DefaultedNumericCapture_AssignmentBoxesForObjectParameterSlot(ExecutionMode mode)
    {
        const string source = """
            function iterate(n: number = 3): number {
                let current: number = 0;
                const iterable = {
                    [Symbol.iterator]() { return this; },
                    next() {
                        if (current < n) {
                            const value: number = current;
                            current = current + 1;
                            return { value, done: false };
                        }
                        return { value: 0, done: true };
                    }
                };
                n = n + 1;
                let total: number = 0;
                for (const value of iterable) total = total + value;
                return total;
            }
            console.log(iterate());
            """;

        AssertOutput(source, "6\n", mode);
    }

    [Theory, ModeData]
    public void AsyncCustomIteratorAccumulator_RetainsObjectCaptureStorage(ExecutionMode mode)
    {
        const string source = """
            async function iterate(n: number): Promise<number> {
                let current: number = 0;
                let total: number = 0;
                const iterable = {
                    [Symbol.iterator]() { return this; },
                    next() {
                        if (total === -1) return { value: 0, done: true };
                        if (current < n) {
                            const value: number = current;
                            current = current + 1;
                            return { value, done: false };
                        }
                        return { value: 0, done: true };
                    }
                };
                for (const value of iterable) total = total + value;
                return 3;
            }
            iterate(3).then((value: number): void => console.log(value));
            """;

        AssertOutput(source, "3\n", mode);
        if (mode == ExecutionMode.Compiled)
            AssertCaptureFieldsHaveType(source, "total", typeof(object));
    }

    private static void AssertOutput(string source, string expected, ExecutionMode mode)
    {
        if (mode == ExecutionMode.Compiled)
        {
            var errors = TestHarness.CompileAndVerifyOnly(source);
            Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        }
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    private static void AssertCaptureFieldsHaveType(string source, string name, Type expected)
    {
        var fields = Compile(source).GetTypes()
            .Where(type => type.Name.Contains("DisplayClass", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .Where(field => field.Name == name || field.Name.EndsWith("_" + name, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(fields);
        Assert.All(fields, field => Assert.Equal(expected, field.FieldType));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"numeric_storage_safety_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }
}
