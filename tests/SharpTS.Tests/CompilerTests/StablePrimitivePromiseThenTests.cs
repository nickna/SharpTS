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
    public void StableNumericChain_ReturnsFreshFinalPromiseForEveryInvocation(
        ExecutionMode mode)
    {
        const string source = """
            function make(n: number): Promise<number> {
                let chain: Promise<number> = Promise.resolve(0);
                for (let i: number = 0; i < n; i++) {
                    chain = chain.then((value: number): number => value + 1);
                }
                return chain;
            }
            const first: any = make(0);
            const second: any = make(3);
            Promise.all([first, second]).then((values: number[]): void => {
                console.log(String(first === second));
                console.log(values.join(":"));
            });
            """;

        Assert.Equal("false\n0:3\n", TestHarness.Run(source, mode));
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

        MethodInfo runtimeEntry = assembly.GetType("$Runtime")!
            .GetMethod("PromiseThenPrimitive")!;
        var runtimeInstructions = ReadInstructions(runtimeEntry).ToArray();
        Assert.Contains(runtimeInstructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "Append",
                DeclaringType.Name: "$PrimitivePromiseChain"
            });

        MethodInfo runOne = assembly.GetType("$PrimitivePromiseChain")!
            .GetMethod("RunOne", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var continuationInstructions = ReadInstructions(runOne).ToArray();
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
                    or "AwaitUnsafeOnCompleted"
            });
        Assert.DoesNotContain(continuationInstructions, instruction =>
            instruction.OpCode == OpCodes.Newarr
            && instruction.Operand == typeof(object));
    }

    [Fact]
    public void StableAsyncLoop_KeepsCounterAndHandlerSnapshotUnboxed()
    {
        Assembly assembly = Compile(StableSource);
        Type stateMachine = Assert.Single(assembly.GetTypes(), type =>
            type.Name.StartsWith("<sumChain>d__", StringComparison.Ordinal));
        MethodInfo moveNext = stateMachine.GetMethod(
            "MoveNext",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Contains(moveNext.GetMethodBody()!.LocalVariables,
            local => local.LocalType == typeof(double));
        Assert.Contains(ReadInstructions(moveNext),
            instruction => instruction.OpCode == OpCodes.Clt);
        Assert.DoesNotContain(ReadInstructions(moveNext), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "JsLessThan" or "ConvertToNumber"
            });
        Assert.Equal(typeof(double), stateMachine.GetField("n")!.FieldType);

        FieldInfo capture = FindSingleDisplayClassField(assembly, "i");
        Assert.Equal(typeof(double), capture.FieldType);
    }

    [Fact]
    public void SuspendingPromiseLoop_RetainsObjectCounterSnapshot()
    {
        Assembly assembly = Compile("""
            async function sumChain(n: number): Promise<number> {
                let chain: Promise<number> = Promise.resolve(0);
                for (let i: number = 0; i < n; i++) {
                    await Promise.resolve(0);
                    chain = chain.then((sum: number): number => sum + i);
                }
                return await chain;
            }
            sumChain(3);
            """);

        FieldInfo capture = FindSingleDisplayClassField(assembly, "i");
        Assert.Equal(typeof(object), capture.FieldType);
    }

    [Fact]
    public void ReassignedLoopBound_RetainsObjectStateField()
    {
        Assembly assembly = Compile("""
            async function sumChain(n: number): Promise<number> {
                n = n + 0;
                let chain: Promise<number> = Promise.resolve(0);
                for (let i: number = 0; i < n; i++) {
                    chain = chain.then((sum: number): number => sum + i);
                }
                return await chain;
            }
            sumChain(3);
            """);

        Type stateMachine = Assert.Single(assembly.GetTypes(), type =>
            type.Name.StartsWith("<sumChain>d__", StringComparison.Ordinal));
        Assert.Equal(typeof(object), stateMachine.GetField("n")!.FieldType);
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
            const sibling: Promise<number> = chain.then(
                (value: number): number => value + 1);
            chain = chain.then((value: number): number => value + 2);
            await sibling;
            return await chain;
        }
        work();
        """,
        """
        function consume(_promise: Promise<number>): void {}
        async function work(): Promise<number> {
            let chain: Promise<number> = Promise.resolve(1);
            consume(chain = chain.then(
                (value: number): number => value + 1));
            return await chain;
        }
        work();
        """,
        """
        async function work(): Promise<number> {
            let chain: Promise<number> = Promise.resolve(1);
            chain = chain.then((value: number): number => value + 1);
            await chain;
            chain = chain.then((value: number): number => value + 1);
            return await chain;
        }
        work();
        """,
        """
        async function work(): Promise<number> {
            let chain: Promise<number> = Promise.resolve(1);
            chain = chain.then((value: number): number => value + 1);
            await chain;
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

    [Fact]
    public void StablePromiseAllFanOut_UsesPrimitiveResultCarrier()
    {
        Assembly assembly = Compile("""
            async function gather(n: number): Promise<number> {
                const promises: Promise<number>[] = [];
                for (let i: number = 0; i < n; i++) {
                    promises.push(Promise.resolve(i));
                }
                const values: number[] = await Promise.all(promises);
                let sum: number = 0;
                for (let i: number = 0; i < values.length; i++) {
                    sum = sum + values[i];
                }
                return sum;
            }
            gather(10);
            """);

        MethodInfo caller = FindSingleCaller(assembly, "PromiseAllPrimitive");
        var instructions = ReadInstructions(caller).ToList();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetIndex" or "GetLength" });
        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Castclass
            && instruction.Operand is Type type
            && type == typeof(List<double>));

        int itemRead = instructions.FindIndex(instruction =>
            instruction.Operand is MethodBase
            {
                Name: "get_Item",
                DeclaringType: { } declaringType
            }
            && declaringType == typeof(List<double>));
        Assert.True(itemRead >= 0);
        Assert.Equal(OpCodes.Add, instructions[itemRead + 1].OpCode);
    }

    [Fact]
    public void StablePromiseAllFanOut_AvoidsPerElementTasksAndWhenAll()
    {
        Assembly assembly = Compile("""
            async function gather(n: number): Promise<number> {
                const promises: Promise<number>[] = [];
                for (let i: number = 0; i < n; i++) {
                    promises.push(Promise.resolve(i));
                }
                const values: number[] = await Promise.all(promises);
                return values.length;
            }
            gather(10);
            """);

        MethodInfo caller = FindSingleCaller(assembly, "PromiseAllPrimitive");
        Assert.DoesNotContain(ReadInstructions(caller), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "FromResult" or "PromiseResolve"
            });
        Assert.Contains(ReadInstructions(caller), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "Add",
                DeclaringType: { } declaringType
            }
            && declaringType == typeof(List<double>));

        MethodInfo primitiveAll = assembly.GetType("$Runtime")!
            .GetMethod("PromiseAllPrimitive")!;
        var instructions = ReadInstructions(primitiveAll).ToList();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "WhenAll" or "get_Result" or
                    "FromResult" or "AdoptPromiseCombinatorResult" or
                    "MarkNonAutoAwaitPromise"
            });
        Assert.Single(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "QueuePromiseJob"
            });
    }

    [Fact]
    public void IntrinsicPromiseAllWithoutPrototypeMutation_AvoidsResultAdoptionFacade()
    {
        Assembly assembly = Compile("""
            function gather(promise: Promise<number>): Promise<number[]> {
                return Promise.all([promise]);
            }
            gather(new Promise<number>((resolve): void => resolve(1)));
            """);

        MethodInfo promiseAll = assembly.GetType("$Runtime")!
            .GetMethod("PromiseAll")!;
        Assert.DoesNotContain(ReadInstructions(promiseAll), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "AdoptPromiseCombinatorResult"
            });
    }

    [Fact]
    public void IntrinsicPromiseAllWithPrototypeMutation_RetainsResultAdoptionFacade()
    {
        Assembly assembly = Compile("""
            (Array.prototype as any).then = function (resolve: any): void {
                resolve("adopted");
            };
            function gather(promise: Promise<number>): Promise<number[]> {
                return Promise.all([promise]);
            }
            gather(new Promise<number>((resolve): void => resolve(1)));
            """);

        MethodInfo promiseAll = assembly.GetType("$Runtime")!
            .GetMethod("PromiseAll")!;
        Assert.Contains(ReadInstructions(promiseAll), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "AdoptPromiseCombinatorResult"
            });
    }

    [Theory, ModeData]
    public void StablePromiseAllFanOut_PreservesPromiseJobOrdering(ExecutionMode mode)
    {
        const string source = """
            async function gather(): Promise<void> {
                const promises: Promise<number>[] = [];
                promises.push(Promise.resolve(1));
                Promise.resolve(0).then((): void => console.log("queued"));
                const values: number[] = await Promise.all(promises);
                console.log("all:" + values.length);
            }
            gather();
            """;

        Assert.Equal("queued\nall:1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StablePromiseAllFanOut_PreservesEmptyInput(ExecutionMode mode)
    {
        const string source = """
            async function gather(): Promise<void> {
                const promises: Promise<number>[] = [];
                const values: number[] = await Promise.all(promises);
                console.log(values.length);
            }
            gather();
            """;

        Assert.Equal("0\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StablePromiseAllReductionLoop_PreservesOutput(ExecutionMode mode)
    {
        const string source = """
            async function gather(n: number): Promise<void> {
                const promises: Promise<number>[] = [];
                for (let i: number = 0; i < n; i++) {
                    promises.push(Promise.resolve(i));
                }
                const values: number[] = await Promise.all(promises);
                let sum: number = 0;
                for (let i: number = 0; i < values.length; i++) {
                    sum = sum + values[i];
                }
                console.log(sum);
            }
            gather(1000);
            """;

        Assert.Equal("499500\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StablePromiseAllFanOut_PreservesOutOfBoundsUndefined(ExecutionMode mode)
    {
        const string source = """
            async function gather(): Promise<void> {
                const promises: Promise<number>[] = [];
                promises.push(Promise.resolve(1));
                const values: number[] = await Promise.all(promises);
                console.log(String(values[4]));
            }
            gather();
            """;

        Assert.Equal("undefined\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StablePromiseAllFanOut_PreservesNonIndexNumericKeys(ExecutionMode mode)
    {
        const string source = """
            async function gather(): Promise<void> {
                const promises: Promise<number>[] = [];
                promises.push(Promise.resolve(1));
                const values: number[] = await Promise.all(promises);
                console.log(String(values[0.5]));
                console.log(String(values[NaN]));
                console.log(String(values[-1]));
                console.log(String(values[Infinity]));
                console.log(values[-0]);
            }
            gather();
            """;

        Assert.Equal(
            "undefined\nundefined\nundefined\nundefined\n1\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void RuntimeUncertainPromiseAllElement_RetainsOrdinaryResult()
    {
        const string source = """
            function uncertain(): number {
                return undefined as any;
            }
            async function gather(): Promise<void> {
                const promises: Promise<number>[] = [];
                promises.push(Promise.resolve(uncertain()));
                const values: number[] = await Promise.all(promises);
                console.log(String(values[0]));
            }
            gather();
            """;

        Assembly assembly = Compile(source);
        Assert.Empty(FindCallers(assembly, "PromiseAllPrimitive"));
        Assert.NotEmpty(FindCallers(assembly, "PromiseAll"));
        Assert.Equal("undefined\n", TestHarness.RunCompiledStandalone(source));
    }

    [Theory]
    [InlineData("const alias: Promise<number>[] = promises;")]
    [InlineData("consume(values);")]
    public void ObservablePromiseAllShapes_RetainOrdinaryResult(string observableUse)
    {
        string source = $$"""
            function consume(_value: number[]): void {}
            async function gather(): Promise<number> {
                const promises: Promise<number>[] = [];
                promises.push(Promise.resolve(1));
                {{(observableUse.Contains("alias", StringComparison.Ordinal) ? observableUse : "")}}
                const values: number[] = await Promise.all(promises);
                {{(observableUse.Contains("values", StringComparison.Ordinal) ? observableUse : "")}}
                return values.length;
            }
            gather();
            """;

        Assembly assembly = Compile(source);
        Assert.Empty(FindCallers(assembly, "PromiseAllPrimitive"));
        Assert.NotEmpty(FindCallers(assembly, "PromiseAll"));
    }

    [Fact]
    public void ArrayPrototypePushMutation_RetainsOrdinaryPromiseAllPath()
    {
        const string source = """
            (Array.prototype as any).push = function(value: any): number {
                return 0;
            };
            async function gather(): Promise<number> {
                const promises: Promise<number>[] = [];
                promises.push(Promise.resolve(1));
                const values: number[] = await Promise.all(promises);
                return values.length;
            }
            gather();
            """;

        Assembly assembly = Compile(source);
        Assert.Empty(FindCallers(assembly, "PromiseAllPrimitive"));
        Assert.NotEmpty(FindCallers(assembly, "PromiseAll"));
    }

    [Fact]
    public void InlinePromiseExecutor_UsesTypedDirectInvocation()
    {
        const string source = """
            function create(): Promise<number> {
                return new Promise<number>((resolve: any, reject: any): void => {
                    resolve(7);
                });
            }
            create().then((value: number): void => console.log(value));
            """;

        Assembly assembly = Compile(source);
        MethodInfo caller = FindSingleCaller(assembly, "PromiseFromDirectExecutor");
        Assert.DoesNotContain(ReadInstructions(caller), instruction =>
            instruction.Operand is MethodBase { Name: "PromiseFromExecutor" });

        MethodInfo direct = assembly.GetType("$Runtime")!
            .GetMethod("PromiseFromDirectExecutor")!;
        Assert.Contains(ReadInstructions(direct), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "Invoke",
                DeclaringType: { IsGenericType: true } declaringType
            }
            && declaringType.GetGenericTypeDefinition() == typeof(Func<,,>));
        Assert.DoesNotContain(ReadInstructions(direct), instruction =>
            instruction.Operand is MethodBase { Name: "InvokeMethodValue" });
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("7\n", TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void EscapedPromiseExecutor_RetainsGeneralCallableDispatch()
    {
        const string source = """
            const executor: any = (resolve: any, reject: any): void => resolve(3);
            const promise: Promise<number> = new Promise<number>(executor);
            promise.then((value: number): void => console.log(value));
            """;

        Assembly assembly = Compile(source);
        Assert.Empty(FindCallers(assembly, "PromiseFromDirectExecutor"));
        Assert.NotEmpty(FindCallers(assembly, "PromiseFromExecutor"));
        Assert.Equal("3\n", TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void CapturingPromiseExecutor_RetainsGeneralCallableDispatch()
    {
        const string source = """
            async function main(): Promise<void> {
                const expected = 9;
                const actual = await new Promise<number>((resolve: any, reject: any): void => {
                    setTimeout(() => resolve(expected), 0);
                });
                console.log(actual);
            }
            main();
            """;

        Assembly assembly = Compile(source);
        Assert.Empty(FindCallers(assembly, "PromiseFromDirectExecutor"));
        Assert.NotEmpty(FindCallers(assembly, "PromiseFromExecutor"));
        Assert.Equal("9\n", TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void StableNumericTwoHandler_UsesCompactTypedReaction()
    {
        const string source = """
            function work(): Promise<number> {
                let chain: Promise<number> = Promise.resolve(1);
                chain = chain.then(
                    (value: number): number => value + 1,
                    (_error: any): number => 0,
                );
                return chain;
            }
            work().then((value: number): void => console.log(value));
            """;

        Assembly assembly = Compile(source);
        MethodInfo caller = FindSingleCaller(
            assembly, "PromiseThenPrimitiveWithRejection");
        Assert.DoesNotContain(ReadInstructions(caller), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "PromiseThen" or "InvokeCallback" or "PromiseResolveValue"
            });

        MethodInfo moveNext = assembly
            .GetType("$PromiseThenPrimitiveWithRejection_SM")!
            .GetMethod("MoveNext")!;
        Assert.Contains(ReadInstructions(moveNext), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "Invoke",
                DeclaringType: { IsGenericType: true } declaringType
            }
            && declaringType.GetGenericTypeDefinition() == typeof(Func<,>));
        Assert.DoesNotContain(ReadInstructions(moveNext), instruction =>
            instruction.Operand is MethodBase
            {
                Name: "InvokeCallback" or "PromiseResolveValue"
            });
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("2\n", TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void StableNumericTwoHandler_PreservesRejectionThrowAndJobOrdering()
    {
        const string source = """
            const events: string[] = [];
            function makeFulfilled(): Promise<number> {
                let chain: Promise<number> = Promise.resolve(1);
                chain = chain.then(
                    (value: number): number => {
                        events.push("fulfilled:" + value);
                        return value + 1;
                    },
                    (_error: any): number => {
                        events.push("unexpected-rejection");
                        return 0;
                    },
                );
                return chain;
            }
            const fulfilled: Promise<number> = makeFulfilled();

            events.push("sync");
            queueMicrotask((): void => {
                events.push("microtask");
            });

            function makeRecovered(): Promise<number> {
                let chain: Promise<number> = Promise.resolve(
                    Promise.reject("source-error")) as any as Promise<number>;
                chain = chain.then(
                    (value: number): number => value,
                    (error: any): number => {
                        events.push("rejected:" + error);
                        return 7;
                    },
                );
                return chain;
            }
            const recovered: Promise<number> = makeRecovered();

            let sameReactionRejectCalls: number = 0;
            function makeThrown(): Promise<number> {
                let chain: Promise<number> = Promise.resolve(1);
                chain = chain.then(
                    (_value: number): number => {
                        throw new Error("handler-error");
                    },
                    (_error: any): number => {
                        sameReactionRejectCalls = sameReactionRejectCalls + 1;
                        return 9;
                    },
                );
                return chain;
            }
            const thrown: Promise<number> = makeThrown();
            const checkedThrown: Promise<number> = thrown.then(
                (_value: number): number => 0,
                (error: any): number => {
                    events.push("thrown:" + error.message);
                    events.push("same-reject:" + sameReactionRejectCalls);
                    return 0;
                },
            );

            function makeRejectedThrow(): Promise<number> {
                let chain: Promise<number> = Promise.resolve(
                    Promise.reject("reject-source")) as any as Promise<number>;
                chain = chain.then(
                    (value: number): number => value,
                    (error: any): number => {
                        throw new Error("reject-handler-" + error);
                    },
                );
                return chain;
            }
            const rejectedThrow: Promise<number> = makeRejectedThrow();
            const checkedRejectedThrow: Promise<number> = rejectedThrow.then(
                (_value: number): number => 0,
                (error: any): number => {
                    events.push("reject-thrown:" + error.message);
                    return 0;
                },
            );

            Promise.all([
                fulfilled,
                recovered,
                checkedThrown,
                checkedRejectedThrow,
            ]).then(
                (values: any): void => {
                    events.push("values:" + values.join(":"));
                    queueMicrotask((): void => console.log(events.join("|")));
                },
            );
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(4, FindCallers(
            assembly, "PromiseThenPrimitiveWithRejection").Count);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal(
            "sync|fulfilled:1|microtask|rejected:source-error|" +
            "thrown:handler-error|same-reject:0|" +
            "reject-thrown:reject-handler-reject-source|values:2:7:0:0\n",
            TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void StableTwoHandlerObjectResult_RetainsGeneralAdoption()
    {
        const string source = """
            function work(): Promise<number> {
                return (
                    Promise.resolve(Promise.reject("source-error"))
                        as any as Promise<number>
                ).then(
                    (value: number): number => value + 1,
                    (_error: any): any => ({
                        then: (resolve: any): void => resolve(9),
                    }),
                );
            }
            work().then((value: number): void => console.log(value));
            """;

        Assembly assembly = Compile(source);
        Assert.Empty(FindCallers(
            assembly, "PromiseThenPrimitiveWithRejection"));
        Assert.NotEmpty(FindCallers(assembly, "PromiseThen"));
        Assert.Equal("9\n", TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void DirectPromiseAllNumericReaction_UsesCompactObjectPrimitivePath()
    {
        const string source = """
            let resolveFirst: any;
            let resolveSecond: any;
            const first: Promise<number> = new Promise<number>((resolve: any): void => {
                resolveFirst = resolve;
            });
            const second: Promise<number> = new Promise<number>((resolve: any): void => {
                resolveSecond = resolve;
            });

            function sum(promises: Promise<number>[]): Promise<number> {
                return (Promise.all(promises) as Promise<any>).then(
                    (values: any): number => values[0] + values[1],
                );
            }
            const result: Promise<number> = sum([first, second]);
            result.then(
                (value: number): void => console.log("result:" + value),
                (error: any): void => console.log("unexpected:" + error),
            );

            resolveFirst(2);
            queueMicrotask((): void => {
                console.log("after-first");
                resolveSecond(3);
            });
            console.log("sync");
            """;

        Assembly assembly = Compile(source);
        Assert.NotEmpty(FindCompactObjectPrimitiveCallers(assembly));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal(
            "sync\nafter-first\nresult:5\n",
            TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void DirectPromiseAllNumericReaction_PropagatesRejectionAndThrow()
    {
        const string source = """
            function one(promises: Promise<number>[]): Promise<number> {
                return (Promise.all(promises) as Promise<any>).then(
                    (values: any): number => values.length,
                );
            }
            function boom(promises: Promise<number>[]): Promise<number> {
                return (Promise.all(promises) as Promise<any>).then(
                    (values: any): number => {
                        if (values.length > 0) {
                            throw new Error("handler");
                        }
                        return values.length;
                    },
                );
            }

            one([Promise.reject("input")]).then(
                (_value: number): void => console.log("unexpected-input"),
                (error: any): void => console.log("rejected:" + error),
            );

            boom([Promise.resolve(1)]).then(
                (_value: number): void => console.log("unexpected-handler"),
                (error: any): void => console.log("threw:" + error.message),
            );
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(2, FindCompactObjectPrimitiveCallers(assembly).Count);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal(
            "rejected:input\nthrew:handler\n",
            TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void DirectPromiseAllObjectReaction_RetainsThenableAdoption()
    {
        const string source = """
            function adopt(promises: Promise<number>[]): Promise<number> {
                return (Promise.all(promises) as Promise<any>).then(
                    (values: any): any => ({
                        then: (resolve: any): void => resolve(values[0] + 1),
                    }),
                );
            }
            adopt([Promise.resolve(2)]).then(
                (value: number): void => console.log(value),
            );
            """;

        Assembly assembly = Compile(source);
        Assert.Empty(FindCompactObjectPrimitiveCallers(assembly));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("3\n", TestHarness.RunCompiledStandalone(source));
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

    private static FieldInfo FindSingleDisplayClassField(
        Assembly assembly,
        string name) =>
        Assert.Single(
            assembly.GetTypes().SelectMany(type =>
                type.Name.Contains("DisplayClass", StringComparison.Ordinal)
                    ? type.GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static)
                    : []),
            field => field.Name == name);

    private static MethodInfo FindSingleCaller(Assembly assembly, string methodName) =>
        Assert.Single(FindCallers(assembly, methodName));

    private static List<MethodInfo> FindCompactObjectPrimitiveCallers(Assembly assembly) =>
        FindCallers(assembly, "PromiseThenObjectPrimitive");

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
