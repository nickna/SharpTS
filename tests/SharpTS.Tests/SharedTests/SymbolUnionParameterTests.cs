using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class SymbolUnionParameterTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["no-narrowing", """
            function kind(key: string | symbol): string { return typeof key; }
            console.log(kind("text"), kind(Symbol("key")));
            """ + "\n", "string symbol\n"];
        yield return ["exact", """
            function matches(key: string | symbol): boolean {
                return typeof key === "string" && key === "boom";
            }
            console.log(matches("boom"), matches("other"), matches(Symbol("boom")));
            """ + "\n", "true false false\n"];
        yield return ["symbol-control", """
            function kind(key: symbol): string { return typeof key; }
            console.log(kind(Symbol("key")));
            """ + "\n", "symbol\n"];
        yield return ["number-symbol-control", """
            function kind(key: number | symbol): string { return typeof key; }
            console.log(kind(1), kind(Symbol("key")));
            """ + "\n", "number symbol\n"];
        yield return ["string-literal-union-control", """
            function echo(value: 'left' | 'right'): string { return value; }
            console.log(echo('left'), echo('right'));
            """ + "\n", "left right\n"];
        yield return ["override-string-symbol-hierarchy-control", """
            class Base { read(key: string | symbol): string { return 'base'; } }
            class Derived extends Base { read(key: string): string { return 'derived'; } }
            const owner: Base = new Derived();
            console.log(owner.read('text'));
            """ + "\n", "derived\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SymbolArgumentsKeepTheirValuesAndOverrideDispatch(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }
}
