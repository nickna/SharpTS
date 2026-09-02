using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StablePrimitiveStringIntrinsicTests
{
    private const string IntrinsicFunctions = """
        function find(input: string, needle: string, position: number): number {
            return input.indexOf(needle, position);
        }
        function contains(input: string, needle: string, position: number): boolean {
            return input.includes(needle, position);
        }
        function takeSlice(input: string, start: number, end: number): string {
            return input.slice(start, end);
        }
        function takeSubstring(input: string, start: number, end: number): string {
            return input.substring(start, end);
        }
        function plainStringLength(input: string): number {
            return input.length;
        }
        function sliceLength(input: string, start: number, end: number): number {
            return input.slice(start, end).length;
        }
        function substringLength(input: string, start: number, end: number): number {
            return input.substring(start, end).length;
        }
        function sliceFromLength(input: string, start: number): number {
            return input.slice(start).length;
        }
        function substringFromLength(input: string, start: number): number {
            return input.substring(start).length;
        }
        function sliceLengthLoop(
            input: string, start: number, end: number, n: number): number {
            let total: number = 0;
            let currentStart: number = start;
            for (let i: number = 0; i < n; i++) {
                total = total + input.slice(currentStart, end).length;
                currentStart = currentStart === start ? start + 1 : start;
            }
            return total;
        }
        function substringLengthLoop(
            input: string, start: number, end: number, n: number): number {
            let total: number = 0;
            let currentStart: number = start;
            for (let i: number = 0; i < n; i++) {
                total = total + input.substring(currentStart, end).length;
                currentStart = currentStart === start ? start + 1 : start;
            }
            return total;
        }
        """;

    [Theory]
    [InlineData("find", "StringIndexOfPrimitive")]
    [InlineData("contains", "StringIncludesPrimitive")]
    [InlineData("takeSlice", "StringSlicePrimitive")]
    [InlineData("takeSubstring", "StringSubstringPrimitive")]
    public void StablePrimitiveCall_UsesTypedFixedArityHelper(
        string functionName, string helperName)
    {
        MethodInfo method = FindFunction(Compile(IntrinsicFunctions), functionName);
        var instructions = ReadInstructions(method).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodBase { Name: var name }
            && name == helperName);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode is var opCode
            && (opCode == OpCodes.Box || opCode == OpCodes.Newarr));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "UnwrapStringReceiver" or "InvokeMethodValue"
            });
    }

    [Theory]
    [InlineData("sliceLength", "StringSliceLengthPrimitive")]
    [InlineData("substringLength", "StringSubstringLengthPrimitive")]
    [InlineData("sliceFromLength", "StringSliceFromLengthPrimitive")]
    [InlineData("substringFromLength", "StringSubstringFromLengthPrimitive")]
    public void StablePrimitiveSliceLength_UsesAllocationFreeBoundsHelper(
        string functionName, string helperName)
    {
        MethodInfo method = FindFunction(Compile(IntrinsicFunctions), functionName);
        var instructions = ReadInstructions(method).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodBase { Name: var name }
            && name == helperName);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode is var opCode
            && (opCode == OpCodes.Box || opCode == OpCodes.Newarr));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "StringSlicePrimitive" or "StringSubstringPrimitive"
            });
    }

    [Fact]
    public void StablePrimitiveStringLength_RemainsUnboxed()
    {
        MethodInfo method = FindFunction(Compile(IntrinsicFunctions), "plainStringLength");
        var instructions = ReadInstructions(method).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "get_Length" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box);
    }

    [Fact]
    public void StablePrimitiveSearch_DoesNotAllocatePerCall()
    {
        Assembly assembly = Compile(IntrinsicFunctions);
        var find = FindFunction(assembly, "find")
            .CreateDelegate<Func<string, string, double, double>>();
        var contains = FindFunction(assembly, "contains")
            .CreateDelegate<Func<string, string, double, bool>>();
        const string input = "alpha-beta-gamma";
        const string needle = "beta";

        for (int i = 0; i < 10_000; i++)
        {
            _ = find(input, needle, 1);
            _ = contains(input, needle, 1);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        double total = 0;
        for (int i = 0; i < 100_000; i++)
        {
            total += find(input, needle, i & 1);
            if (contains(input, needle, i & 1))
                total++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(700_000, total);
        Assert.True(allocated <= 1_024,
            $"Primitive string searches allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void StablePrimitiveSliceLengths_DoNotAllocateResultStringsOrBoxes()
    {
        Assembly assembly = Compile(IntrinsicFunctions);
        var slice = FindFunction(assembly, "sliceLengthLoop")
            .CreateDelegate<Func<string, double, double, double, double>>();
        var substring = FindFunction(assembly, "substringLengthLoop")
            .CreateDelegate<Func<string, double, double, double, double>>();
        Assert.DoesNotContain(
            ReadInstructions(FindFunction(assembly, "sliceLengthLoop")),
            instruction => instruction.OpCode == OpCodes.Box);
        Assert.DoesNotContain(
            ReadInstructions(FindFunction(assembly, "substringLengthLoop")),
            instruction => instruction.OpCode == OpCodes.Box);
        const string input = "alpha-beta-gamma-delta";

        Assert.Equal(1_450, slice(input, 3, 18, 100));
        Assert.Equal(1_450, substring(input, 3, 18, 100));
        _ = slice(input, 3, 18, 10_000);
        _ = substring(input, 3, 18, 10_000);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(1_450_000, slice(input, 3, 18, 100_000));
        Assert.Equal(1_450_000, substring(input, 3, 18, 100_000));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= 1_024,
            $"Primitive slice lengths allocated {allocated:N0} bytes.");
    }

    [Theory, ModeData]
    public void PrimitiveIntrinsics_PreservePositionsUtf16AndOmittedArguments(
        ExecutionMode mode)
    {
        const string source = """
            const unicode: string = "A😀BC😀D";
            const ascii: string = "abcdef";
            const zero: number = 0;
            const two: number = 2;
            const three: number = 3;
            const five: number = 5;
            const huge: number = 1e100;
            const negativeHuge: number = -1e100;
            const notNumber: number = NaN;
            const positiveInfinity: number = Infinity;
            const negativeInfinity: number = -Infinity;

            console.log(unicode.indexOf("😀", zero));
            console.log(unicode.indexOf("😀", two));
            console.log(unicode.indexOf("😀"));
            console.log(unicode.indexOf("", huge));
            console.log(unicode.indexOf("", negativeInfinity));
            console.log(unicode.includes("😀", two));
            console.log(unicode.includes("😀", huge));
            console.log(unicode.includes("A"));
            console.log(unicode.includes("A", notNumber));

            console.log(ascii.slice(zero));
            console.log(ascii.slice(-three));
            console.log(ascii.slice(negativeInfinity, positiveInfinity));
            console.log(ascii.slice(notNumber, three));
            console.log(ascii.slice(five, two));
            console.log(ascii.slice(positiveInfinity));

            console.log(ascii.substring(zero));
            console.log(ascii.substring(-three));
            console.log(ascii.substring(five, two));
            console.log(ascii.substring(notNumber, positiveInfinity));
            console.log(ascii.substring(positiveInfinity, negativeHuge));
            """;

        Assert.Equal(
            "1\n5\n1\n8\n0\ntrue\nfalse\ntrue\ntrue\n" +
            "abcdef\ndef\nabcdef\nabc\n\n\n" +
            "abcdef\nabcdef\ncde\nabcdef\nabcdef\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PrimitiveSliceLengths_PreservePositionsUtf16AndOmittedArguments(
        ExecutionMode mode)
    {
        const string source = """
            const unicode: string = "A😀BC😀D";
            const ascii: string = "abcdef";
            const zero: number = 0;
            const three: number = 3;
            const five: number = 5;
            const notNumber: number = NaN;
            const positiveInfinity: number = Infinity;
            const negativeInfinity: number = -Infinity;
            const negativeHuge: number = -1e100;
            const fractionalStart: number = 1.9;
            const fractionalEnd: number = 4.8;

            console.log(unicode.slice(1, 3).length);
            console.log(ascii.slice(zero).length);
            console.log(ascii.slice(-three).length);
            console.log(ascii.slice(negativeInfinity, positiveInfinity).length);
            console.log(ascii.slice(notNumber, three).length);
            console.log(ascii.slice(five, 2).length);
            console.log(ascii.slice(positiveInfinity).length);
            console.log(ascii.slice(fractionalStart, fractionalEnd).length);

            console.log(ascii.substring(zero).length);
            console.log(ascii.substring(-three).length);
            console.log(ascii.substring(five, 2).length);
            console.log(ascii.substring(notNumber, positiveInfinity).length);
            console.log(ascii.substring(positiveInfinity, negativeHuge).length);
            console.log(ascii.substring(fractionalStart, fractionalEnd).length);
            """;

        Assert.Equal(
            "2\n6\n3\n6\n3\n0\n0\n3\n" +
            "6\n6\n3\n6\n6\n3\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void GenericFallback_PreservesBoxingCoercionAndRegExpRejection(
        ExecutionMode mode)
    {
        const string source = """
            const boxed: any = new String("abcdef");
            const typedBoxed: String = new String("abcdef");
            console.log(typedBoxed.length);
            console.log(boxed.indexOf("cd", 0));
            console.log(boxed.includes("ef", 0));
            console.log(boxed.slice(1, 4));
            console.log(boxed.substring(4, 1));

            const events: string[] = [];
            const needle: any = {
                toString: function(): string { events.push("needle"); return "cd"; }
            };
            const position: any = {
                valueOf: function(): number { events.push("position"); return 1; }
            };
            console.log("abcdef".indexOf(needle, position));
            console.log(events.join(","));

            try {
                console.log("abcdef".includes(/cd/ as any));
            } catch (error) {
                console.log(error instanceof TypeError);
            }
            """;

        Assert.Equal(
            "6\n2\ntrue\nbcd\nbcd\n2\nneedle,position\ntrue\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void StringPrototypeReplacement_DisablesTypedHelperAndIsObservable()
    {
        const string source = """
            (String.prototype as any).indexOf = function(
                search: any, position: any): number {
                console.log("custom", this, search, position);
                return 41;
            };
            const input: string = "abcdef";
            const needle: string = "cd";
            const position: number = 2;
            console.log(input.indexOf(needle, position));
            """;

        Assembly assembly = Compile(source);
        MethodInfo main = assembly.GetType("$Program")!.GetMethod(
            "Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.DoesNotContain(ReadInstructions(main), instruction =>
            instruction.Operand is MethodBase { Name: "StringIndexOfPrimitive" });
        Assert.Contains(ReadInstructions(main), instruction =>
            instruction.Operand is MethodBase { Name: "InvokeMethodValue" });
        Assert.Equal("custom abcdef cd 2\n41\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void StringPrototypeReplacement_DisablesSliceLengthHelperAndIsObservable()
    {
        const string source = """
            (String.prototype as any).slice = function(
                start: any, end: any): string {
                console.log("custom", this, start, end);
                return "xy";
            };
            const input: string = "abcdef";
            const start: number = 1;
            const end: number = 4;
            console.log(input.slice(start, end).length);
            """;

        Assembly assembly = Compile(source);
        MethodInfo main = assembly.GetType("$Program")!.GetMethod(
            "Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.DoesNotContain(ReadInstructions(main), instruction =>
            instruction.Operand is MethodBase { Name: "StringSliceLengthPrimitive" });
        Assert.Contains(ReadInstructions(main), instruction =>
            instruction.Operand is MethodBase { Name: "InvokeMethodValue" });
        Assert.Equal("custom abcdef 1 4\n2\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void StableAndFallbackShapesPassIlVerification()
    {
        const string source = IntrinsicFunctions + """
            async function asyncSliceLength(
                input: string, start: number, end: number): Promise<number> {
                return input.slice(start, end).length;
            }
            function* substringLengthGenerator(
                input: string, start: number, end: number): Generator<number> {
                yield input.substring(start, end).length;
            }
            const boxed: any = new String("abcdef");
            const localized = new Date(Date.UTC(2024, 0, 15))
                .toLocaleDateString("en-US", { timeZone: "UTC" });
            console.log(find("abcdef", "cd", 0));
            console.log(contains("abcdef", "cd", 0));
            console.log(takeSlice("abcdef", 1, 4));
            console.log(takeSubstring("abcdef", 4, 1));
            console.log(boxed.slice(1, 4));
            console.log(localized.includes("2024"));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("2\ntrue\nbcd\nbcd\nbcd\ntrue\n", output);
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"stable_primitive_string_{Guid.NewGuid():N}");
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
