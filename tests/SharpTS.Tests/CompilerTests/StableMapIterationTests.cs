using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableMapIterationTests
{
    [Fact]
    public void StableNumericEntryReads_UsePromotedDictionaryEnumerator()
    {
        Assembly assembly = Compile(StableSource);
        MethodInfo method = FindFunction(assembly, "sumMap");
        var instructions = ReadInstructions(method).ToArray();

        Assert.Contains(method.GetMethodBody()!.LocalVariables, local =>
            IsDoubleDictionary(local.LocalType));

        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "GetEnumerator",
                DeclaringType: { IsGenericType: true } declaringType
            }
            && IsDoubleDictionary(declaringType));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "MapEntries" or "NormalizeToEnumerator" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is ConstructorInfo
            {
                DeclaringType: { IsGenericType: true } declaringType
            }
            && declaringType.GetGenericTypeDefinition() == typeof(List<>));
        Assert.DoesNotContain(instructions, instruction =>
            (instruction.OpCode == OpCodes.Box || instruction.OpCode == OpCodes.Unbox_Any)
            && instruction.Operand == typeof(double));
    }

    [Fact]
    public void BenchmarkShape_UsesTypedStorageAndCapacityReservation()
    {
        Assembly assembly = Compile(BenchmarkShapeSource);
        MethodInfo method = FindFunction(assembly, "mapIteration");
        var instructions = ReadInstructions(method).ToArray();

        Assert.Contains(method.GetMethodBody()!.LocalVariables, local =>
            IsDoubleDictionary(local.LocalType));
        Assert.Single(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "EnsureCapacity" } capacity
            && IsDoubleDictionary(capacity.DeclaringType));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "set_Item" } setItem
            && IsDoubleDictionary(setItem.DeclaringType));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetEnumerator" } getEnumerator
            && IsDoubleDictionary(getEnumerator.DeclaringType));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "MapSet" or "MapEntries" or "NormalizeToEnumerator"
            });
        Assert.DoesNotContain(instructions, instruction =>
            (instruction.OpCode == OpCodes.Box || instruction.OpCode == OpCodes.Unbox_Any)
            && instruction.Operand == typeof(double));

        Assert.Equal("20000\n", TestHarness.RunCompiled(BenchmarkShapeSource));
    }

    [Fact]
    public void StableNumericEntryReads_PreserveResultAndVerifyIl()
    {
        Assert.Equal("18\n", TestHarness.RunCompiled(StableSource));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(StableSource));
    }

    [Fact]
    public void StableNumericEntryReads_WorkInStandaloneOutput()
    {
        Assert.Equal("18\n", TestHarness.RunCompiledStandalone(StableSource));
    }

    [Fact]
    public void PromotedIteration_PreservesSameValueZeroAndInsertionOrder()
    {
        const string source = """
            function inspect(): string {
                const map = new Map<number, number>();
                map.set(-0, 1);
                map.set(0, 2);
                map.set(NaN, 3);
                map.set(NaN, 4);
                map.set(2, 20);
                map.set(1, 10);
                map.set(2, 21);

                let trace: string = "";
                for (const entry of map) {
                    if (Number.isNaN(entry[0])) {
                        trace = trace + "n:" + entry[1] + ";";
                    } else {
                        trace = trace + entry[0] + ":" + entry[1] + ";";
                    }
                }
                return trace + "size=" + map.size;
            }
            console.log(inspect());
            """;

        const string expected = "0:2;n:4;2:21;1:10;size=4\n";
        Assert.Equal(expected, TestHarness.RunCompiled(source));
        Assert.Equal(expected, TestHarness.RunCompiledStandalone(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void ReceiverAlias_RetainsMaterializedIteratorPath()
    {
        AssertUsesFallback("""
            function read(): number {
                const map = new Map<number, number>();
                map.set(1, 2);
                const alias: any = map;
                let sum: number = 0;
                for (const entry of map) sum = sum + entry[0] + entry[1];
                return sum + alias.size - alias.size;
            }
            """);
    }

    [Fact]
    public void ReceiverAliasThroughSetResult_RetainsMaterializedIteratorPath()
    {
        AssertUsesFallback("""
            function read(): number {
                const map = new Map<number, number>();
                const alias: Map<number, number> = map.set(1, 2);
                let sum: number = 0;
                for (const entry of map) sum = sum + entry[0] + entry[1];
                return sum + alias.size - alias.size;
            }
            """);
    }

    [Fact]
    public void EntryEscape_RetainsMaterializedIteratorPath()
    {
        AssertUsesFallback("""
            function consume(value: any): number { return value[0] + value[1]; }
            function read(): number {
                const map = new Map<number, number>();
                map.set(1, 2);
                let sum: number = 0;
                for (const entry of map) sum = sum + consume(entry);
                return sum;
            }
            """);
    }

    [Fact]
    public void DynamicEntryIndex_RetainsMaterializedIteratorPath()
    {
        AssertUsesFallback("""
            function read(index: number): number {
                const map = new Map<number, number>();
                map.set(1, 2);
                let sum: number = 0;
                for (const entry of map) sum = sum + entry[index];
                return sum;
            }
            """);
    }

    [Fact]
    public void DestructuredEntry_RetainsIteratorSemantics()
    {
        AssertUsesFallback("""
            function read(): number {
                const map = new Map<number, number>();
                map.set(1, 2);
                let sum: number = 0;
                for (const [key, value] of map) sum = sum + key + value;
                return sum;
            }
            """);
    }

    [Fact]
    public void ShadowedEntryBinding_RetainsIteratorSemantics()
    {
        AssertUsesFallback("""
            function read(): number {
                const map = new Map<number, number>();
                map.set(1, 2);
                let sum: number = 0;
                for (const entry of map) {
                    {
                        const entry: number[] = [10, 20];
                        sum = sum + entry[0] + entry[1];
                    }
                }
                return sum;
            }
            """);
    }

    [Fact]
    public void DynamicMap_RetainsMaterializedIteratorPath()
    {
        AssertUsesFallback("""
            function read(): number {
                const map: any = new Map<number, number>();
                map.set(1, 2);
                let sum: number = 0;
                for (const entry of map) sum = sum + entry[0] + entry[1];
                return sum;
            }
            """);
    }

    [Fact]
    public void MutationDuringIteration_RetainsLiveDeletionBehavior()
    {
        const string source = """
            const map = new Map<number, number>();
            map.set(1, 10);
            map.set(2, 20);
            map.set(3, 30);
            let seen: string = "";
            for (const entry of map) {
                seen = seen + entry[0];
                if (entry[0] === 1) map.delete(2);
            }
            console.log(seen);
            """;

        Assert.Equal("13\n", TestHarness.RunCompiled(source));

        Assembly assembly = Compile(source);
        var entryPoint = assembly.EntryPoint!;
        Assert.Contains(ReadInstructions(entryPoint), instruction =>
            instruction.Operand is MethodBase { Name: "MapEntries" });
    }

    [Fact]
    public void InsertAndClearDuringIteration_RetainMaterializedIteratorPath()
    {
        AssertUsesFallback("""
            function read(): number {
                const map = new Map<number, number>();
                map.set(1, 10);
                map.set(2, 20);
                let sum: number = 0;
                for (const entry of map) {
                    sum = sum + entry[0];
                    if (entry[0] === 1) map.set(3, 30);
                    if (entry[0] === 2) map.clear();
                }
                return sum;
            }
            """);
    }

    [Fact]
    public void CustomIteratorAlias_RetainsMaterializedIteratorPath()
    {
        AssertUsesFallback("""
            function read(): number {
                const map = new Map<number, number>();
                map.set(1, 2);
                const observable: any = map;
                observable[Symbol.iterator] = () => [][Symbol.iterator]();
                let sum: number = 0;
                for (const entry of map) sum = sum + entry[0] + entry[1];
                return sum;
            }
            """);
    }

    [Fact]
    public void StableEntryReads_PreserveEvaluationOrderAndExceptions()
    {
        const string source = """
            let trace: string = "";
            function note(label: string, value: number): number {
                trace = trace + label + value;
                return value;
            }
            function read(): number {
                const map = new Map<number, number>();
                map.set(1, 10);
                map.set(2, 20);
                let sum: number = 0;
                try {
                    for (const entry of map) {
                        sum = sum + note("k", entry[0]) + note("v", entry[1]);
                        if (entry[0] === 2) throw "stop";
                    }
                } catch (error) {
                    trace = trace + ":caught";
                }
                return sum;
            }
            console.log(read());
            console.log(trace);
            """;

        Assert.Equal("33\nk1v10k2v20:caught\n", TestHarness.RunCompiled(source));

        Assembly assembly = Compile(source);
        Assert.DoesNotContain(ReadInstructions(FindFunction(assembly, "read")), instruction =>
            instruction.Operand is MethodBase { Name: "MapEntries" or "NormalizeToEnumerator" });
    }

    [Fact]
    public void DirectEval_RetainsMaterializedIteratorPath()
    {
        AssertUsesFallback("""
            function read(): number {
                const map = new Map<number, number>();
                map.set(1, 2);
                eval("void 0");
                let sum: number = 0;
                for (const entry of map) sum = sum + entry[0] + entry[1];
                return sum;
            }
            """);
    }

    private static void AssertUsesFallback(string source)
    {
        Assembly assembly = Compile(source);
        var instructions = ReadInstructions(FindFunction(assembly, "read")).ToArray();
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "MapEntries" });
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "NormalizeToEnumerator" });
    }

    private const string StableSource = """
        function sumMap(): number {
            const map = new Map<number, number>();
            for (let i: number = 0; i < 3; i++) {
                map.set(i, i * 3 + 1);
            }
            let sum: number = 0;
            for (const entry of map) {
                sum = sum + entry[0] + entry[1];
            }
            return sum + map.size;
        }
        console.log(sumMap());
        """;

    private const string BenchmarkShapeSource = """
        function mapIteration(n: number): number {
            const map = new Map<number, number>();
            for (let i: number = 0; i < n; i++) {
                map.set(i, i * 3 + 1);
            }

            let sum: number = 0;
            for (const entry of map) {
                sum = sum + entry[0] + entry[1];
            }
            return sum + map.size;
        }
        console.log(mapIteration(100));
        """;

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1435_{Guid.NewGuid():N}");
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
