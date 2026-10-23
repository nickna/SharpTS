using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class DeletedFunctionDefaultMetadataTests
{
    public const string IssueSource = """
        function original(first: any, second: any) {}
        const target: any = original;
        console.log(delete target.length, target.length, Reflect.get(target, "length"));
        console.log(delete target.name, target.name === "", Reflect.get(target, "name") === "");
        """;

    public const string InterpretedPositiveSource = """
        function declaration(first: any, second: any) { return first; }
        const arrow: any = (first: any, second: any) => first;
        async function asyncDeclaration(first: any, second: any) { return first; }
        const asyncExpression: any = async function namedAsync(first: any, second: any) { return first; };
        const asyncArrow: any = async (first: any, second: any) => first;
        for (const target of [declaration as any, arrow, asyncDeclaration as any, asyncExpression, asyncArrow]) {
            console.log(target.length, Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'));
            console.log(Reflect.deleteProperty(target, 'name'), delete target['length'], target.name === '',
                target['length'], Reflect.get(target, 'name') === '', Reflect.get(target, 'length'));
            console.log(Object.hasOwn(target, 'name'), Object.getOwnPropertyDescriptor(target, 'length') === undefined,
                Object.getOwnPropertyNames(target).includes('name'), Object.getOwnPropertyNames(target).includes('length'));
        }
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-default-inherited-issue", IssueSource, "true 0 0\ntrue true true\n"];
        yield return ["own-replacement-deletion-and-recreation", """
            function original(first: any, second: any) {}
            const target: any = original;
            console.log(target.length, target.name === 'original', Object.getPrototypeOf(target) === Function.prototype);
            Object.defineProperties(target, {
                name: { value: 'before', configurable: true },
                length: { value: 9, configurable: true }
            });
            console.log(target.name, target['length'], Reflect.get(target, 'name'), Reflect.get(target, 'length'));
            console.log(delete target.name, delete target.length, target.name === '', target.length);
            Object.defineProperties(target, {
                name: { value: 'after', writable: true, configurable: true },
                length: { value: 4, writable: true, configurable: true }
            });
            console.log(target.name, target.length, Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'));
            console.log(Reflect.deleteProperty(target, 'name'), Reflect.deleteProperty(target, 'length'),
                target['name'] === '', target['length']);
            """, "2 true true\nbefore 9 before 9\ntrue true true 0\nafter 4 true true\ntrue true true 0\n"];
        yield return ["independent-functions-aliases-and-own-absence", """
            function first(one: any, two: any) { return 7; }
            function second(one: any) { return 8; }
            const target: any = first, alias: any = target, other: any = second;
            console.log(delete alias.length, delete alias.name);
            console.log(target.length, target.name === '', other.length, other.name === 'second');
            console.log(Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'),
                Object.getOwnPropertyDescriptor(target, 'name') === undefined,
                Reflect.getOwnPropertyDescriptor(target, 'length') === undefined,
                Object.getOwnPropertyNames(target).includes('name'), Object.getOwnPropertyNames(target).includes('length'));
            const prototype: any = Function.prototype;
            console.log(prototype.length, prototype.name === '', Object.hasOwn(prototype, 'length'),
                Object.hasOwn(prototype, 'name'), 'name' in target, 'length' in target);
            console.log(alias === first, target(), other());
            Object.defineProperties(target, {
                name: { value: 'replacement', configurable: true },
                length: { value: 3, configurable: true }
            });
            console.log(alias.name, alias.length, other.name === 'second', other.length);
            """, "true true\n0 true 1 true\nfalse false true true false false\n0 true true true true true\ntrue 7 8\nreplacement 3 true 1\n"];
        yield return ["synchronous-and-async-callable-default-chains", """
            function declaration(first: any, second: any) { return first; }
            const expression: any = function named(first: any, second: any) { return first; };
            const arrow: any = (first: any, second: any) => first;
            async function asyncDeclaration(first: any, second: any) { return first; }
            const asyncExpression: any = async function namedAsync(first: any, second: any) { return first; };
            const asyncArrow: any = async (first: any, second: any) => first;
            for (const target of [declaration as any, expression, arrow, asyncDeclaration as any, asyncExpression, asyncArrow]) {
                console.log(target.length, Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'));
                console.log(Reflect.deleteProperty(target, 'name'), delete target['length'], target.name === '',
                    target['length'], Reflect.get(target, 'name') === '', Reflect.get(target, 'length'));
                console.log(Object.hasOwn(target, 'name'), Object.getOwnPropertyDescriptor(target, 'length') === undefined,
                    Object.getOwnPropertyNames(target).includes('name'), Object.getOwnPropertyNames(target).includes('length'));
            }
            """, string.Concat(Enumerable.Repeat("2 true true\ntrue true true 0 true 0\nfalse true false false\n", 6))];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
            foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
                yield return [row[0], row[1], row[2], mode];
        yield return ["passing-sync-and-async-indexed-metadata-deletion", InterpretedPositiveSource,
            string.Concat(Enumerable.Repeat("2 true true\ntrue true true 0 true 0\nfalse true false false\n", 5)),
            ExecutionMode.Interpreted];
    }

    public static IEnumerable<object[]> SavedCases() => Cases().Concat(
        AsyncCallableOwnPropertyDeletionTests.Cases().Where(row =>
            (string)row[0] is "issue" or "metadata-deletion-and-inherited-fallback"));

    [Theory, MemberData(nameof(ModeCases))]
    public void DeletedOwnMetadataFallsBackThroughTheDefaultFunctionPrototype(
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
