using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Regression coverage for #1451. A deliberately narrow async function with no
/// suspension points and a primitive result completes through a typed core and fresh Task wrapper.
/// Immediately consumed intrinsic primitive resolves can join that core; observable or uncertain
/// shapes retain the ordinary async state machine.
/// </summary>
public sealed class SuspensionFreePrimitiveAsyncTests
{
    private const string EligibleSource = """
        async function identity(value: number): Promise<number> {
            return value;
        }
        identity(7).then((value: number): void => console.log(value));
        """;

    [Theory, ModeData]
    public void PrimitiveResult_PreservesFulfillment(ExecutionMode mode)
    {
        Assert.Equal("7\n", TestHarness.Run(EligibleSource, mode));
    }

    [Theory, ModeData]
    public void ReturnExpression_RunsSynchronouslyBeforeCallReturns(ExecutionMode mode)
    {
        const string source = """
            let bodyRan: boolean = false;
            function mark(value: number): number {
                bodyRan = true;
                return value;
            }
            async function identity(value: number): Promise<number> {
                return mark(value);
            }
            const result: Promise<number> = identity(9);
            console.log(bodyRan);
            result.then((value: number): void => console.log(value));
            """;

        Assert.Equal("true\n9\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SynchronousThrow_BecomesRejectedPromise(ExecutionMode mode)
    {
        const string source = """
            function fail(): number {
                throw new Error("boom");
            }
            async function work(): Promise<number> {
                return fail();
            }
            let returned: boolean = false;
            let promise: Promise<number>;
            try {
                promise = work();
                returned = true;
            } catch (_error) {
                console.log("escaped");
                promise = Promise.resolve(0);
            }
            console.log(returned);
            promise.catch((error: Error): void => console.log(error.message));
            """;

        Assert.Equal("true\nboom\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void EveryInvocation_ReturnsFreshPromise(ExecutionMode mode)
    {
        const string source = """
            async function identity(value: number): Promise<number> {
                return value;
            }
            const first: any = identity(1);
            const second: any = identity(1);
            console.log(first === second);
            Promise.all([first, second]).then(
                (values: number[]): void => console.log(values.join(":")));
            """;

        Assert.Equal("false\n1:1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void Reactions_RemainAsynchronousJobs(ExecutionMode mode)
    {
        const string source = """
            async function identity(value: number): Promise<number> {
                return value;
            }
            const order: string[] = ["start"];
            identity(1).then((_value: number): void => {
                order.push("then");
                console.log(order.join(":"));
            });
            order.push("after");
            console.log(order.join(":"));
            """;

        Assert.Equal(
            "start:after\nstart:after:then\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ImmediatelyAwaitedDirectCall_PreservesResult(ExecutionMode mode)
    {
        const string source = """
            async function identity(value: number): Promise<number> {
                return value;
            }
            async function run(value: number): Promise<number> {
                return await identity(value);
            }
            run(12).then((value: number): void => console.log(value));
            """;

        Assert.Equal("12\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void ReassignedBinding_DoesNotUseTypedCore()
    {
        const string source = """
            async function identity(value: number): Promise<number> {
                return value;
            }
            async function replacement(value: number): Promise<number> {
                return value + 10;
            }
            identity = replacement;
            async function run(): Promise<number> {
                return await identity(1);
            }
            run();
            """;

        Assembly assembly = Compile(source);
        MethodInfo moveNext = assembly.GetTypes()
            .Single(type => type.Name.Contains("<run>d__", StringComparison.Ordinal))
            .GetMethod("MoveNext")!;
        var calls = ReadInstructions(moveNext)
            .Select(instruction => instruction.Operand)
            .OfType<MethodBase>()
            .ToArray();

        Assert.DoesNotContain(calls, method => method.Name.Contains(
            "$asyncCore$identity", StringComparison.Ordinal));
        Assert.Contains(calls, method => method.Name == "identity");
    }

    [Theory, ModeData]
    public void LocallyShadowedBinding_RetainsValueCall(ExecutionMode mode)
    {
        const string source = """
            async function identity(value: number): Promise<number> {
                return value;
            }
            async function run(): Promise<number> {
                const identity = (value: number): Promise<number> =>
                    Promise.resolve(value + 20);
                return await identity(1);
            }
            run().then((value: number): void => console.log(value));
            """;

        Assert.Equal("21\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MixedDirectAndRealAwaits_PreserveStateNumbering(ExecutionMode mode)
    {
        const string source = """
            async function identity(value: number): Promise<number> {
                return value;
            }
            async function run(value: number): Promise<number> {
                const first: number = await identity(value);
                const second: number = await Promise.resolve(first + 1);
                return await identity(second + 1);
            }
            run(5).then((value: number): void => console.log(value));
            """;

        Assert.Equal("7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DirectCoreThrow_IsRejectedByOuterWrapper(ExecutionMode mode)
    {
        const string source = """
            function fail(): number {
                throw new Error("nested boom");
            }
            async function work(): Promise<number> {
                return fail();
            }
            async function run(): Promise<number> {
                return await work();
            }
            run().catch((error: Error): void => console.log(error.message));
            """;

        Assert.Equal("nested boom\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void EligibleFunction_UsesCompletedAndFaultedTasksWithoutStateMachine()
    {
        Assembly assembly = Compile(EligibleSource);
        MethodInfo identity = FindFunction(assembly, "identity");
        var instructions = ReadInstructions(identity).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "FromResult",
                DeclaringType: { } declaringType
            }
            && declaringType == typeof(Task));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "FromException",
                DeclaringType: { } declaringType
            }
            && declaringType == typeof(Task));
        Assert.DoesNotContain(assembly.GetTypes(), type =>
            type.Name.Contains("<identity>d__", StringComparison.Ordinal));
        Assert.DoesNotContain(
            identity.GetCustomAttributesData(),
            attribute => attribute.AttributeType.Name == "AsyncStateMachineAttribute");
    }

    [Fact]
    public void ImmediatelyAwaitedDirectCall_UsesTypedCoreWithoutTaskCall()
    {
        Assembly assembly = Compile("""
            async function identity(value: number): Promise<number> {
                return value;
            }
            async function run(value: number): Promise<number> {
                return await identity(value);
            }
            run(1);
            """);

        MethodInfo runCore = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.Contains(
                "$asyncCore$run", StringComparison.Ordinal));
        var calls = ReadInstructions(runCore)
            .Select(instruction => instruction.Operand)
            .OfType<MethodBase>()
            .ToArray();

        Assert.Contains(calls, method => method.Name.Contains(
            "$asyncCore$identity", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, method => method.Name == "identity");
        Assert.DoesNotContain(assembly.GetTypes(), type =>
            type.Name.Contains("<run>d__", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(FallbackSources))]
    public void UncertainShapes_RetainAsyncStateMachine(string source, string functionName)
    {
        Assembly assembly = Compile(source);

        Assert.Contains(assembly.GetTypes(), type =>
            type.Name.Contains($"<{functionName}>d__", StringComparison.Ordinal));
    }

    public static TheoryData<string, string> FallbackSources => new()
    {
        {
            """
            async function suspended(value: any): Promise<any> {
                return await Promise.resolve(value);
            }
            suspended(1);
            """,
            "suspended"
        },
        {
            """
            async function objectResult(): Promise<{ value: number }> {
                return { value: 1 };
            }
            objectResult();
            """,
            "objectResult"
        },
        {
            """
            async function defaulted(value: number = 1): Promise<number> {
                return value;
            }
            defaulted();
            """,
            "defaulted"
        },
    };

    [Fact]
    public void PrimitiveLocalsAndDirectCoreAwaits_ElideOuterStateMachine()
    {
        const string source = """
            async function identity(value: number): Promise<number> {
                return value;
            }
            async function sum(count: number): Promise<number> {
                let total: number = 0;
                for (let index: number = 0; index < count; index++) {
                    total = total + await identity(index);
                }
                return total;
            }
            sum(5).then((value: number): void => console.log(value));
            """;

        Assembly assembly = Compile(source);
        Assert.DoesNotContain(assembly.GetTypes(), type =>
            type.Name.Contains("<sum>d__", StringComparison.Ordinal));
        Assert.Equal("10\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void ImmediatelyAwaitedPrimitiveResolve_ElidesStateMachineAndCompletedTasks()
    {
        const string source = """
            async function sum(count: number): Promise<number> {
                let total: number = 0;
                for (let index: number = 0; index < count; index++) {
                    total = total + await Promise.resolve(index);
                }
                return total;
            }
            sum(5).then((value: number): void => console.log(value));
            """;

        var parsed = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var parsedSum = parsed.OfType<Stmt.Function>()
            .Single(function => function.Name.Lexeme == "sum");
        var totalDeclaration = parsedSum.Body!.OfType<Stmt.Var>()
            .Single(statement => statement.Name.Lexeme == "total");
        var inspectedTypeMap = new TypeChecker().Check(parsed);
        var loop = parsedSum.Body!.OfType<Stmt.For>().Single();
        var assignment = ((Stmt.Block)loop.Body).Statements
            .OfType<Stmt.Expression>()
            .Select(statement => statement.Expr)
            .OfType<Expr.Assign>()
            .Single();
        var addition = Assert.IsType<Expr.Binary>(assignment.Value);
        Assert.IsType<SharpTS.TypeSystem.TypeInfo.Primitive>(
            inspectedTypeMap.Get(addition.Left));
        Assert.IsType<SharpTS.TypeSystem.TypeInfo.Primitive>(
            inspectedTypeMap.Get(addition.Right));
        Assert.IsType<SharpTS.TypeSystem.TypeInfo.Primitive>(
            inspectedTypeMap.Get(addition));
        Assert.False(inspectedTypeMap.IsUndefinedReachableNumericLocal(totalDeclaration));
        Assert.False(inspectedTypeMap.IsUndefinedReachableNumericLocal(
            totalDeclaration.Initializer!));

        Assembly assembly = Compile(source);
        Assert.DoesNotContain(assembly.GetTypes(), type =>
            type.Name.Contains("<sum>d__", StringComparison.Ordinal));
        MethodInfo core = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.Contains(
                "$asyncCore$sum", StringComparison.Ordinal));
        var instructions = ReadInstructions(core).ToArray();
        var calls = instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodBase>()
            .ToArray();

        Assert.DoesNotContain(calls, method =>
            method.Name == "FromResult" && method.DeclaringType == typeof(Task));
        Assert.DoesNotContain(calls, method => method.Name == "PrepareHostedAwait");
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box);
        Assert.Equal("10\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void ShadowedPromiseResolve_DoesNotElideAwait()
    {
        const string source = """
            async function work(Promise: any): Promise<number> {
                return await Promise.resolve(1);
            }
            const replacement = {
                resolve(value: number): Promise<number> {
                    return globalThis.Promise.resolve(value + 20);
                }
            };
            work(replacement).then((value: number): void => console.log(value));
            """;

        Assembly assembly = Compile(source);
        MethodInfo moveNext = assembly.GetTypes()
            .Single(type => type.Name.Contains("<work>d__", StringComparison.Ordinal))
            .GetMethod("MoveNext")!;
        var calls = ReadInstructions(moveNext)
            .Select(instruction => instruction.Operand)
            .OfType<MethodBase>()
            .ToArray();

        Assert.Contains(calls, method =>
            method.Name == "FromResult" && method.DeclaringType == typeof(Task));
    }

    [Fact]
    public void PromiseMutation_RetainsCompletedTaskAwait()
    {
        const string source = """
            (Promise as any).resolve = (value: number): Promise<number> =>
                new Promise<number>((resolve): void => resolve(value + 30));
            async function work(): Promise<number> {
                return await Promise.resolve(1);
            }
            work().then((value: number): void => console.log(value));
            """;

        Assembly assembly = Compile(source);
        MethodInfo moveNext = assembly.GetTypes()
            .Single(type => type.Name.Contains("<work>d__", StringComparison.Ordinal))
            .GetMethod("MoveNext")!;
        var calls = ReadInstructions(moveNext)
            .Select(instruction => instruction.Operand)
            .OfType<MethodBase>()
            .ToArray();

        Assert.Contains(calls, method =>
            method.Name == "FromResult" && method.DeclaringType == typeof(Task));
    }

    [Fact]
    public void EligibleFunction_VerifiesIlAndStandaloneOutput()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(EligibleSource));
        Assert.Equal("7\n", TestHarness.RunCompiledStandalone(EligibleSource));
    }

    [Fact]
    public void ImmediatelyAwaitedDirectCall_VerifiesIlAndStandaloneOutput()
    {
        const string source = """
            async function identity(value: number): Promise<number> {
                return value;
            }
            async function run(value: number): Promise<number> {
                return await identity(value);
            }
            run(12).then((value: number): void => console.log(value));
            """;

        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("12\n", TestHarness.RunCompiledStandalone(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1451_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method =>
                method.Name.EndsWith(name, StringComparison.Ordinal)
                && !method.Name.StartsWith("$asyncCore$", StringComparison.Ordinal));

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
