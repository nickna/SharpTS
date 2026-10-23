using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ObjectDefinitionRuntimeReachabilityTests
{
    public const string AliasSource = """
        const ObjectAlias: any = Object, ReflectAlias: any = Reflect;
        const { defineProperties: defineMany } = Object;
        const defineOne: any = ObjectAlias.defineProperty;
        const reflectDefine: any = ReflectAlias.defineProperty;
        const key = Symbol('slot'), target: any = {};
        defineOne(target, 'first', { value: 3, enumerable: true });
        defineMany(target, { [key]: { value: 7, enumerable: true } });
        console.log(reflectDefine(target, 'last', { value: 9, enumerable: true }),
            target.first, target[key], target.last);
        console.log(Object.getOwnPropertySymbols(target)[0] === key);
        """;

    public const string ConstructorSource = """
        const {constructor:{defineProperty:d}} = {} as any;
        const t:any={};
        d(t,'x',{value:7});
        console.log(t.x);
        const constructor: any = ({}).constructor;
        const method = 'define' + 'Properties';
        const symbol = Symbol('selected'), target: any = {};
        constructor[method](target, { [symbol]: { value: 11 } });
        console.log(target[symbol]);
        """;

    public const string LiteralAccessorSource = """
        let stored = 2;
        const events: string[] = [];
        const target = {
            get value() { events.push('get'); return stored; },
            set value(next: number) { events.push('set:' + next); stored = next; }
        };
        console.log(target.value);
        target.value = 7;
        console.log(target.value, stored, events.join(','));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["held-destructured-and-aliased-object-reflect-mutators", AliasSource, "true 3 7 9\ntrue\n"];
        yield return ["constructor-derived-destructured-and-computed-mutators", ConstructorSource, "7\n11\n"];
        yield return ["literal-accessors-retain-descriptor-storage-without-definition-api", LiteralAccessorSource, "2\n7 7 get,set:7,get\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DefinitionRuntimeRemainsReachableThroughSupportedReferences(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
