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
    public void GenericFallback_PreservesBoxingCoercionAndRegExpRejection(
        ExecutionMode mode)
    {
        const string source = """
            const boxed: any = new String("abcdef");
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
            "2\ntrue\nbcd\nbcd\n2\nneedle,position\ntrue\n",
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
    public void StableAndFallbackShapesPassIlVerification()
    {
        const string source = IntrinsicFunctions + """
            const boxed: any = new String("abcdef");
            console.log(find("abcdef", "cd", 0));
            console.log(contains("abcdef", "cd", 0));
            console.log(takeSlice("abcdef", 1, 4));
            console.log(takeSubstring("abcdef", 4, 1));
            console.log(boxed.slice(1, 4));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("2\ntrue\nbcd\nbcd\nbcd\n", output);
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
