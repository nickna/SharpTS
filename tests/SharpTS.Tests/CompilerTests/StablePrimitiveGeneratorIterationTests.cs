using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StablePrimitiveGeneratorIterationTests
{
    private const string RangeSource = """
        function* numericRange(n: number): Generator<number> {
            for (let i: number = 0; i < n; i++) {
                yield i;
            }
        }

        function sumRange(n: number): number {
            let sum: number = 0;
            for (const value of numericRange(n)) {
                sum = sum + value;
            }
            return sum;
        }
        """;

    [Theory, ModeData]
    public void StableNumericRange_IsCorrect(ExecutionMode mode)
    {
        Assert.Equal("4999950000\n", TestHarness.Run(
            RangeSource + "\nconsole.log(sumRange(100000));", mode));
    }

    [Fact]
    public void StableNumericRange_PassesIlVerification()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(
            RangeSource + "\nconsole.log(sumRange(10));"));
    }

    [Fact]
    public void StableNumericRange_UsesNativeStateAndConsumerBridge()
    {
        Assembly assembly = Compile(RangeSource);
        MethodInfo consumer = FindFunction(assembly, "sumRange");
        Type stateMachine = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("<numericRange>d__", StringComparison.Ordinal));
        MethodInfo moveNext = stateMachine.GetMethod(
            "MoveNext", BindingFlags.Public | BindingFlags.Instance)!;

        Assert.Equal(typeof(double), stateMachine.GetField("n")!.FieldType);
        Assert.Equal(typeof(double), stateMachine.GetField("i")!.FieldType);
        Assert.Equal(typeof(double), stateMachine.GetField(
            "<>2__currentNumber", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType);

        var consumerCalls = CalledMethods(consumer).ToArray();
        Assert.Contains(consumerCalls, method => method.Name == "$moveNextForOf");
        Assert.Contains(consumerCalls, method => method.Name == "$getCurrentNumber");
        Assert.DoesNotContain(consumerCalls, method => method.Name is
            "next" or "GetIteratorDone" or "GetIteratorValue");
        var moveNextInstructions = Instructions(moveNext).ToArray();
        var numericBoxes = moveNextInstructions
            .Select((instruction, index) => (instruction, index))
            .Where(entry => entry.instruction.OpCode == OpCodes.Box
                && entry.instruction.Operand == typeof(double))
            .ToArray();
        Assert.True(numericBoxes.Length == 0,
            $"MoveNext contains numeric boxing at instruction(s): {string.Join(", ", numericBoxes.Select(entry => entry.index))}.\n"
            + string.Join("\n", moveNextInstructions.Select((instruction, index) =>
                $"{index}: {instruction.OpCode} {instruction.Operand}")));

        var run = consumer.CreateDelegate<Func<double, double>>();
        Assert.Equal(45, run(10));
        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = run(100_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(4_999_950_000, result);
        Assert.True(allocated <= 512,
            $"Native generator iteration allocated {allocated:N0} bytes.");
    }

    [Theory, ModeData]
    public void PublicNext_RetainsIteratorResultSemantics(ExecutionMode mode)
    {
        const string source = """
            function* numericRange(n: number): Generator<number> {
                for (let i: number = 0; i < n; i++) yield i;
            }
            const iterator = numericRange(2);
            const first = iterator.next();
            const second = iterator.next();
            const done = iterator.next();
            console.log(first.value, first.done, second.value, second.done,
                done.value === undefined, done.done);
            """;

        Assert.Equal("0 false 1 false true true\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void AliasedGeneratorCall_RetainsPublicProtocolPath()
    {
        Assembly assembly = Compile(RangeSource + """

            const makeRange = numericRange;
            function sumAliased(n: number): number {
                let sum: number = 0;
                for (const value of makeRange(n)) sum = sum + value;
                return sum;
            }
            """);

        var calls = CalledMethods(FindFunction(assembly, "sumAliased")).ToArray();
        Assert.Contains(calls, method => method.Name == "next");
        Assert.Contains(calls, method => method.Name == "GetIteratorDone");
        Assert.Contains(calls, method => method.Name == "GetIteratorValue");
        Assert.DoesNotContain(calls, method => method.Name == "$moveNextForOf");
    }

    [Theory, ModeData]
    public void ReassignedExportedGenerator_RetainsLiveBinding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["model.ts"] = """
                export function* values(n: number): Generator<number> {
                    for (let i: number = 0; i < n; i++) yield i;
                }
                export function sum(n: number): number {
                    let result: number = 0;
                    for (const value of values(n)) result = result + value;
                    return result;
                }
                export function replace(): void {
                    values = function* (n: number): Generator<number> {
                        for (let i: number = 0; i < n; i++) yield i + 100;
                    };
                }
                """,
            ["main.ts"] = """
                import { replace, sum } from "./model";
                replace();
                console.log(sum(2));
                """
        };

        Assert.Equal("201\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Fact]
    public void AbruptExit_StillClosesNativeGenerator()
    {
        const string source = """
            function* values(n: number): Generator<number> {
                try {
                    for (let i: number = 0; i < n; i++) yield i;
                } finally {
                    console.log("closed");
                }
            }
            for (const value of values(3)) {
                console.log(value);
                break;
            }
            """;

        Assert.Equal("0\nclosed\n", TestHarness.RunCompiled(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"native_generator_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static IEnumerable<MethodBase> CalledMethods(MethodInfo method) =>
        Instructions(method)
            .Where(instruction => instruction.Operand is MethodBase)
            .Select(instruction => (MethodBase)instruction.Operand!);

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> Instructions(
        MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"Method '{method.Name}' has no IL body.");
        Module module = method.Module;

        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : first;
            OpCode opCode = OpCodeByValue[value];
            MemberInfo? operand = null;
            if (opCode.OperandType is OperandType.InlineField or OperandType.InlineMethod
                or OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                operand = opCode.OperandType switch
                {
                    OperandType.InlineField => module.ResolveField(token),
                    OperandType.InlineMethod => module.ResolveMethod(token),
                    _ => module.ResolveType(token)
                };
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
