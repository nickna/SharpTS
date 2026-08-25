using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableNumericStringConversionTests
{
    [Fact]
    public void StableTypedConversions_PreserveNumericSemantics()
    {
        const string source = """
            function format(value: number): string {
                return value.toString() + "|" + value.toString(10)
                    + "|" + value.toFixed() + "|" + value.toFixed(2);
            }

            console.log(format(12.34));
            console.log((-0).toString(), (-0).toFixed(2));
            console.log((1e21).toFixed(2), (1e-6).toString(), (1e-7).toString());
            console.log((NaN).toFixed(1), (Infinity).toFixed(1));
            console.log(
                parseInt("42"),
                parseInt("0x10"),
                parseInt("0x10", 16),
                Object.is(parseInt("-0", 10), -0),
                parseInt("101tail", 2),
                parseInt("z", 36),
                Number.isNaN(parseInt("x", 10)));
            """;

        const string expected =
            "12.34|12.34|12|12.34\n" +
            "0 0.00\n" +
            "1e+21 0.000001 1e-7\n" +
            "NaN Infinity\n" +
            "42 16 16 true 5 35 true\n";

        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, ModeData]
    public void ToFixed_UsesJavaScriptExactRounding(ExecutionMode mode)
    {
        const string source = """
            function dynamicFixed(value: number, digits: number): string {
                return value.toFixed(digits);
            }

            console.log(
                (0.125).toFixed(2),
                (1.125).toFixed(2),
                (2.5).toFixed(0),
                (3.5).toFixed(0),
                (-2.5).toFixed(0));
            console.log(
                (2.55).toFixed(1),
                (1.005).toFixed(2),
                (-0.001).toFixed(2),
                (-0).toFixed(2));
            console.log(dynamicFixed(0.1, 100));
            console.log(dynamicFixed(5e-324, 100));
            console.log(dynamicFixed(999999999999999900000, 2));
            """;

        const string expected =
            "0.13 1.13 3 4 -3\n" +
            "2.5 1.00 -0.00 0.00\n" +
            "0.1000000000000000055511151231257827021181583404541015625" +
                "000000000000000000000000000000000000000000000\n" +
            "0.0000000000000000000000000000000000000000000000000000000" +
                "000000000000000000000000000000000000000000000\n" +
            "999999999999999868928.00\n";

        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Fact]
    public void StableTypedConversions_PassIlVerification()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly("""
            function render(value: number): string {
                return value.toString(10) + value.toFixed(2);
            }
            function read(value: string): number {
                return parseInt(value, 10);
            }
            console.log(render(read("12")));
            """));
    }

    [Fact]
    public void StableTypedConversions_CallTypedRuntimeHelpers()
    {
        Assembly assembly = Compile("""
            function counterLengths(n: number): number {
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    total += i.toString().length;
                }
                return total;
            }
            function render(value: number): string { return value.toString(10); }
            function fixed(value: number): string { return value.toFixed(2); }
            function read(value: string): number { return parseInt(value, 10); }
            function readStatic(value: string): number {
                return Number.parseInt(value, 10);
            }
            function parseCounter(n: number): number {
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    total += parseInt(i.toString(), 10);
                }
                return total;
            }
            function parseCounterStatic(n: number): number {
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    total += Number.parseInt(i.toString(), 10);
                }
                return total;
            }
            """);

        AssertCalls(assembly, "counterLengths", "ConcatStringInt64");
        AssertCalls(assembly, "render", "FormatNumber");
        AssertCalls(assembly, "fixed", "NumberToFixedDouble");
        AssertCalls(assembly, "read", "NumberParseIntDecimalString");
        AssertCalls(assembly, "readStatic", "NumberParseIntDecimalString");

        foreach (string function in new[]
                 {
                     "counterLengths", "render", "fixed", "read", "readStatic",
                     "parseCounter", "parseCounterStatic"
                 })
        {
            MethodInfo method = FindFunction(assembly, function);
            MethodBase? unexpected = CalledMethods(method).FirstOrDefault(called =>
                called.Name is "NumberToStringRadix" or "NumberToFixed" or
                    "NumberParseIntString" or "NumberParseInt" or
                    "ParseIntHelper" or "InvokeMethodValue");
            Assert.True(unexpected == null,
                $"{function} unexpectedly calls {unexpected?.Name}.");
        }

        foreach (string function in new[] { "parseCounter", "parseCounterStatic" })
        {
            MethodInfo method = FindFunction(assembly, function);
            Assert.DoesNotContain(CalledMethods(method), called =>
                called.Name is "NumberParseIntDecimalString" or "FormatNumber" or
                    "ConcatStringInt64");
            Assert.Contains(Instructions(method), instruction =>
                instruction.OpCode == OpCodes.Conv_R8);
        }

        Assert.DoesNotContain(Instructions(FindFunction(assembly, "read")), instruction =>
            instruction.OpCode == OpCodes.Box);
        Assert.DoesNotContain(
            Instructions(FindFunction(assembly, "readStatic")),
            instruction => instruction.OpCode == OpCodes.Box);
        Assert.DoesNotContain(Instructions(FindFunction(assembly, "fixed")), instruction =>
            instruction.OpCode == OpCodes.Box);
    }

    [Fact]
    public void TypedDecimalParseIntLoop_DoesNotAllocatePerIteration()
    {
        Assembly assembly = Compile("""
            function run(n: number): number {
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    total += parseInt(" \uFEFF12345suffix", 10);
                }
                return total;
            }
            """);
        var run = FindFunction(assembly, "run").CreateDelegate<Func<double, double>>();

        Assert.Equal(123450, run(10));
        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = run(100_000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1_234_500_000, result);
        Assert.True(allocated <= 256,
            $"Typed decimal parseInt allocated {allocated:N0} bytes in the hot loop.");
    }

    [Fact]
    public void TypedToFixedLoop_AllocatesOnlyResultStrings()
    {
        Assembly assembly = Compile("""
            function render(value: number): string {
                return value.toFixed(2);
            }
            """);
        var render = FindFunction(assembly, "render")
            .CreateDelegate<Func<double, object>>();

        Assert.Equal("1.25", (string)render(1.25));
        long before = GC.GetAllocatedBytesForCurrentThread();
        string result = "";
        for (int i = 0; i < 100_000; i++)
            result = (string)render(i * 0.125);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal("12499.88", result);
        Assert.True(allocated <= 4_100_000,
            $"Typed toFixed allocated {allocated:N0} bytes for 100,000 result strings.");
    }

    [Theory, ModeData]
    public void NumberPrototypeMutation_RetainsLiveDispatch(ExecutionMode mode)
    {
        const string source = """
            const originalToString: any = Number.prototype.toString;
            const originalToFixed: any = Number.prototype.toFixed;
            function parseCounters(n: number): number {
                let total: number = 0;
                for (let i: number = 0; i < n; i++) {
                    total += parseInt(i.toString(), 10);
                    total += Number.parseInt(i.toString(), 10);
                }
                return total;
            }

            (Number.prototype as any).toString = function(): string { return "patched-string"; };
            (Number.prototype as any).toFixed = function(): string { return "patched-fixed"; };
            console.log((5).toString(), (5).toFixed(2));
            console.log(parseCounters(3));

            (Number.prototype as any).toString = originalToString;
            (Number.prototype as any).toFixed = originalToFixed;
            """;

        Assert.Equal(
            "patched-string patched-fixed\nNaN\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void ReassignedGlobalParseInt_RetainsLiveDispatch()
    {
        const string source = """
            const originalParseInt: any = globalThis.parseInt;
            (globalThis as any).parseInt = function(): number { return 77; };
            console.log(parseInt("10", 10));
            (globalThis as any).parseInt = originalParseInt;
            """;

        Assert.Equal("77\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void ReassignedNumberParseInt_RetainsLiveDispatch()
    {
        const string source = """
            const originalParseInt: any = Number.parseInt;
            (Number as any).parseInt = function(): number { return 88; };
            console.log(Number.parseInt("10", 10));
            (Number as any).parseInt = originalParseInt;
            """;

        Assert.Equal("88\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, ModeData]
    public void ShadowedParseInt_RetainsValueDispatch(ExecutionMode mode)
    {
        const string source = """
            function invoke(
                parseInt: (value: string, radix: number) => number
            ): number {
                return parseInt("10", 10);
            }
            console.log(invoke((_value, _radix) => 99));
            """;

        Assert.Equal("99\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncTypedConversions_AreCorrect(ExecutionMode mode)
    {
        const string source = """
            async function convert(value: number): Promise<string> {
                await Promise.resolve(0);
                return value.toString() + "|" + value.toFixed(2)
                    + "|" + parseInt("12", 10);
            }
            convert(3.5).then(value => console.log(value));
            """;

        Assert.Equal("3.5|3.50|12\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void DynamicArguments_KeepGeneralNumberConversionHelpers()
    {
        Assembly assembly = Compile("""
            function render(value: number, radix: number): string {
                return value.toString(radix);
            }
            function fixed(value: number, digits: number): string {
                return value.toFixed(digits);
            }
            function dynamicRadix(value: string, radix: number): number {
                return parseInt(value, radix);
            }
            function dynamicInput(value: any): number {
                return parseInt(value, 10);
            }
            function hexadecimal(value: string): number {
                return parseInt(value, 16);
            }
            """);

        AssertCalls(assembly, "render", "NumberToStringRadix");
        AssertCalls(assembly, "fixed", "NumberToFixed");
        AssertCalls(assembly, "dynamicRadix", "NumberParseInt");
        AssertCalls(assembly, "dynamicInput", "NumberParseInt");
        AssertCalls(assembly, "hexadecimal", "NumberParseIntString");
        foreach (string function in new[]
                 {
                     "dynamicRadix", "dynamicInput", "hexadecimal"
                 })
        {
            Assert.DoesNotContain(CalledMethods(FindFunction(assembly, function)),
                called => called.Name == "NumberParseIntDecimalString");
        }
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"numeric_string_conversions_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static void AssertCalls(Assembly assembly, string function, string helper) =>
        Assert.Contains(CalledMethods(FindFunction(assembly, function)),
            called => called.Name == helper);

    private static IEnumerable<MethodBase> CalledMethods(MethodInfo method) =>
        Instructions(method)
            .Where(instruction => instruction.Operand is MethodBase)
            .Select(instruction => (MethodBase)instruction.Operand!);

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> Instructions(MethodInfo method)
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
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
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
