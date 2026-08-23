using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Regression coverage for #1438: fresh, non-escaping intrinsic Promise chains
/// with inline numeric handlers use the typed continuation ABI. Every observable
/// or uncertain shape retains ordinary property / Promise resolution dispatch.
/// </summary>
public sealed class StablePrimitivePromiseThenTests
{
    private const string StableSource = """
        async function sumChain(n: number): Promise<number> {
            let chain: Promise<number> = Promise.resolve(0);
            for (let i: number = 0; i < n; i++) {
                chain = chain.then((sum: number): number => sum + i);
            }
            return await chain;
        }
        sumChain(10).then((value: number): void => console.log(value));
        """;

    [Theory, ModeData]
    public void StableNumericChain_PreservesResult(ExecutionMode mode)
    {
        Assert.Equal("45\n", TestHarness.Run(StableSource, mode));
    }

    [Theory, ModeData]
    public void StableNumericHandlerThrow_RejectsOnce(ExecutionMode mode)
    {
        const string source = """
            let calls: number = 0;
            async function run(): Promise<void> {
                let chain: Promise<number> = Promise.resolve(1);
                chain = chain.then((value: number): number => {
                    calls = calls + 1;
                    if (value === 1) throw new Error("boom");
                    return value;
                });
                try {
                    await chain;
                } catch (_error) {
                    console.log(calls);
                }
            }
            run();
            """;

        Assert.Equal("1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StableNumericChain_InImportedModulePreservesResult(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["chain.ts"] = """
                export async function sumChain(n: number): Promise<number> {
                    let chain: Promise<number> = Promise.resolve(0);
                    for (let i: number = 0; i < n; i++) {
                        chain = chain.then((sum: number): number => sum + i);
                    }
                    return await chain;
                }
                """,
            ["main.ts"] = """
                import { sumChain } from './chain';
                async function main(): Promise<void> {
                    console.log(await sumChain(10));
                }
                main();
                """
        };

        Assert.Equal("45\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void OwnThenOverride_RetainsValueDispatch(ExecutionMode mode)
    {
        const string source = """
            async function run(): Promise<void> {
                const promise: any = Promise.resolve(1);
                promise.then = (_handler: any): Promise<number> => Promise.resolve(99);
                console.log(await promise.then((value: number): number => value + 1));
            }
            run();
            """;

        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PromisePrototypeThenOverride_RetainsValueDispatch(ExecutionMode mode)
    {
        const string source = """
            async function run(): Promise<void> {
                const prototype: any = Promise.prototype;
                prototype.then = function (_handler: any): Promise<number> {
                    return Promise.resolve(77);
                };
                console.log(await Promise.resolve(1).then(
                    (value: number): number => value + 1));
            }
            run();
            """;

        Assert.Equal("77\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void StableNumericChain_UsesTypedContinuationWithoutDynamicCallbackArrays()
    {
        Assembly assembly = Compile(StableSource);
        MethodInfo caller = FindSingleCaller(assembly, "PromiseThenPrimitive");
        var instructions = ReadInstructions(caller).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "PromiseThenPrimitive" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "PromiseThen" or "PromiseResolveValue" or "InvokeCallback"
                    or "ObservePromiseConstructor" or "WrapDerivedPromiseResult"
            });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Newarr
            && instruction.Operand == typeof(object));

        MethodInfo continuationMoveNext = assembly.GetType("$PromiseThenPrimitive_SM")!
            .GetMethod("MoveNext")!;
        var continuationInstructions = ReadInstructions(continuationMoveNext).ToArray();
        Assert.Contains(continuationInstructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "Invoke",
                DeclaringType: { IsGenericType: true } declaringType
            }
            && declaringType.GetGenericTypeDefinition() == typeof(Func<,>));
        Assert.DoesNotContain(continuationInstructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "PromiseResolveValue" or "InvokeCallback"
            });
        Assert.DoesNotContain(continuationInstructions, instruction =>
            instruction.OpCode == OpCodes.Newarr
            && instruction.Operand == typeof(object));
    }

    [Fact]
    public void StableNumericChain_VerifiesIlAndStandaloneOutput()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(StableSource));
        Assert.Equal("45\n", TestHarness.RunCompiledStandalone(StableSource));
    }

    [Theory]
    [MemberData(nameof(FallbackSources))]
    public void UncertainShapes_RetainGeneralPromiseThen(string source)
    {
        Assembly assembly = Compile(source);
        Assert.Empty(FindCallers(assembly, "PromiseThenPrimitive"));
        Assert.NotEmpty(FindCallers(assembly, "PromiseThen"));
    }

    public static TheoryData<string> FallbackSources => new()
    {
        """
        async function work(): Promise<number> {
            let chain: Promise<number> = Promise.resolve(1);
            const alias: Promise<number> = chain;
            chain = chain.then((value: number): number => value + 1);
            await alias;
            return await chain;
        }
        work();
        """,
        """
        async function work(): Promise<number> {
            let chain: Promise<number> = Promise.resolve(1);
            chain = chain.then(
                (value: number): number => value + 1,
                (_error: any): number => 0);
            return await chain;
        }
        work();
        """,
        """
        async function work(): Promise<number> {
            let chain: Promise<number> = Promise.resolve(1);
            const next: Promise<{ value: number }> = chain.then(
                (value: number): { value: number } => ({ value: value + 1 }));
            return (await next).value;
        }
        work();
        """,
        """
        function increment(value: number): number { return value + 1; }
        async function work(): Promise<number> {
            let chain: Promise<number> = Promise.resolve(1);
            chain = chain.then(increment);
            return await chain;
        }
        work();
        """,
        """
        async function source(): Promise<number> { return 1; }
        async function work(): Promise<number> {
            let chain: Promise<number> = source();
            chain = chain.then((value: number): number => value + 1);
            return await chain;
        }
        work();
        """,
        """
        let chain: Promise<number> = Promise.resolve(1);
        chain = chain.then((value: number): number => value + 1);
        chain.then((value: number): void => console.log(value));
        """
    };

    [Fact]
    public void PromiseMutation_RetainsOrdinaryMethodValueDispatch()
    {
        const string source = """
            async function work(): Promise<number> {
                const promise: any = Promise.resolve(1);
                promise.then = (_handler: any): Promise<number> => Promise.resolve(9);
                return await promise.then((value: number): number => value + 1);
            }
            work();
            """;

        Assembly assembly = Compile(source);
        Assert.Empty(FindCallers(assembly, "PromiseThenPrimitive"));
        Assert.NotEmpty(FindCallers(assembly, "InvokeMethodValue"));
    }

    [Fact]
    public void PrimitiveResolve_UsesFreshCompletedTaskWithoutDynamicResolution()
    {
        Assembly assembly = Compile("""
            function make(): Promise<number> {
                return Promise.resolve(1);
            }
            """);

        MethodInfo caller = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static)
            .Single(method => method.Name.EndsWith("make", StringComparison.Ordinal));
        Assert.Contains(ReadInstructions(caller), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "FromResult",
                DeclaringType: { } declaringType
            }
            && declaringType == typeof(Task));
        Assert.DoesNotContain(ReadInstructions(caller), instruction =>
            instruction.Operand is MethodBase { Name: "PromiseResolve" });
    }

    [Fact]
    public void PromiseAllRuntime_HasNativeTaskArrayNormalizationPath()
    {
        Assembly assembly = Compile("""
            async function gather(): Promise<number[]> {
                return await Promise.all([Promise.resolve(1)]);
            }
            """);

        MethodInfo normalize = assembly.GetType("$Runtime")!
            .GetMethod("NormalizePromiseList")!;
        Assert.Contains(ReadInstructions(normalize), instruction =>
            instruction.OpCode == OpCodes.Newarr
            && instruction.Operand is Type type
            && type == typeof(Task<object?>));

        MethodInfo moveNext = assembly.GetType("$PromiseAll_SM")!
            .GetMethod("MoveNext")!;
        Assert.Contains(ReadInstructions(moveNext), instruction =>
            instruction.OpCode == OpCodes.Isinst
            && instruction.Operand is Type type
            && type == typeof(Task<object?>[]));
    }

    [Fact]
    public void StablePromiseAllFanOut_VerifiesIlAndStandaloneOutput()
    {
        const string source = """
            async function gather(): Promise<void> {
                const promises: Promise<number>[] = [];
                for (let i: number = 0; i < 4; i++) {
                    promises.push(Promise.resolve(i));
                }
                const values: number[] = await Promise.all(promises);
                console.log(values[0] + values[1] + values[2] + values[3]);
            }
            gather();
            """;

        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("6\n", TestHarness.RunCompiledStandalone(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1438_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindSingleCaller(Assembly assembly, string methodName) =>
        Assert.Single(FindCallers(assembly, methodName));

    private static List<MethodInfo> FindCallers(Assembly assembly, string methodName) =>
        assembly.GetTypes()
            .Where(type => type.Name != "$Runtime")
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance))
            .Where(method => method.GetMethodBody() != null)
            .Where(method => ReadInstructions(method).Any(instruction =>
                instruction.Operand is MethodBase called
                && called.Name == methodName))
            .ToList();

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> ReadInstructions(
        MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"Method '{method.Name}' has no IL body.");
        Module module = method.Module;
        Type[]? typeArguments = method.DeclaringType?.IsGenericType == true
            ? method.DeclaringType.GetGenericArguments()
            : null;
        Type[]? methodArguments = method.IsGenericMethod
            ? method.GetGenericArguments()
            : null;

        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : first;
            OpCode opCode = OpCodeByValue[value];
            MemberInfo? operand = null;
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                operand = opCode.OperandType == OperandType.InlineMethod
                    ? module.ResolveMethod(token, typeArguments, methodArguments)
                    : module.ResolveType(token, typeArguments, methodArguments);
            }

            int operandSize = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                    OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or
                    OperandType.InlineField or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or
                    OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch =>
                    4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException(
                    $"Unsupported IL operand type {opCode.OperandType}.")
            };
            offset += operandSize;
            yield return (opCode, operand);
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
}
