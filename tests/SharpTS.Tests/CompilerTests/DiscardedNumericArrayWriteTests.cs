using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class DiscardedNumericArrayWriteTests
{
    [Fact]
    public void StableLoopWrite_DoesNotMaterializeDiscardedAssignmentResult()
    {
        Assembly assembly = Compile("""
            function fill(values: number[], n: number): void {
                for (let i: number = 0; i < n; i++) {
                    values[i] = i * 3;
                }
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "fill")).ToArray();
        int setDouble = Array.FindIndex(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "SetDouble" });

        Assert.True(setDouble >= 0, "Expected the numeric $Array SetDouble fast path.");
        string following = string.Join(", ", instructions
            .Skip(setDouble + 1)
            .Take(5)
            .Select(instruction => instruction.OpCode.Name));
        Assert.True(
            instructions[setDouble + 1].OpCode.FlowControl == FlowControl.Branch,
            "The discarded assignment must branch directly after SetDouble instead of reloading and boxing its value. " +
            $"Following IL: {following}");
    }

    [Fact]
    public void ObservedAssignment_RetainsResultProducingPath()
    {
        const string source = """
            function set(values: number[], index: number, value: number): number {
                return values[index] = value;
            }
            const values: number[] = [1, 2];
            console.log(set(values, 1, 7), values[1]);
            console.log(set(values, -1, 9), values.length);
            """;
        Assembly assembly = Compile(source);

        var instructions = ReadInstructions(FindFunction(assembly, "set")).ToArray();
        int setDouble = Array.FindIndex(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "SetDouble" });
        Assert.True(setDouble >= 0, "Expected the numeric $Array SetDouble fast path.");
        Assert.Equal("7 7\n9 2\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void DiscardedWrite_PreservesIndexBeforeValueEvaluation()
    {
        const string source = """
            let trace: string = "";
            function index(): number { trace = trace + "i"; return 0; }
            function value(): number { trace = trace + "v"; return 7; }
            function set(values: number[]): void {
                values[index()] = value();
            }
            const values: number[] = [];
            set(values);
            console.log(trace, values[0]);
            """;

        Assert.Equal("iv 7\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void GenericFallback_PreservesFractionalPropertyKey()
    {
        const string source = """
            function set(values: number[]): void {
                values[3.5] = 7;
            }
            const receiver: any = {};
            set(receiver as number[]);
            console.log(receiver[3.5], receiver[3]);
            """;

        Assert.Equal("7 undefined\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void ObservableDescriptors_DisableDiscardedWriteIntrinsic()
    {
        Assembly assembly = Compile("""
            Object.defineProperty([], '0', { value: 1 });
            function fill(values: number[], n: number): void {
                for (let i: number = 0; i < n; i++) {
                    values[i] = i * 3;
                }
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "fill")).ToArray();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "SetDouble" });
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "SetIndex" });
    }

    [Fact]
    public void GuardFallback_PreservesArrayLikeReceiverWrites()
    {
        const string source = """
            function fill(values: number[], n: number): void {
                for (let i: number = 0; i < n; i++) {
                    values[i] = i * 3;
                }
            }
            const receiver: any = {};
            fill(receiver as number[], 3);
            console.log(receiver[0], receiver[1], receiver[2]);
            """;

        Assert.Equal("0 3 6\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void StableLoopWrite_PassesIlVerification()
    {
        const string source = """
            function fill(values: number[], n: number): void {
                for (let i: number = 0; i < n; i++) {
                    values[i] = i * 3;
                }
            }
            const values: number[] = [];
            fill(values, 3);
            console.log(values.join(','));
            """;

        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1427_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> ReadInstructions(
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
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                operand = opCode.OperandType == OperandType.InlineMethod
                    ? module.ResolveMethod(token)
                    : module.ResolveType(token);
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
