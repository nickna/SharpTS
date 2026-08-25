using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableClassScalarReplacementTests
{
    [Fact]
    public void ExactNonEscapingPrimitiveFields_UseShapeWithoutClassAllocation()
    {
        const string source = """
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            function construction(n: number): number {
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) {
                    const counter = new Counter(i);
                    sum = sum + counter.value;
                }
                return sum;
            }
            console.log(construction(10));
            """;

        Assembly assembly = Compile(source);
        var instructions = ReadInstructions(FindFunction(assembly, "construction")).ToArray();

        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Newobj
            && instruction.Operand is ConstructorInfo { DeclaringType.Name: "Counter" });
        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Initobj
            && instruction.Operand is Type type
            && type.Name.StartsWith("$Shape_", StringComparison.Ordinal));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double));

        Assert.Equal("45\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void PrimitiveArguments_AreEvaluatedLeftToRightBeforeScalarInitialization()
    {
        const string source = """
            let trace: string = "";
            function mark(value: number): number {
                trace = trace + value;
                return value;
            }
            class Entry {
                numberValue: number;
                booleanValue: boolean;
                stringValue: string;
                constructor(numberValue: number, booleanValue: boolean, stringValue: string) {
                    this.stringValue = stringValue;
                    this.numberValue = numberValue;
                    this.booleanValue = booleanValue;
                }
            }
            function read(): string {
                const entry = new Entry(mark(1), true, "ok");
                return entry.booleanValue ? entry.stringValue + entry.numberValue : "bad";
            }
            console.log(read(), trace);
            """;

        Assert.Equal("ok1 1\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));

        Assembly assembly = Compile(source);
        Assert.DoesNotContain(ReadInstructions(FindFunction(assembly, "read")), instruction =>
            instruction.OpCode == OpCodes.Newobj
            && instruction.Operand is ConstructorInfo { DeclaringType.Name: "Entry" });
    }

    [Fact]
    public void EscapesMutationAndConstructorEffects_RetainClassAllocation()
    {
        Assembly assembly = Compile("""
            let effects: number = 0;
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            class Effectful {
                value: number;
                constructor(value: number) {
                    effects = effects + 1;
                    this.value = value;
                }
            }
            function exact(): number {
                const exactCounter = new Counter(1);
                return exactCounter.value;
            }
            function escaped(): Counter {
                const escapedCounter = new Counter(2);
                return escapedCounter;
            }
            function mutated(): number {
                const mutatedCounter = new Counter(3);
                mutatedCounter.value = 4;
                return mutatedCounter.value;
            }
            function effectful(): number {
                const value = new Effectful(5);
                return value.value;
            }
            function incremented(): number {
                const incrementedCounter = new Counter(6);
                incrementedCounter.value++;
                return incrementedCounter.value;
            }
            """);

        Assert.DoesNotContain(ReadInstructions(FindFunction(assembly, "exact")), instruction =>
            IsNewobjOf(instruction, "Counter"));
        Assert.Contains(ReadInstructions(FindFunction(assembly, "escaped")), instruction =>
            IsNewobjOf(instruction, "Counter"));
        Assert.Contains(ReadInstructions(FindFunction(assembly, "mutated")), instruction =>
            IsNewobjOf(instruction, "Counter"));
        Assert.Contains(ReadInstructions(FindFunction(assembly, "effectful")), instruction =>
            IsNewobjOf(instruction, "Effectful"));
        Assert.Contains(ReadInstructions(FindFunction(assembly, "incremented")), instruction =>
            IsNewobjOf(instruction, "Counter"));
    }

    [Fact]
    public void PrototypeObservationAndDynamicDescriptors_DisableScalarReplacement()
    {
        Assembly prototypeAssembly = Compile("""
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            const observed = Counter.prototype;
            function read(): number {
                const counter = new Counter(1);
                return counter.value;
            }
            """);
        Assert.Contains(ReadInstructions(FindFunction(prototypeAssembly, "read")), instruction =>
            IsNewobjOf(instruction, "Counter"));

        Assembly descriptorAssembly = Compile("""
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            Object.defineProperty({}, "x", { value: 1 });
            function read(): number {
                const counter = new Counter(1);
                return counter.value;
            }
            """);
        Assert.Contains(ReadInstructions(FindFunction(descriptorAssembly, "read")), instruction =>
            IsNewobjOf(instruction, "Counter"));
    }

    private static bool IsNewobjOf(
        (OpCode OpCode, MemberInfo? Operand) instruction,
        string typeName) =>
        instruction.OpCode == OpCodes.Newobj
        && instruction.Operand is ConstructorInfo constructor
        && constructor.DeclaringType?.Name == typeName;

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"class_scalar_{Guid.NewGuid():N}");
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
