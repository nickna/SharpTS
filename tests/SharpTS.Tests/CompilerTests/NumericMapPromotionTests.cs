using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NumericMapPromotionTests
{
    [Fact]
    public void ExactLocal_UsesTypedDictionaryAndDirectCallsWithoutNumericBoxing()
    {
        Assembly assembly = Compile(EligibleSource);
        MethodInfo method = FindFunction(assembly, "exercise");
        var instructions = ReadInstructions(method).ToArray();

        Assert.Contains(method.GetMethodBody()!.LocalVariables, local =>
            local.LocalType == typeof(Dictionary<double, double>));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "set_Item" }
            && IsDoubleDictionary(instruction.Operand.DeclaringType));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ContainsKey" }
            && IsDoubleDictionary(instruction.Operand.DeclaringType));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "Remove" }
            && IsDoubleDictionary(instruction.Operand.DeclaringType));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "Clear" }
            && IsDoubleDictionary(instruction.Operand.DeclaringType));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "get_Count" }
            && IsDoubleDictionary(instruction.Operand.DeclaringType));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "MapSet" or "MapHas" or "MapDelete" or "MapClear" or "MapSize"
            });
    }

    [Fact]
    public void ExactLocal_PreservesResultAndVerifiesIl()
    {
        var diagnostics = TestHarness.CompileAndVerifyOnly(EligibleSource);
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
        Assert.Equal("3\n", TestHarness.RunCompiled(EligibleSource));
        Assert.Equal("3\n", TestHarness.RunCompiledStandalone(EligibleSource));
    }

    [Fact]
    public void CountedNumericFill_ReservesTypedDictionaryCapacityOnce()
    {
        Assembly assembly = Compile(CountedFillSource);
        MethodInfo method = FindFunction(assembly, "fill");
        var instructions = ReadInstructions(method).ToArray();

        Assert.Single(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "EnsureCapacity" }
            && IsDoubleDictionary(instruction.Operand.DeclaringType));
        var diagnostics = TestHarness.CompileAndVerifyOnly(CountedFillSource);
        Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
        Assert.Equal("10000\n", TestHarness.RunCompiled(CountedFillSource));
    }

    [Theory]
    [MemberData(nameof(BailoutSources))]
    public void ObservableOrWidenedLifetime_RetainsObjectMap(string source)
    {
        Assembly assembly = Compile(source);
        MethodInfo method = FindFunction(assembly, "read");
        var instructions = ReadInstructions(method).ToArray();

        Assert.DoesNotContain(method.GetMethodBody()!.LocalVariables, local =>
            local.LocalType == typeof(Dictionary<double, double>));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "MapSet" });
    }

    public static TheoryData<string> BailoutSources => new()
    {
        // forEach observes order, callback arguments, and receiver identity.
        """
        function read(): number {
            const map = new Map<number, number>();
            map.set(1, 2);
            let sum: number = 0;
            map.forEach((value: number): void => { sum = sum + value; });
            return sum;
        }
        """,
        // Constructor entry iterables are outside the empty-intrinsic proof.
        """
        function read(): number {
            const map = new Map<number, number>([[1, 2]]);
            map.set(2, 3);
            return map.size;
        }
        """,
        // Identity observations require the ordinary JavaScript object.
        """
        function read(): number {
            const map = new Map<number, number>();
            map.set(1, 2);
            return map === map ? 1 : 0;
        }
        """,
        // A direct alias escapes the specialized slot.
        """
        function read(): number {
            const map = new Map<number, number>();
            map.set(1, 2);
            const alias: Map<number, number> = map;
            return alias.size;
        }
        """,
        // The result of set is the receiver and therefore an alias.
        """
        function read(): number {
            const map = new Map<number, number>();
            const alias: Map<number, number> = map.set(1, 2);
            return alias.size;
        }
        """,
        // Captures require an object-valued display-class field.
        """
        function read(): number {
            const map = new Map<number, number>();
            map.set(1, 2);
            const size = (): number => map.size;
            return size();
        }
        """,
        // Returning the Map exposes its representation and identity.
        """
        function read(): any {
            const map = new Map<number, number>();
            map.set(1, 2);
            return map;
        }
        """,
        // Unknown calls may retain or mutate the Map.
        """
        function consume(value: any): void {}
        function read(): number {
            const map = new Map<number, number>();
            map.set(1, 2);
            consume(map);
            return map.size;
        }
        """,
        // Computed method extraction can observe a replaced member and receiver.
        """
        function read(): number {
            const map = new Map<number, number>();
            map.set(1, 2);
            const get: any = (map as any)["get"];
            return get.call(map, 1);
        }
        """,
        // Intrinsic prototype mutation invalidates direct method dispatch globally.
        """
        function read(): number {
            const map = new Map<number, number>();
            const proto: any = Map.prototype;
            proto.set = null;
            map.set(1, 2);
            return map.size;
        }
        """,
        // A widened key can carry non-number JavaScript values.
        """
        function read(): number {
            const map = new Map<number, number>();
            const key: any = 1;
            map.set(key, 2);
            return map.size;
        }
        """,
        // A widened value cannot be stored in a native double slot.
        """
        function read(): number {
            const map = new Map<number, number>();
            const value: any = 2;
            map.set(1, value);
            return map.size;
        }
        """,
        // Direct eval can obtain and mutate bindings or intrinsic prototypes.
        """
        function read(): number {
            const map = new Map<number, number>();
            map.set(1, 2);
            eval("void 0");
            return map.size;
        }
        """
    };

    private const string EligibleSource = """
        function exercise(n: number): number {
            const map = new Map<number, number>();
            map.set(0, n);
            let score: number = 0;
            if (map.has(0)) score = score + 1;
            if (map.delete(0)) score = score + 1;
            map.set(2, 3);
            score = score + map.size;
            map.clear();
            return score + map.size;
        }
        console.log(exercise(7));
        """;

    private const string CountedFillSource = """
        function fill(n: number): number {
            const map = new Map<number, number>();
            for (let i: number = 0; i < n; i++) {
                map.set(i, i * 3 + 1);
            }
            return map.size;
        }
        console.log(fill(10000));
        """;

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1482_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static bool IsDoubleDictionary(Type? type) =>
        type?.IsGenericType == true
        && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        && type.GetGenericArguments() is [var key, var value]
        && key == typeof(double)
        && value == typeof(double);

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
