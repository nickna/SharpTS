using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class UnboxedNumberArrayReadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedBoxedNumericReads_DoNotAllocate(bool plainList)
    {
        var assembly = Compile("""
            function sum(values: number[], n: number): number {
                let result: number = 0.5;
                for (let i: number = 0; i < n; i++) {
                    const index: number = i % 2;
                    result = result + values[index] + values[index + 1] + values[index + 2] + values[index + 3];
                }
                return result;
            }
            """);
        var method = FindFunction(assembly, "sum");
        Assert.Contains(ReadInstructions(method), i => i.Operand is MethodBase { Name: "TryGetBoxedDouble" });
        var run = method.CreateDelegate<Func<object, double, double>>();
        var list = new List<object> { 1d, 1d, 1d, 1d, 1d };
        object values = plainList ? list : Activator.CreateInstance(assembly.GetType("$Array")!, [list])!;
        for (int i = 0; i < 30; i++) Assert.Equal(4000.5, run(values, 1000));
        long before = GC.GetAllocatedBytesForCurrentThread();
        double actual = run(values, 10000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(40000.5, actual);
        Assert.Equal(0, allocated);
    }

    [Theory, ModeData]
    public void BoxedReadGuard_PreservesValuesAndSideEffects(ExecutionMode mode)
    {
        const string source = """
            function read(values: number[], index: number): number { return values[index] * 2; }
            const values: number[] = [2, 3, 4];
            const alias: any = values;
            alias[1] = "7";
            console.log(read(values, 0), read(values, 1));
            delete alias[0];
            console.log(read(values, 0), read(values, -1), read(values, 10));
            let calls: number = 0;
            function key(): number { calls++; alias[2] = "9"; return 2; }
            console.log(values[key()] * 2, calls);
            const numeric: number[] = [];
            numeric.push(-0); numeric.push(NaN); numeric.push(Infinity);
            console.log(1 / read(numeric, 0), read(numeric, 1), read(numeric, 2));
            """;
        Assert.Equal("4 14\nNaN NaN NaN\n18 1\n-Infinity NaN Infinity\n", TestHarness.Run(source, mode));
        if (mode == ExecutionMode.Compiled) Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Theory, ModeData]
    public void BoxedReadGuard_RespectsAccessorAndPrototypeOverrides(ExecutionMode mode)
    {
        const string source = """
            function read(values: number[], index: number): number { return values[index] * 2; }
            let calls: number = 0;
            const values: number[] = [2, 3];
            Object.defineProperty(values, "0", { get: (): number => { calls++; return 7; } });
            console.log(read(values, 0), calls);
            const alias: any = values;
            delete alias[1];
            Object.defineProperty(Array.prototype, "1", { value: 9, configurable: true, writable: true });
            console.log(read(values, 1));
            delete (Array.prototype as any)[1];
            """;
        Assert.Equal("14 1\n18\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void ArrayLength_KeepsNumericResultUnboxed()
    {
        Assembly assembly = Compile("""
            function length(values: { value: number }[]): number {
                return values.length;
            }
            """);
        MethodInfo method = FindFunction(assembly, "length");
        Assert.DoesNotContain(ReadInstructions(method), instruction => instruction.OpCode == OpCodes.Box);
        var length = method.CreateDelegate<Func<object, double>>();
        var values = new List<object> { new object() };
        for (int i = 0; i < 1000; i++) _ = length(values);
        long before = GC.GetAllocatedBytesForCurrentThread();
        double sum = 0;
        for (int i = 0; i < 10_000; i++) sum += length(values);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(10_000, sum);
        Assert.True(allocated <= 1024, $"Array length reads allocated {allocated:N0} bytes.");
    }

    [Theory, ModeData]
    public void ArrayLength_PreservesObjectConsumersAndMutations(ExecutionMode mode)
    {
        const string source = """
            function length(values: any[]): any { return values.length; }
            const values: any[] = [];
            values.push("x");
            console.log(length(values), typeof length(values));
            values.push("y");
            console.log(values.length + 1);
            console.log(JSON.stringify({ size: values.length }));
            const parsed: any[] = JSON.parse('[1,2,3]');
            console.log(length(parsed));
            """;
        Assert.Equal("1 number\n3\n{\"size\":2}\n3\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void DenseNumericLiteral_ConstructsPackedStorageWithoutBoxingElements()
    {
        var assembly = Compile("""
            function make(n: number): number[] { return [n, n + 1, n + 2, n + 3]; }
            """);
        var instructions = ReadInstructions(FindFunction(assembly, "make")).ToArray();
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Newobj &&
            instruction.Operand is ConstructorInfo ctor && ctor.DeclaringType?.Name == "$Array" &&
            ctor.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(new[] { typeof(double[]) }));
        Assert.DoesNotContain(instructions, instruction => instruction.OpCode == OpCodes.Box);
    }

    [Fact]
    public void NestedArrayRead_UsesNumericStorageAndCapturesReceiverBeforeKey()
    {
        const string source = """
            interface Record { values: number[]; }
            function read(record: Record, index: number): number {
                return record.values[index] * 2;
            }
            let record: Record = { values: [3, 4] };
            function key(): number { record.values = [9, 10]; return 0; }
            console.log(record.values[key()] * 2, read(record, 0));
            """;
        var assembly = Compile(source);
        Assert.Contains(ReadInstructions(FindFunction(assembly, "read")), instruction =>
            instruction.Operand is MethodBase { Name: "GetDouble" });
        Assert.Equal("6 18\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void NumericConsumer_UsesGuardedGetDoubleWithoutBoxingHotResult()
    {
        Assembly assembly = Compile("""
            function read(values: number[], index: number): number {
                return values[index];
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "read")).ToArray();
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "CanGetDouble" });

        int getDouble = Array.FindIndex(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetDouble" });
        Assert.True(getDouble >= 0, "Expected the guarded numeric $Array read fast path.");
        Assert.Equal(FlowControl.Branch, instructions[getDouble + 1].OpCode.FlowControl);
        Assert.False(instructions.Skip(getDouble + 1).Take(2).Any(instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double)),
            "The GetDouble hot result must branch to the native-double merge without boxing.");
    }

    [Fact]
    public void RawConsumer_RetainsOrdinaryObjectRead()
    {
        Assembly assembly = Compile("""
            function read(values: number[], index: number): any {
                return values[index];
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "read")).ToArray();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetDouble" });
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "Get" });
    }

    [Fact]
    public void FractionalIndex_PreservesOrdinaryPropertyKey()
    {
        const string source = """
            function read(values: number[], index: number): number {
                return values[index] * 2;
            }
            const receiver: any = {};
            receiver[3] = 100;
            receiver[3.5] = 7;
            console.log(read(receiver as number[], 3.5));
            """;

        Assert.Equal("14\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void OutOfRangeAndNegativeReads_RetainOrdinarySemantics()
    {
        const string source = """
            function read(values: number[], index: number): number {
                return values[index] * 2;
            }
            const values: number[] = [];
            values.push(4);
            console.log(read(values, 5), read(values, -1), read(values, 4294967295));
            """;

        Assert.Equal("NaN NaN NaN\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void BoxedArrayAndDynamicGetter_UseOrdinaryFallback()
    {
        const string source = """
            function read(values: number[], index: number): number {
                return values[index] * 2;
            }

            const values: number[] = [];
            values.push(4);
            const alias: any = values;
            alias[0] = "6";

            let calls: number = 0;
            const receiver: any = {
                get 0(): number { calls = calls + 1; return 9; }
            };

            console.log(read(values, 0), read(receiver as number[], 0), calls);
            """;

        Assert.Equal("12 18 1\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void ObservableDescriptors_DisableGetDoubleIntrinsic()
    {
        Assembly assembly = Compile("""
            Object.defineProperty([], "0", { get: (): number => 1 });
            function read(values: number[], index: number): number {
                return values[index];
            }
            """);

        var instructions = ReadInstructions(FindFunction(assembly, "read")).ToArray();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetDouble" });
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetIndex" });
    }

    [Fact]
    public void HoistedLoopRead_PassesIlVerification()
    {
        const string source = """
            function sum(values: number[]): number {
                let result: number = 0;
                let i: number = 0;
                while (i < values.length) {
                    result = result + values[i];
                    i = i + 1;
                }
                return result;
            }
            const values: number[] = [];
            values.push(1); values.push(2); values.push(3);
            console.log(sum(values));
            """;

        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("6\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void NumericRead_WorksInStandaloneOutput()
    {
        const string source = """
            function read(values: number[], index: number): number {
                return values[index] * 2;
            }
            const values: number[] = [];
            values.push(21);
            console.log(read(values, 0));
            """;

        Assert.Equal("42\n", TestHarness.RunCompiledStandalone(source));
    }

    [Fact]
    public void DirectEvalInLoop_DisablesReceiverHoisting()
    {
        var statements = new Parser(new Lexer("""
            function read(): number {
                let values: number[] = [];
                values.push(1);
                let i: number = 0;
                while (i < 2) {
                    eval("values = [20]");
                    console.log(values[0]);
                    i = i + 1;
                }
                return values[0];
            }
            """).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var function = Assert.IsType<Stmt.Function>(Assert.Single(statements));
        var loop = Assert.IsType<Stmt.While>(Assert.Single(function.Body!.OfType<Stmt.While>()));

        Assert.Empty(ArrayHoistAnalyzer.AnalyzeFor(
            loop.Body, loop.Condition, increment: null, typeMap));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1432_{Guid.NewGuid():N}");
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
