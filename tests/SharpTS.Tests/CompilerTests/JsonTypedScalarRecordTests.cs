using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>Regression coverage for #1414 closed JSON scalar records.</summary>
public sealed class JsonTypedScalarRecordTests
{
    private const string Source = """
        type Item = { id: number; name: string; value: number };
        type Payload = { items: Item[] };

        function roundTrip(n: number): number {
            const items: Item[] = [];
            for (let i: number = 0; i < n; i++) {
                items.push({ id: i, name: "item-" + i, value: i * 3 - 1 });
            }
            const payload: Payload = { items: items };
            const json: string = JSON.stringify(payload);
            const parsed: any = JSON.parse(json);
            const back: Item[] = parsed.items;
            let sum: number = 0;
            for (let i: number = 0; i < back.length; i++) {
                sum = sum + back[i].value;
            }
            return sum;
        }

        console.log(roundTrip(4));
        """;

    [Fact]
    public void ClosedRecordUsesNativeFieldsAcrossLiteralStringifyAndParse()
    {
        Assembly assembly = Compile(Source);
        Type itemCarrier = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("$JsonTypedScalarRecord", StringComparison.Ordinal) &&
            type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Count(field => field.Name.StartsWith("_v", StringComparison.Ordinal)) == 3);
        Type[] fieldTypes = itemCarrier
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(field => field.Name.StartsWith("_v", StringComparison.Ordinal))
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .Select(field => field.FieldType)
            .ToArray();
        Assert.Equal([typeof(double), typeof(string), typeof(double)], fieldTypes);

        MethodInfo parser = assembly.GetType("$Runtime")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method =>
                method.Name.StartsWith("ParseJsonTypedScalarRecord", StringComparison.Ordinal) &&
                ReadMembers(method).Any(member =>
                    member.OpCode == OpCodes.Newobj &&
                    member.Member?.DeclaringType == itemCarrier));
        Assert.DoesNotContain(ReadMembers(parser), member => member.OpCode == OpCodes.Box);

        MethodInfo appender = assembly.GetType("$Runtime")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method =>
                method.Name.StartsWith("AppendJsonTypedScalarRecord", StringComparison.Ordinal) &&
                method.GetParameters()[1].ParameterType == itemCarrier);
        Assert.DoesNotContain(ReadMembers(appender), member => member.OpCode == OpCodes.Box);

        MethodInfo roundTrip = FindFunction(assembly, "roundTrip");
        var roundTripMembers = ReadMembers(roundTrip).ToArray();
        int typedRead = Array.FindIndex(roundTripMembers, member =>
            member.OpCode == OpCodes.Ldfld &&
            member.Member?.DeclaringType == itemCarrier &&
            member.Member is FieldInfo { FieldType: var type } && type == typeof(double));
        Assert.True(typedRead >= 0);
        Assert.NotEqual(OpCodes.Box, roundTripMembers[typedRead + 1].OpCode);
        Assert.Equal("14\n", TestHarness.RunCompiled(Source));
    }

    [Fact]
    public void DynamicMutationMaterializesAndPreservesJsonObjectSemantics()
    {
        const string source = """
            type Item = { id: number; name: string; value: number };
            const item: Item = { id: 1, name: "a", value: 2 };
            const json: string = JSON.stringify(item);
            const parsed: any = JSON.parse(json);
            parsed.value = "changed";
            delete parsed.name;
            Object.defineProperty(parsed, "extra", {
                value: true, enumerable: true, configurable: true
            });
            console.log(parsed.value, parsed.name, JSON.stringify(parsed));
            """;

        Assert.Equal(
            "changed undefined {\"id\":1,\"value\":\"changed\",\"extra\":true}\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void OnlyTheAssociatedStringUsesTheTypedParser()
    {
        const string source = """
            type Item = { id: number; name: string; value: number };
            function associated(): any {
                const item: Item = { id: 1, name: "a", value: 2 };
                const json: string = JSON.stringify(item);
                return JSON.parse(json);
            }
            function copied(): any {
                const item: Item = { id: 1, name: "a", value: 2 };
                const json: string = JSON.stringify(item);
                const copy: string = (" " + json).slice(1);
                return JSON.parse(copy);
            }
            function associatedArray(): any {
                const value: { items: Item[] } = {
                    items: [{ id: 1, name: "a", value: 2 }]
                };
                const json: string = JSON.stringify(value);
                return JSON.parse(json).items;
            }
            """;

        Assembly assembly = Compile(source);
        object associated = FindFunction(assembly, "associated").Invoke(null, null)!;
        object copied = FindFunction(assembly, "copied").Invoke(null, null)!;
        object associatedArray =
            FindFunction(assembly, "associatedArray").Invoke(null, null)!;
        Assert.StartsWith("$JsonTypedScalarRecord", associated.GetType().Name);
        Assert.IsType<Dictionary<string, object>>(copied);
        Assert.IsType<List<object>>(associatedArray);
    }

    [Fact]
    public void ClosedRecordWithNumericArrayPassesIlVerification()
    {
        const string source = """
            const value: { a: number; values: number[] } = {
                a: 0.1 + 0.2,
                values: [NaN, Infinity]
            };
            console.log(JSON.stringify(value));
            """;

        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1414_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static IEnumerable<(OpCode OpCode, MemberInfo? Member)> ReadMembers(MethodInfo method)
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
            MemberInfo? member = null;
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineField or
                OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                member = opCode.OperandType switch
                {
                    OperandType.InlineMethod => module.ResolveMethod(token),
                    OperandType.InlineField => module.ResolveField(token),
                    _ => module.ResolveType(token)
                };
            }
            int operandSize = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                    OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or
                    OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException(
                    $"Unsupported IL operand type {opCode.OperandType}.")
            };
            offset += operandSize;
            yield return (opCode, member);
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
}
