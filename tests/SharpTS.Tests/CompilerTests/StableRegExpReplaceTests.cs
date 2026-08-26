using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableRegExpReplaceTests
{
    private const string ReplaceLoopSource = """
        function replaceLoop(input: string, n: number): number {
            let total: number = 0;
            for (let i: number = 0; i < n; i++) {
                total = total + input.replace(/foo/g, "bar").length;
            }
            return total;
        }
        """;

    [Fact]
    public void StableLiteralReplace_UsesTypedHelperWithoutProtocolDispatch()
    {
        MethodInfo replaceLoop = FindFunction(Compile(ReplaceLoopSource), "replaceLoop");
        var instructions = ReadInstructions(replaceLoop).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Call
            && instruction.Operand is MethodBase { Name: "StableRegExpReplace" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "StringReplaceRegExp" or "StringTryInvokeSymbolMethod" or
                    "InvokeMethodValue"
            });
    }

    [Fact]
    public void StableLiteralReplace_AllocatesOnlyNearTheResultStringCost()
    {
        MethodInfo replaceLoopMethod = FindFunction(Compile(ReplaceLoopSource), "replaceLoop");
        var replaceLoop = replaceLoopMethod.CreateDelegate<Func<string, double, double>>();
        const string input = "foo bar foo baz foo qux";

        Assert.Equal(input.Length * 10, replaceLoop(input, 10));
        _ = replaceLoop(input, 10_000);

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(input.Length * 100_000, replaceLoop(input, 100_000));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= 16_000_000,
            $"Stable RegExp replace allocated {allocated:N0} bytes for 100,000 calls.");
    }

    [Theory, ModeData]
    public void StableLiteralReplace_PreservesCapturesTokensAndMatchCardinality(
        ExecutionMode mode)
    {
        const string source = """
            console.log("aba".replace(/a/g, "x"));
            console.log("aba".replace(/a/, "x"));
            console.log("abc".replace(/(b)/, "[$$][$&][$1][$`][$']"));
            console.log("ab".replace(/(?<letter>[a-z])/g, "<$<letter>>"));
            console.log("ab".replace(/(?:)/g, "-"));
            """;

        Assert.Equal(
            "xbx\nxba\na[$][b][b][a][c]c\n<a><b>\n-a-b-\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CallbackReplacement_PreservesArgumentsAndNamedGroups(ExecutionMode mode)
    {
        const string source = """
            const result: string = "ab".replace(
                /(?<letter>[a-z])/g,
                function(match: string, capture: string, index: number,
                    input: string, groups: any): string {
                    console.log(match, capture, index, input, groups.letter);
                    return capture.toUpperCase();
                });
            console.log(result);
            """;

        Assert.Equal("a a 0 ab a\nb b 1 ab b\nAB\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void RegExpPrototypeMutation_DisablesStableHelperAndRemainsObservable()
    {
        const string source = """
            RegExp.prototype[Symbol.replace] = function(
                input: string, replacement: any): string {
                console.log("custom", input, replacement);
                return "mutated";
            };
            console.log("foo".replace(/foo/g, "bar"));
            """;

        Assembly assembly = Compile(source);
        MethodInfo main = assembly.GetType("$Program")!.GetMethod(
            "Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.DoesNotContain(ReadInstructions(main), instruction =>
            instruction.Operand is MethodBase { Name: "StableRegExpReplace" });
        Assert.Equal("custom foo bar\nmutated\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void StableAndFallbackReplaceShapesPassIlVerification()
    {
        const string source = ReplaceLoopSource + """
            const custom: any = {
                [Symbol.replace]: function(input: string, replacement: any): string {
                    return input + ":" + replacement;
                }
            };
            const callback: any = function(match: string): string { return match; };
            console.log(replaceLoop("foo", 2));
            console.log("x".replace(custom, "y"));
            console.log("x".replace(/x/, callback));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("6\nx:y\nx\n", output);
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"stable_regexp_replace_{Guid.NewGuid():N}");
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
