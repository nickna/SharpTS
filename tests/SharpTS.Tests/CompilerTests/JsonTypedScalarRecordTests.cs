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

        MethodInfo associatedParser = assembly.GetType("$Runtime")!
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method =>
                method.Name.StartsWith("ParseJsonAssociatedRecord", StringComparison.Ordinal) &&
                ReadMembers(method).Any(member =>
                    member.OpCode == OpCodes.Newobj &&
                    member.Member?.DeclaringType == itemCarrier));
        var associatedMembers = ReadMembers(associatedParser).ToArray();
        Assert.DoesNotContain(associatedMembers, member => member.OpCode == OpCodes.Box);
        Assert.DoesNotContain(associatedMembers, member =>
            member.Member?.DeclaringType == typeof(System.Text.Encoding) ||
            member.Member?.DeclaringType == typeof(System.Text.Json.Utf8JsonReader));

        MethodInfo parseJsonValue = assembly.GetType("$Runtime")!
            .GetMethod("ParseJsonValue", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Contains(ReadMembers(parseJsonValue), member =>
            member.Member?.Name == "TryParseJsonAssociated");

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
    public void ClosedRecordArraySelectsExactWriterOutsideTheElementLoop()
    {
        Assembly assembly = Compile(Source);
        Type runtime = assembly.GetType("$Runtime")!;
        Type itemCarrier = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("$JsonTypedScalarRecord", StringComparison.Ordinal) &&
            type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Count(field => field.Name.StartsWith("_v", StringComparison.Ordinal)) == 3);
        MethodInfo recordAppender = runtime
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method =>
                method.Name.StartsWith("AppendJsonTypedScalarRecord", StringComparison.Ordinal) &&
                method.GetParameters()[1].ParameterType == itemCarrier);
        MethodInfo arrayAppender = runtime
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method =>
                method.Name.StartsWith("AppendJsonTypedScalarRecordArray", StringComparison.Ordinal) &&
                ReadMembers(method).Any(member => member.Member == recordAppender));

        var members = ReadMembers(arrayAppender).ToArray();
        Assert.Contains(members, member => member.Member == recordAppender);
        Assert.DoesNotContain(members, member =>
            member.Member?.Name == "AppendJsonShapedValue" ||
            member.Member?.Name == "PDSHasPropertyDescriptors" ||
            member.Member?.Name == "PDSHasPrototypeEntry");
        Assert.DoesNotContain(members, member =>
            member.Member?.DeclaringType == typeof(string) &&
            member.Member.Name == "op_Equality");
        Assert.DoesNotContain(members, member => member.OpCode == OpCodes.Box);

        MethodInfo dispatcher = runtime.GetMethod(
            "AppendJsonShapedValue",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Contains(ReadMembers(dispatcher), member => member.Member == arrayAppender);
    }

    [Fact]
    public void ClosedRecordArrayFallsBackAfterElementMaterialization()
    {
        const string source = """
            type Item = { id: number; name: string; value: number };
            const items: Item[] = [
                { id: 1, name: "a", value: 2 },
                { id: 3, name: "b", value: 4 }
            ];
            const changed: any = items[1];
            changed.value = "changed";
            delete changed.name;
            changed.extra = true;
            console.log(JSON.stringify({ items: items }));
            """;

        Assert.Equal(
            "{\"items\":[{\"id\":1,\"name\":\"a\",\"value\":2},{\"id\":3,\"value\":\"changed\",\"extra\":true}]}\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void ClosedRecordArrayFallsBackForDescriptorAndPrototypeSemantics()
    {
        const string source = """
            type Item = { id: number; name: string; value: number };
            const first: Item = { id: 1, name: "a", value: 2 };
            const second: Item = { id: 3, name: "b", value: 4 };
            Object.defineProperty(second, "value", {
                get: (): number => 99,
                enumerable: true,
                configurable: true
            });
            Object.setPrototypeOf(first, {
                toJSON: function(): any { return { observed: this.name }; }
            });
            console.log(JSON.stringify({ items: [first, second] }));
            """;

        Assert.Equal(
            "{\"items\":[{\"observed\":\"a\"},{\"id\":3,\"name\":\"b\",\"value\":99}]}\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void IntegerCounterStringConcatFormatsIntoTheFinalString()
    {
        const string source = """
            function labels(): string {
                const values: string[] = [];
                for (let i: number = -2; i < 3; i++) {
                    values.push("item-" + i);
                    values.push(i + "!");
                }
                return values.join("|");
            }
            console.log(labels());
            """;

        Assembly assembly = Compile(source);
        MethodInfo labels = FindFunction(assembly, "labels");
        var members = ReadMembers(labels).ToArray();
        Assert.Contains(members, member => member.Member?.Name == "ConcatStringInt64");
        Assert.DoesNotContain(members, member =>
            member.Member?.DeclaringType?.Name == "$Runtime" &&
            member.Member.Name is ("Add" or "FormatNumber"));
        Assert.DoesNotContain(members, member => member.OpCode == OpCodes.Box);
        Assert.Equal(
            "item--2|-2!|item--1|-1!|item-0|0!|item-1|1!|item-2|2!\n",
            TestHarness.RunCompiled(source));
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

    [Fact]
    public void AssociatedParserHandlesEscapedStringsBooleansAndRecordArrays()
    {
        const string source = """
            type Item = { id: number; name: string; enabled: boolean };
            type Payload = { items: Item[] };
            const name: string = "quote\" slash\\ line\n snowman ☃";
            const populated: Payload = {
                items: [
                    { id: -1.25, name: name, enabled: true },
                    { id: 1e100, name: "exponent", enabled: false },
                    { id: 9007199254740992, name: "wide", enabled: true }
                ]
            };
            const populatedJson: string = JSON.stringify(populated);
            const populatedBack: any = JSON.parse(populatedJson);
            const populatedAgain: any = JSON.parse(populatedJson);
            const empty: Payload = { items: [] };
            const emptyJson: string = JSON.stringify(empty);
            const emptyBack: any = JSON.parse(emptyJson);
            console.log(
                populatedBack.items[0].id,
                populatedBack.items[0].name === name,
                populatedBack.items[0].enabled,
                populatedBack.items[1].id === 1e100,
                populatedBack.items[1].enabled,
                populatedBack.items[2].id === 9007199254740992,
                populatedBack !== populatedAgain,
                populatedBack.items !== populatedAgain.items,
                populatedBack.items[0] !== populatedAgain.items[0],
                emptyBack.items.length
            );
            """;

        Assert.Equal(
            "-1.25 true true true false true true true true 0\n",
            TestHarness.RunCompiled(source));
        var errors = TestHarness.CompileAndVerifyOnly(source);
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void ReviverBypassesAssociatedParserAndWalksOrdinaryGraph()
    {
        const string source = """
            type Item = { id: number; name: string; value: number };
            type Payload = { items: Item[] };
            const payload: Payload = {
                items: [{ id: 1, name: "a", value: 2 }]
            };
            const json: string = JSON.stringify(payload);
            const parsed: any = JSON.parse(
                json,
                (key: string, value: any): any =>
                    key === "value" ? value + 1 : value
            );
            console.log(parsed.items[0].value);
            """;

        Assert.Equal("3\n", TestHarness.RunCompiled(source));
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
