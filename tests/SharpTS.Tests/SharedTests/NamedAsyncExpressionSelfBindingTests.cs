using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class NamedAsyncExpressionSelfBindingTests
{
    public const string IssueSource = """
        const target: any = async function self() { return self; };
        target().then((value: any) => console.log(value === target));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["named-async-private-self-issue", IssueSource, "true\n"];
        yield return ["private-name-shadows-outer-but-parameters-and-body-bindings-win", """
            const target: any = async function shadowed() { return shadowed; };
            const shadowed = 41;
            const parameter: any = async function self(self: any) { return self; };
            const body: any = async function self() { const self = 13; return self; };
            const privateOnly: any = async function onlyInside() { return onlyInside; };
            console.log(Object.hasOwn(globalThis, 'onlyInside'), typeof shadowed, shadowed);
            target().then((value: any) => {
                console.log(value === target, typeof shadowed, shadowed);
                return parameter('argument');
            }).then((value: any) => { console.log(value); return body(); })
                .then((value: any) => { console.log(value); return privateOnly(); })
                .then((value: any) => console.log(value === privateOnly, Object.hasOwn(globalThis, 'onlyInside')))
                .catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "false number 41\ntrue number 41\nargument\n13\ntrue false\n"];
        yield return ["bounded-private-recursion-returns-canonical-self", """
            function recursiveSelf(): any { return 'outer'; }
            const target: any = async function recursiveSelf(depth: number): Promise<any> {
                if (depth === 0) return recursiveSelf;
                return recursiveSelf(depth - 1);
            };
            target(3).then((value: any) => console.log(value === target, recursiveSelf()))
                .catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "true outer\n"];
        yield return ["factory-expressions-return-their-own-distinct-private-values", """
            function make(): any { return async function self() { return self; }; }
            const first = make(), second = make();
            console.log(first !== second);
            first().then((value: any) => {
                console.log(value === first, value !== second);
                return second();
            }).then((value: any) => console.log(value === second, value !== first))
                .catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "true\ntrue true\ntrue true\n"];
        yield return ["call-bind-and-reflect-receivers-keep-canonical-private-self", """
            const target: any = async function self(this: any, value: number): Promise<any> {
                return [self, this.label, value, arguments.length];
            };
            const bound = target.bind({ label: 'bound' }, 7);
            target.call({ label: 'call' }, 1).then((value: any) => {
                console.log(value[0] === target, value[1], value[2], value[3]);
                return bound();
            }).then((value: any) => {
                console.log(value[0] === target, value[1], value[2], value[3]);
                return Reflect.apply(target, { label: 'reflect' }, [3]);
            }).then((value: any) => console.log(value[0] === target, value[1], value[2], value[3]))
                .catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "true call 1 1\ntrue bound 7 1\ntrue reflect 3 1\n"];
        yield return ["nested-async-construction-relays-private-self-to-an-arrow-across-await", """
            async function outer(): Promise<any> {
                return async function self(): Promise<any> {
                    const read = () => self;
                    const readAsync = async () => self;
                    await Promise.resolve();
                    return [read(), await readAsync()];
                };
            }
            outer().then((target: any) => target().then((value: any) =>
                console.log(value[0] === target, value[1] === target)))
                .catch((error: any) => console.log('unexpected', error.name, error.message));
            """, "true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> SavedCases() => Cases().Concat(
        AsyncCallableOwnPropertyDeletionTests.Cases().Where(row => (string)row[0] == "receiver-views-share-state-and-arrow-this"));

    [Theory, MemberData(nameof(ModeCases))]
    public void NamedAsyncExpressionsUseTheirPrivateSelfBinding(
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
