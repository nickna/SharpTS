using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>Regression coverage for the allocation-free #1414 push slice.</summary>
public sealed class StableDiscardedArrayPushTests
{
    [Fact]
    public void DiscardedStablePush_UsesVoidGuardedHelperWithoutArgumentArrayOrBox()
    {
        Assembly assembly = Compile("""
            type Item = { value: number };
            function append(items: Item[], item: Item): void {
                items.push(item);
            }
            """);

        MethodInfo append = FindFunction(assembly, "append");
        var instructions = ReadInstructions(append).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodBase { Name: "ArrayPushOneDiscarded" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ArrayPushProto" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Newarr &&
            instruction.Operand is Type type && type == typeof(object));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box);
    }

    [Fact]
    public void ObservedPushResult_RetainsGeneralResultProducingPath()
    {
        Assembly assembly = Compile("""
            type Item = { value: number };
            function append(items: Item[], item: Item): number {
                return items.push(item);
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "append")).ToArray();
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ArrayPushProto" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ArrayPushOneDiscarded" });
    }

    [Fact]
    public void NumericPush_RetainsUnboxedArrayPath()
    {
        Assembly assembly = Compile("""
            function append(items: number[], value: number): void {
                items.push(value);
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "append")).ToArray();
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "PushDouble" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ArrayPushOneDiscarded" });
    }

    [Theory]
    [InlineData("Object.defineProperty([], '0', { value: 1 });")]
    [InlineData("const p = Array.prototype;")]
    [InlineData("Object.setPrototypeOf({}, null);")]
    [InlineData("const altered: Item[] = []; (altered as any).push = () => 0;")]
    [InlineData("Reflect.set([], 'push', () => 0);")]
    [InlineData("Object.assign([], { push: () => 0 });")]
    public void ObservableDescriptorOrPrototypeCode_DisablesDirectCall(string prelude)
    {
        Assembly assembly = Compile($$"""
            type Item = { value: number };
            {{prelude}}
            function append(items: Item[], item: Item): void {
                items.push(item);
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "append")).ToArray();
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ArrayPushProto" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ArrayPushOneDiscarded" });
    }

    [Fact]
    public void RuntimeGuard_PreservesGenericArrayLikeReceiverSemantics()
    {
        const string source = """
            type Item = { value: number };
            function append(items: Item[]): void {
                items.push({ value: 7 });
            }
            const receiver: any = { length: 0 };
            append(receiver as Item[]);
            console.log(receiver.length, receiver[0].value);
            """;

        Assert.Equal("1 7\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void StableDiscardedPush_PassesIlVerification()
    {
        const string source = """
            type Item = { value: number };
            function append(items: Item[], item: Item): void {
                items.push(item);
            }
            const items: Item[] = [];
            append(items, { value: 7 });
            console.log(items[0].value);
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.Empty(errors);
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1414_push_{Guid.NewGuid():N}");
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
