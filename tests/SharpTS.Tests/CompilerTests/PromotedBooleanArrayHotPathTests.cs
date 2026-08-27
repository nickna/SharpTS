using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class PromotedBooleanArrayHotPathTests
{
    [Fact]
    public void CountPrimes_UsesUnboxedListHotPaths()
    {
        Assembly assembly = Compile("""
            function countPrimes(n: number): number {
                if (n <= 2) return 0;
                const isPrime: boolean[] = [];
                for (let i: number = 0; i < n; i++) { isPrime.push(true); }
                isPrime[0] = false;
                isPrime[1] = false;
                for (let i: number = 2; i * i < n; i++) {
                    if (isPrime[i]) {
                        for (let j: number = i * i; j < n; j = j + i) {
                            isPrime[j] = false;
                        }
                    }
                }
                let count: number = 0;
                for (let i: number = 0; i < n; i++) {
                    if (isPrime[i]) count = count + 1;
                }
                return count;
            }
            """);

        MethodInfo countPrimes = FindFunction(assembly, "countPrimes");
        var instructions = ReadInstructions(countPrimes).ToArray();

        Assert.Contains(instructions, instruction => IsListBoolMethod(instruction, "Add"));
        Assert.Contains(instructions, instruction => IsListBoolMethod(instruction, "EnsureCapacity"));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "SetCount",
                DeclaringType: var declaringType
            }
            && declaringType == typeof(System.Runtime.InteropServices.CollectionsMarshal));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "Fill" });
        Assert.Contains(instructions, instruction => IsSpanBoolMethod(instruction, "get_Item"));
        Assert.Contains(instructions, instruction => IsListBoolMethod(instruction, "set_Item"));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(bool));
        Assert.True(
            countPrimes.GetMethodBody()!.LocalVariables.Count(local =>
                local.LocalType == typeof(long)) >= 3,
            "Expected the fill, outer sieve, and nested sieve counters to use Int64 locals.");
        Assert.Contains(
            countPrimes.GetMethodBody()!.LocalVariables,
            local => local.LocalType == typeof(Span<bool>));
    }

    [Fact]
    public void CountPrimes_PassesIlVerificationAndProducesExpectedResult()
    {
        const string source = """
            function countPrimes(n: number): number {
                const isPrime: boolean[] = [];
                for (let i: number = 0; i < n; i++) { isPrime.push(true); }
                isPrime[0] = false;
                isPrime[1] = false;
                for (let i: number = 2; i * i < n; i++) {
                    if (isPrime[i]) {
                        for (let j: number = i * i; j < n; j = j + i) {
                            isPrime[j] = false;
                        }
                    }
                }
                let count: number = 0;
                for (let i: number = 0; i < n; i++) {
                    if (isPrime[i]) count = count + 1;
                }
                return count;
            }
            console.log(countPrimes(100));
            """;

        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("25\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void HoistedBooleanSpan_RefreshesAfterIndexedGrowth()
    {
        const string source = """
            function grow(): number {
                const xs: boolean[] = [];
                xs.push(true);
                let seen: number = 0;
                for (let i: number = 0; i < 2; i++) {
                    const index: number = 5 + i;
                    xs[index] = true;
                    if (xs[index]) seen = seen + 1;
                }
                return xs.length * 10 + seen;
            }
            console.log(grow());
            """;

        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("72\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void ReceiverMutationInsideLoop_DisablesBooleanSpanHoist()
    {
        const string source = """
            function mutate(): number {
                const xs: boolean[] = [];
                xs.push(true);
                let seen: number = 0;
                for (let i: number = 0; i < 2; i++) {
                    xs.push(false);
                    if (xs[i]) seen = seen + 1;
                }
                return xs.length * 10 + seen;
            }
            console.log(mutate());
            """;

        Assembly assembly = Compile(source);
        MethodInfo mutate = FindFunction(assembly, "mutate");

        Assert.DoesNotContain(
            mutate.GetMethodBody()!.LocalVariables,
            local => local.LocalType == typeof(Span<bool>));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
        Assert.Equal("31\n", TestHarness.RunCompiled(source));
    }

    private static bool IsListBoolMethod(
        (OpCode OpCode, MemberInfo? Operand) instruction,
        string name) =>
        instruction.Operand is MethodBase method
        && method.Name == name
        && method.DeclaringType == typeof(List<bool>);

    private static bool IsSpanBoolMethod(
        (OpCode OpCode, MemberInfo? Operand) instruction,
        string name) =>
        instruction.Operand is MethodBase method
        && method.Name == name
        && method.DeclaringType == typeof(Span<bool>);

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"promoted_bool_hot_path_{Guid.NewGuid():N}");
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
