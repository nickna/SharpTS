using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableClassFieldLoweringTests
{
    [Fact]
    public void PrimitiveFieldReadWrite_StaysTypedAndSkipsIntegrityProbe()
    {
        Assembly assembly = Compile("""
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            function bump(counter: Counter, next: number): number {
                counter.value = next;
                return counter.value + 1;
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "bump")).ToArray();
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "set_Value" });
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "get_Value" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "TryGetValue" });
    }

    [Fact]
    public void IntegrityMutationReference_RetainsFrozenObjectProbe()
    {
        Assembly assembly = Compile("""
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            function setValue(counter: Counter, next: number): number {
                counter.value = next;
                return counter.value;
            }
            const frozen = new Counter(1);
            Object.freeze(frozen);
            """);

        Assert.Contains(ReadInstructions(FindFunction(assembly, "setValue")), instruction =>
            instruction.Operand is MethodBase { Name: "TryGetValue" });
    }

    [Fact]
    public void FrozenFieldWrite_PreservesAssignmentResultAndStoredValue()
    {
        const string source = """
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            const counter = new Counter(1);
            Object.freeze(counter);
            console.log(counter.value = 5, counter.value);
            """;

        Assert.Equal("5 1\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void NumberBooleanAndStringFields_PassIlVerification()
    {
        const string source = """
            class Values {
                count: number = 1;
                ready: boolean = false;
                name: string = "a";
            }
            const values = new Values();
            values.count = values.count + 1;
            values.ready = true;
            values.name = values.name + "b";
            console.log(values.count, values.ready, values.name);
            """;

        Assert.Equal("2 true ab\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1455_{Guid.NewGuid():N}");
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
