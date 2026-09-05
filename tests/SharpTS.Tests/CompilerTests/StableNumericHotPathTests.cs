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
            function run(index: number): number { return pick(index, 1, 2, 3); }
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
            instruction.Operand is MethodBase { Name: "AppendRest" });
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

    [Theory]
    [InlineData("const alias = add; const chain = alias;", "chain(i, 1, 2, 3)")]
    [InlineData("", "pick(0, i, 1, 2, 3)")]
    public void ProvenRestCalls_UseAllocationFreeCompanions(string setup, string call)
    {
        string source = $$"""
            function add(...values: number[]): number {
                return values[0] + values[1] + values[2] + values[3];
            }
            function pick(start: number, ...values: number[]): number {
                return values[start] + values[start + 1] + values[start + 2] + values[start + 3];
            }
            function run(n: number): number {
                {{setup}}
                let sum: number = 0.5;
                for (let i: number = 0; i < n; i++) sum = sum + {{call}};
                return sum;
            }
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        var assembly = Compile(source);
        var method = FindFunction(assembly, "run");
        var instructions = ReadInstructions(method).ToArray();
        Assert.Contains(instructions, i => i.Operand is MethodBase called && called.Name.Contains("$rest$arity"));
        Assert.DoesNotContain(instructions, i => i.OpCode == OpCodes.Newarr);
        Assert.DoesNotContain(instructions, i => i.Operand is MethodBase { Name: "InvokeMethodValue" or "AppendRest" });
        var run = method.CreateDelegate<Func<double, double>>();
        for (int i = 0; i < 20; i++) run(1000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double actual = run(100_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(5000550000.5, actual);
        // Creating an observable function value for the local alias can have a
        // fixed cost; its loop must allocate no per-call argument/rest storage.
        Assert.True(allocated < 1024, $"Unexpected loop allocations: {allocated}");
    }

    [Fact]
    public void ConstantIndexVariants_AreBounded()
    {
        string calls = string.Join(" + ", Enumerable.Range(0, 12)
            .Select(i => $"pick({i}, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11)"));
        var assembly = Compile($$"""
            function pick(index: number, ...values: number[]): number { return values[index]; }
            function run(): number { return {{calls}}; }
            """);
        Assert.Equal(8, assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Count(m => m.Name.Contains("$rest$arity")));
        Assert.Equal(66, FindFunction(assembly, "run").CreateDelegate<Func<double>>()());
    }

    [Fact]
    public void ConstantIndexVariants_HaveCompilationWideLimit()
    {
        string declarations = string.Join("\n", Enumerable.Range(0, 9).Select(i =>
            $"function pick{i}(index: number, ...values: number[]): number {{ return values[index]; }}"));
        string calls = string.Join(" + ", Enumerable.Range(0, 9).SelectMany(i => Enumerable.Range(0, 8)
            .Select(j => $"pick{i}({j}, 0, 1, 2, 3, 4, 5, 6, 7)")));
        var assembly = Compile(declarations + $"\nfunction run(): number {{ return {calls}; }}");
        Assert.Equal(64, assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Count(m => m.Name.Contains("$rest$arity")));
        Assert.Equal(252, FindFunction(assembly, "run").CreateDelegate<Func<double>>()());
    }

    [Fact]
    public void SpreadPacking_UsesOneDestinationAndVerifies()
    {
        const string source = """
            function collect(prefix: number, ...values: number[]): number[] { return values; }
            function run(): number { return collect(...[1, 2], 3, ...[4]).length; }
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        var method = FindFunction(Compile(source), "run");
        Assert.Contains(ReadInstructions(method), i => i.Operand is MethodBase { Name: "IterateIntoList" });
        Assert.DoesNotContain(ReadInstructions(method), i => i.Operand is MethodBase { Name: "ExpandCallArgs" });
        Assert.Equal(3, method.CreateDelegate<Func<double>>()());
    }

    [Fact]
    public void UnknownTarget_RetainsValueDispatch()
    {
        var assembly = Compile("""
            function run(fn: (...values: number[]) => number, n: number): number {
                return fn(n, 1, 2, 3);
            }
            """);
        Assert.Contains(ReadInstructions(FindFunction(assembly, "run")), i =>
            i.Operand is MethodBase { Name: "InvokeMethodValue" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpreadRest_PreservesNumericSourceAndIndependentDestination(bool numericSource)
    {
        const string source = """
            function input(): number[] {
                const values: number[] = [];
                values.push(1); values.push(2); values.push(3);
                return values;
            }
            function collect(prefix: number, ...values: number[]): number[] { return values; }
            function run(values: number[]): number[] { return collect(0, ...values, 4); }
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        var assembly = Compile(source);
        var type = assembly.GetType("$Array")!;
        var numeric = type.GetField("_isNumeric", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var store = type.GetField("_numStore", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object input = numericSource ? FindFunction(assembly, "input").Invoke(null, null)!
            : new List<object> { 1d, 2d, 3d };
        var run = FindFunction(assembly, "run").CreateDelegate<Func<object, object>>();
        object result = run(input);
        Assert.True((bool)numeric.GetValue(result)!);
        Assert.Empty((List<object>)result);
        var get = type.GetMethod("GetDouble")!;
        for (int i = 0; i < 4; i++) Assert.Equal(i + 1d, get.Invoke(result, [i]));
        if (numericSource)
        {
            Assert.True((bool)numeric.GetValue(input)!);
            Assert.Empty((List<object>)input);
            Assert.NotSame(store.GetValue(input), store.GetValue(result));
        }
    }

    [Fact]
    public void OrdinaryRestPacking_FillsFinalStorageWithoutTemporaryArray()
    {
        const string source = """
            function escape(...values: number[]): number[] { return values; }
            function run(n: number): number[] { return escape(n, 1, 2, 3); }
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        var instructions = ReadInstructions(FindFunction(Compile(source), "run")).ToArray();
        Assert.DoesNotContain(instructions, i => i.OpCode == OpCodes.Newarr);
        Assert.Contains(instructions, i => i.Operand is MethodBase { Name: "CreateNumericRest" });
        Assert.Contains(instructions, i => i.Operand is MethodBase { Name: "PushDouble" });
        Assert.DoesNotContain(instructions, i => i.OpCode == OpCodes.Box && i.Operand == typeof(double));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    public void OrdinaryNumericRest_KeepsFreshStorage(int count)
    {
        string arguments = string.Join(", ", Enumerable.Repeat("n", count));
        var assembly = Compile($$"""
            function escape(...values: number[]): number[] { return values; }
            function run(n: number): number[] { return escape({{arguments}}); }
            """);
        var run = FindFunction(assembly, "run").CreateDelegate<Func<double, object>>();
        var first = run(-0.0);
        var second = run(2);
        Assert.NotSame(first, second);
        var type = assembly.GetType("$Array")!;
        var numeric = type.GetField("_isNumeric", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(count > 0, numeric.GetValue(first));
        Assert.Empty((List<object>)first);
        if (count > 0)
        {
            var get = type.GetMethod("GetDouble")!;
            Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0),
                BitConverter.DoubleToInt64Bits((double)get.Invoke(first, [0])!));
            Assert.Equal(2d, get.Invoke(second, [count - 1]));
        }
    }


    [Fact]
    public void UsingBindingsPreventConstantIndexSpecialization()
    {
        var assembly = Compile("""
            function pick(start: number, ...values: number[]): any {
                let result: any;
                { using start: any = null; result = values[start]; }
                return result;
            }
            function run(): any { return pick(0, 10, 20); }
            """);
        Assert.DoesNotContain(assembly.GetType("$Program")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static),
            method => method.Name.Contains("$rest$arity", StringComparison.Ordinal));
    }

    internal static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"numeric_hot_paths_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    [Fact]
    public void WarmArgumentConversion_UsesCachedSignatureMetadata()
    {
        var assembly = Compile("""
            function run(prefix: number, ...values: number[]): number { return prefix + values.length; }
            """);
        var wrapper = assembly.GetType("$TSFunction")!;
        foreach (string name in new[] { "ConvertArgsForUnionTypes", "CoercePrimitiveArgs" })
        {
            var helper = wrapper.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(typeof(ParameterInfo[]), helper.GetParameters()[0].ParameterType);
            Assert.DoesNotContain(ReadInstructions(helper), i => i.Operand is MethodBase { Name: "GetParameters" });
        }
    }

    [Fact]
    public void IndirectNumericAndStringParameters_RetainClrConversions()
    {
        const string source = """
            function numeric(prefix: number, ...values: number[]): number { return prefix + values[0] + values.length; }
            function text(prefix: string, ...values: number[]): string { return prefix + ":" + values.length; }
            const n: any = numeric;
            const s: any = text;
            console.log(n("10", 1, 2), s(7, 1, 2));
            console.log(n.call(null, "20", 1, 2), s.apply(null, [8, 1]));
            """;
        // Foreign values crossing typed CLR slots retain the existing compiled ABI.
        Assert.Equal("13 7:2\n23 8:1\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }

    internal static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    internal static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> ReadInstructions(
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
