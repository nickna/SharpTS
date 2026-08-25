using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableNumericHotPathTests
{
    private const string OptimizedSource = """
        function add4(...values: number[]): number {
            return values[0] + values[1] + values[2] + values[3];
        }
        function run(n: number): number {
            let total: number = 0;
            for (let i: number = 0; i < n; i++) {
                total += add4(i, 1, 2, 3);
            }
            return total;
        }
        console.log(run(3));
        """;

    [Theory, ModeData]
    public void NumericCompoundAndFixedRestCall_AreCorrect(ExecutionMode mode)
    {
        Assert.Equal("21\n", TestHarness.Run(OptimizedSource, mode));
    }

    [Fact]
    public void NumericCompoundAndFixedRestCall_PassIlVerification()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(OptimizedSource));
    }

    [Fact]
    public void NumericCompoundLocal_UsesNativeDoubleArithmetic()
    {
        Assembly assembly = Compile(OptimizedSource);
        var instructions = ReadInstructions(FindFunction(assembly, "run")).ToArray();

        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Add);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ConvertToNumber" or "Add" });
    }

    [Fact]
    public void StableFixedNumericRestCall_UsesFlattenedTypedCompanion()
    {
        Assembly assembly = Compile(OptimizedSource);
        MethodInfo caller = FindFunction(assembly, "run");
        MethodInfo companion = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.Contains("add4$rest$arity4", StringComparison.Ordinal));

        var callerInstructions = ReadInstructions(caller).ToArray();
        Assert.Contains(callerInstructions, instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodBase called
            && called.MetadataToken == companion.MetadataToken);
        Assert.DoesNotContain(callerInstructions, instruction =>
            instruction.OpCode == OpCodes.Newarr && instruction.Operand == typeof(object));
        Assert.DoesNotContain(callerInstructions, instruction =>
            instruction.Operand is MethodBase { Name: "CreateArray" });

        Assert.All(companion.GetParameters(), parameter =>
            Assert.Equal(typeof(double), parameter.ParameterType));
        Assert.DoesNotContain(ReadInstructions(companion), instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double));
        Assert.DoesNotContain(ReadInstructions(companion), instruction =>
            instruction.Operand is MethodBase { Name: "GetIndex" or "GetLength" });

        var compiled = caller.CreateDelegate<Func<double, double>>();
        Assert.Equal(5000550000, compiled(100000));
        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = compiled(100000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(5000550000, result);
        Assert.Equal(0, allocated);
    }

    [Theory, ModeData]
    public void DynamicRestUses_RetainOrdinaryArraySemantics(ExecutionMode mode)
    {
        const string source = """
            function pick(index: number, ...values: number[]): number {
                values[0] = values[0] + 10;
                return values[index] + values.length;
            }
            const args: number[] = [1, 2, 3];
            console.log(pick(1, ...args));
            """;

        Assert.Equal("5\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void DynamicRestUse_DoesNotDefineFlattenedCompanion()
    {
        Assembly assembly = Compile("""
            function pick(index: number, ...values: number[]): number {
                return values[index];
            }
            function run(): number { return pick(1, 1, 2, 3); }
            """);

        Assert.DoesNotContain(
            assembly.GetType("$Program")!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static),
            method => method.Name.Contains("$rest$arity", StringComparison.Ordinal));
    }

    [Theory, ModeData]
    public void RegularNumericParametersAndRestLength_AreCorrect(ExecutionMode mode)
    {
        const string source = """
            function weighted(prefix: number, ...values: number[]): number {
                return prefix + values[0] + values.length;
            }
            function run(): number { return weighted(10, 20, 30); }
            console.log(run());
            """;

        Assert.Equal("32\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void AnyTypedRestArgument_UsesOrdinaryPackingAtThatCallSite()
    {
        Assembly assembly = Compile("""
            function add(...values: number[]): number {
                return values[0] + values[1];
            }
            function runNumeric(): number { return add(1, 2); }
            function runDynamic(value: any): number { return add(value, 2); }
            """);

        MethodInfo companion = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.Contains("add$rest$arity2", StringComparison.Ordinal));
        Assert.Contains(ReadInstructions(FindFunction(assembly, "runNumeric")), instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodBase called
            && called.MetadataToken == companion.MetadataToken);
        Assert.Contains(ReadInstructions(FindFunction(assembly, "runDynamic")), instruction =>
            instruction.Operand is MethodBase { Name: "CreateArray" });
    }

    [Fact]
    public void DynamicCompoundAssignment_RetainsRuntimeAdd()
    {
        Assembly assembly = Compile("""
            function append(value: any): any {
                let result: any = 1;
                result += value;
                return result;
            }
            """);

        Assert.Contains(ReadInstructions(FindFunction(assembly, "append")), instruction =>
            instruction.Operand is MethodBase { Name: "Add" });
    }

    [Fact]
    public void ReassignedRestFunctionBinding_DoesNotDefineFlattenedCompanion()
    {
        const string source = """
            function total(...values: number[]): number { return values[0] + values[1]; }
            const original = total;
            total = (...values: number[]): number => values[0] * values[1];
            console.log(original(2, 3), total(2, 3));
            """;

        Assembly assembly = Compile(source);
        Assert.DoesNotContain(
            assembly.GetType("$Program")!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static),
            method => method.Name.Contains("$rest$arity", StringComparison.Ordinal));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"numeric_hot_paths_{Guid.NewGuid():N}");
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
