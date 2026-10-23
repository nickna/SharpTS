using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ComputedObjectBindingTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-symbol-binding-and-rest", """
            const symbol = Symbol("selected");
            const source: any = { [symbol]: 7, keep: 9 };
            const { [symbol]: taken, ...rest } = source;
            console.log(taken, rest.keep, Object.getOwnPropertySymbols(rest).length);
            """ + "\n", "7 9 0\n"];
        yield return ["full-original-class-prototype-computed-symbol-exclusion", """
            const own = Symbol('own'), inherited = Symbol('inherited'), hidden = Symbol('hidden');
            class Parent {}
            const basePrototype: any = Parent.prototype;
            basePrototype.inherited = 8; basePrototype[inherited] = 9;
            class Owner extends Parent { method() {} }
            const source: any = Owner.prototype;
            source.beta = 2; source['10'] = 10; source['2'] = 2; source.alpha = 1; source[own] = 7;
            Object.defineProperty(source, 'hidden', { value: 4, enumerable: false });
            Object.defineProperty(source, hidden, { value: 5, enumerable: false });
            const copy: any = { ...source };
            const { beta, [own]: symbolValue, ...rest } = source;
            console.log(Object.keys(copy).join(','), copy[own], Object.getOwnPropertySymbols(copy)[0] === own);
            console.log(beta, symbolValue, Object.keys(rest).join(','), Object.getOwnPropertySymbols(rest).length);
            console.log(Object.hasOwn(copy, 'constructor'), Object.hasOwn(copy, 'method'),
                Object.hasOwn(copy, 'inherited'), Object.hasOwn(copy, inherited),
                Object.hasOwn(copy, 'hidden'), Object.hasOwn(copy, hidden));
            """, "2,10,beta,alpha 7 true\n2 7 2,10,alpha 0\nfalse false false false false false\n"];
        yield return ["excluded-symbol-identity-same-description", """
            const excluded = Symbol("same"), retained = Symbol("same");
            const source: any = { visible: 1, [excluded]: 2, [retained]: 3 };
            const { [excluded]: selected, ...rest } = source;
            const symbols = Object.getOwnPropertySymbols(rest);
            console.log(selected, rest[excluded], rest[retained], rest.visible, symbols.length, symbols[0] === retained);
            """ + "\n", "2 undefined 3 1 1 true\n"];
        yield return ["computed-string-key-evaluated-once", """
            const state = { calls: 0 };
            function key(): string {
                state.calls++;
                return "picked";
            }
            const source: any = { picked: 7, keep: 9 };
            const { [key()]: picked, ...rest } = source;
            console.log(picked, rest.keep, Object.hasOwn(rest, "picked"), state.calls);
            """ + "\n", "7 9 false 1\n"];
        yield return ["full-original-excluded-getters-and-abrupt-completion", """
            const symbol = Symbol('skip'), state = { kept: 0, later: 0 };
            class Owner {}
            const source: any = Owner.prototype;
            Object.defineProperty(source, 'skip', { enumerable: true, get() { return 1; } });
            Object.defineProperty(source, 'keep', { enumerable: true, get() { state.kept++; return 2; } });
            Object.defineProperty(source, symbol, { enumerable: true, get() { return 3; } });
            const { skip, [symbol]: taken, ...rest } = source;
            console.log(skip, taken, rest.keep, state.kept,
                Object.hasOwn(rest, 'skip'), Object.hasOwn(rest, symbol));
            class Broken {}
            const broken: any = Broken.prototype, marker = {};
            Object.defineProperty(broken, 'first', { enumerable: true, get() { throw marker; } });
            Object.defineProperty(broken, 'later', { enumerable: true, get() { state.later++; return 9; } });
            try { const copy = { ...broken }; } catch (error) { console.log(error === marker, state.later); }
            try { const { ...copy } = broken; } catch (error) { console.log(error === marker, state.later); }
            """, "1 3 2 1 false false\ntrue 0\ntrue 0\n"];
        yield return ["computed-key-conversion-once", """
            const source: any = { selected: 7, keep: 9 };
            let calls = 0;
            const key: any = {
                toString() {
                    calls++;
                    return "selected";
                }
            };
            const { [key]: taken, ...rest } = source;
            console.log(taken, rest.keep, Object.hasOwn(rest, "selected"), calls);
            """ + "\n", "7 9 false 1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedBindingKeysAreEvaluatedAndExcludedOnce(
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
