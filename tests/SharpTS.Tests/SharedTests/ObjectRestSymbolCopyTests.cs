using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ObjectRestSymbolCopyTests
{
    // #2759 separately owns the computed-binding reference case retained in the issue artifacts.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-ordinary-record-rest-symbol", """
            const symbol = Symbol('extra');
            const source: any = { visible: 1, [symbol]: 2 };
            const { visible, ...rest } = source;
            const assigned: any = Object.assign({}, source), spread: any = { ...source };
            console.log(visible, assigned[symbol], spread[symbol], rest[symbol], Object.getOwnPropertySymbols(rest).length);
            """, "1 2 2 2 1\n"];
        yield return ["nonenumerable-and-inherited-symbol-exclusion", """
            const inherited = Symbol("inherited"), hidden = Symbol("hidden"), own = Symbol("own");
            const prototype: any = { inheritedString: 9, [inherited]: 8 };
            const source: any = Object.create(prototype);
            source.visible = 1;
            source.keep = 4;
            source[own] = 2;
            const defined = Object.defineProperty(source, hidden, { value: 3, enumerable: false });
            const { visible, ...rest } = source;
            console.log(visible, rest.keep, rest[own], Object.hasOwn(rest, hidden),
                Object.hasOwn(rest, inherited), Object.hasOwn(rest, "inheritedString"),
                Object.getOwnPropertySymbols(rest).length);
            """, "1 4 2 false false false 1\n"];
        yield return ["getter-receiver-and-string-symbol-copy-order", """
            const symbol = Symbol("extra");
            const trace: string[] = [];
            const source: any = { visible: 1 };
            const stringProperty = Object.defineProperty(source, "keep", { enumerable: true, get: function(this: any) {
                trace.push("keep");
                return this === source ? 2 : 99;
            } });
            const symbolProperty = Object.defineProperty(source, symbol, { enumerable: true, get: function(this: any) {
                trace.push("symbol");
                return this === source ? 3 : 99;
            } });
            const { visible, ...rest } = source;
            console.log(visible, rest.keep, rest[symbol], trace.join(","));
            const descriptor = Object.getOwnPropertyDescriptor(rest, symbol);
            if (descriptor === undefined) {
                console.log("descriptor missing");
            } else {
                console.log(descriptor.value, descriptor.get === undefined,
                    descriptor.writable, descriptor.enumerable, descriptor.configurable);
            }
            """, "1 2 3 keep,symbol\n3 true true true true\n"];
        // Reuse the entire existing sync/async iterator program and its exact
        // source/expected values, including its original CRLF and absent final LF.
        var generator = GeneratorOwnPropertyTests.Cases()
            .Single(row => (string)row[0] == "rest-symbol");
        yield return ["full-generator-iterator-rest-symbol-row", generator[1], generator[2]];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void CopiesEnumerableOwnSymbols(string name, string source, string expected, ExecutionMode mode)
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
