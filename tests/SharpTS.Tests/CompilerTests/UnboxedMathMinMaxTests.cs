using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class UnboxedMathMinMaxTests
{
    private const string NumericLoopSource = """
        function mathLoop(n: number): number {
            const a: number = 3;
            const b: number = 4;
            let total: number = 0;
            for (let i: number = 0; i < n; i++) {
                total = total + Math.min(a, b) + Math.max(a, b);
            }
            return total;
        }
        """;

    [Fact]
    public void FixedArityNumericCalls_StayInNativeDoubleIl()
    {
        MethodInfo loop = FindFunction(Compile(NumericLoopSource), "mathLoop");
        var instructions = ReadInstructions(loop).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodBase
            {
                DeclaringType: { } declaringType,
                Name: "Min"
            }
            && declaringType == typeof(Math));
        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodBase
            {
                DeclaringType: { } declaringType,
                Name: "Max"
            }
            && declaringType == typeof(Math));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "ToNumber" or "MathMinAdapter" or "MathMaxAdapter"
            });
    }

    [Fact]
    public void ZeroOneAndSeveralNumericArguments_AreAlsoUnboxed()
    {
        const string source = """
            function emptyMin(): number { return Math.min(); }
            function emptyMax(): number { return Math.max(); }
            function one(a: number): number { return Math.min(a); }
            function several(a: number, b: number, c: number): number {
                return Math.max(a, b, c);
            }
            """;

        Assembly assembly = Compile(source);
        foreach (string name in new[] { "emptyMin", "emptyMax", "one", "several" })
        {
            var instructions = ReadInstructions(FindFunction(assembly, name)).ToArray();
            Assert.DoesNotContain(instructions, instruction =>
                instruction.OpCode == OpCodes.Box);
            Assert.DoesNotContain(instructions, instruction =>
                instruction.Operand is MethodBase { Name: "ToNumber" });
        }

        Assert.Equal(2, ReadInstructions(FindFunction(assembly, "several")).Count(instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodBase { Name: "Min" or "Max" }));
    }

    [Fact]
    public void TwoArgumentNumericLoop_HasNoSteadyStateAllocation()
    {
        var loop = FindFunction(Compile(NumericLoopSource), "mathLoop")
            .CreateDelegate<Func<double, double>>();

        Assert.Equal(70, loop(10));
        _ = loop(10_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(7_000, loop(1_000));
        long smallAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(700_000, loop(100_000));
        long largeAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(largeAllocated <= smallAllocated + 1_024,
            $"Typed Math.min/max allocations scaled: {smallAllocated:N0} vs {largeAllocated:N0} bytes.");
    }

    [Theory, ModeData]
    public void NumericFastPath_PreservesEdgesAndEvaluationOrder(ExecutionMode mode)
    {
        const string source = """
            const positiveZero: number = 0;
            const negativeZero: number = -0;
            const nan: number = NaN;
            const positiveInfinity: number = Infinity;
            const negativeInfinity: number = -Infinity;

            console.log(Math.min() === positiveInfinity);
            console.log(Math.max() === negativeInfinity);
            console.log(Number.isNaN(Math.min(1, nan, 2)));
            console.log(Number.isNaN(Math.max(nan, 1)));
            console.log(Object.is(Math.min(positiveZero, negativeZero), negativeZero));
            console.log(Object.is(Math.min(negativeZero, positiveZero), negativeZero));
            console.log(Object.is(Math.max(positiveZero, negativeZero), positiveZero));
            console.log(Object.is(Math.max(negativeZero, positiveZero), positiveZero));
            console.log(Math.min(positiveInfinity, 9, negativeInfinity));
            console.log(Math.max(negativeInfinity, 9, positiveInfinity));

            const events: string[] = [];
            function first(): number { events.push("first"); return 3; }
            function second(): number { events.push("second"); return 1; }
            function third(): number { events.push("third"); return 2; }
            console.log(Math.min(first(), second(), third()));
            console.log(events.join(","));
            """;

        Assert.Equal(
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n" +
            "-Infinity\nInfinity\n1\nfirst,second,third\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DynamicSpreadAndStoredCalls_KeepGenericSemantics(ExecutionMode mode)
    {
        const string source = """
            const events: string[] = [];
            const first: any = {
                valueOf: function(): number { events.push("first"); return 4; }
            };
            const second: any = {
                valueOf: function(): number { events.push("second"); return 2; }
            };
            console.log(Math.min(first, second));
            console.log(events.join(","));
            console.log(Math.max(...[1, 7, 3]));

            const storedMin: any = Math.min;
            const mathAlias: any = Math;
            const storedMax: any = mathAlias.max;
            console.log(storedMin("3", 2));
            console.log(storedMax("3", 2));

            try { Math.min(1n as any, 1); }
            catch (error) { console.log(error instanceof TypeError); }
            try { Math.max(Symbol("x") as any, 1); }
            catch (error) { console.log(error instanceof TypeError); }
            """;

        Assert.Equal(
            "2\nfirst,second\n7\n2\n3\ntrue\ntrue\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void ReplacedMathMethod_UsesLivePropertyDispatch()
    {
        const string source = """
            (Math as any).min = function(a: any, b: any): number {
                console.log("custom", a, b);
                return 41;
            };
            console.log(Math.min(1, 2));
            const stored: any = Math.min;
            console.log(stored(3, 4));
            """;

        Assembly assembly = Compile(source);
        MethodInfo main = assembly.GetType("$Program")!.GetMethod(
            "Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.DoesNotContain(ReadInstructions(main), instruction =>
            instruction.Operand is MethodBase
            {
                DeclaringType: { } declaringType,
                Name: "Min"
            }
            && declaringType == typeof(Math));
        Assert.Contains(ReadInstructions(main), instruction =>
            instruction.Operand is MethodBase { Name: "InvokeMethodValue" });
        Assert.Equal(
            "custom 1 2\n41\ncustom 3 4\n41\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void DynamicAndSpreadControlsUseGenericAdaptersAndVerify()
    {
        const string source = NumericLoopSource + """
            function dynamicMin(a: any, b: any): number {
                return Math.min(a, b);
            }
            function spreadMax(values: number[]): number {
                return Math.max(...values);
            }
            console.log(mathLoop(2));
            console.log(dynamicMin("3", 2));
            console.log(spreadMax([1, 7, 3]));
            """;

        Assembly assembly = Compile(source);
        Assert.Contains(ReadInstructions(FindFunction(assembly, "dynamicMin")), instruction =>
            instruction.Operand is MethodBase { Name: "ToNumber" });
        Assert.Contains(ReadInstructions(FindFunction(assembly, "spreadMax")), instruction =>
            instruction.Operand is MethodBase { Name: "MathMaxAdapter" });

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("14\n2\n7\n", output);
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"unboxed_math_min_max_{Guid.NewGuid():N}");
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
