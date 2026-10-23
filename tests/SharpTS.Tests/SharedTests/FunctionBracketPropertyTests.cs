using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FunctionBracketPropertyTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-forms-helpers-and-invocation", """
            function declaration(this: any, a: number, b: number) { return this.base + a + b; }
            const functions: any[] = [declaration, function(this: any, a: number, b: number) { return this.base + a + b; },
                (a: number, b: number) => a + b];
            for (const fn of functions) {
                console.log(typeof fn.call, typeof fn['call'], typeof fn.apply, typeof fn['apply'], typeof fn.bind, typeof fn['bind']);
                console.log(fn['call']({ base: 10 }, 1, 2), fn['apply']({ base: 10 }, [3, 4]), fn['bind']({ base: 10 }, 5)(6));
            }
            """, "function function function function function function\n13 17 21\n"
                + "function function function function function function\n13 17 21\n"
                + "function function function function function function\n3 7 11\n"];
        yield return ["canonical-helper-identity", """
            function declaration() {}
            const functions: any[] = [declaration, function() {}, () => {}];
            for (const fn of functions) {
                console.log(fn['call'] === fn.call && fn['call'] === Function.prototype.call,
                    fn['apply'] === fn.apply && fn['apply'] === Reflect.get(fn, 'apply'),
                    fn['bind'] === fn.bind && fn['bind'] === Function.prototype.bind);
            }
            """, "true true true\ntrue true true\ntrue true true\n"];
        yield return ["own-data-and-getterless-masks", """
            function declaration() {}
            const functions: any[] = [declaration, function() {}, () => {}], replacement = () => 17;
            for (const fn of functions) {
                Object.defineProperty(fn, 'call', { value: replacement, writable: true, configurable: true });
                console.log(fn['call'] === replacement, fn['call'] === fn.call, fn['call']());
                fn.call = null; console.log(fn['call'] === null);
                fn.call = undefined; console.log(fn['call'] === undefined);
                Object.defineProperty(fn, 'call', { get: undefined });
                Object.defineProperty(fn, 'apply', { set(value: any) {}, configurable: true });
                console.log(fn['call'] === undefined, fn['apply'] === undefined);
                delete fn['call']; delete fn['apply'];
                console.log(typeof fn['call'], typeof fn['apply']);
            }
            """, string.Concat(Enumerable.Repeat("true true 17\ntrue\ntrue\ntrue true\nfunction function\n", 3))];
        yield return ["own-getter-receiver-and-abrupt", """
            function declaration() {}
            const functions: any[] = [declaration, function() {}, () => {}], marker: any = {}, value = () => 7;
            for (const fn of functions) {
                const state = { calls: 0, correct: true };
                const getter = function(this: any) { state.calls++; state.correct = state.correct && this === fn; return value; };
                Object.defineProperty(fn, 'call', { get: getter, configurable: true });
                const bracket = fn['call'];
                console.log(bracket === value, state.calls, state.correct,
                    Object.getOwnPropertyDescriptor(fn, 'call')!.get === getter);
                Object.defineProperty(fn, 'call', { get: function(this: any) { state.calls++; state.correct = state.correct && this === fn; throw marker; } });
                try { fn['call']; } catch (error) { console.log(error === marker, state.calls, state.correct); }
            }
            """, string.Concat(Enumerable.Repeat("true 1 true true\ntrue 2 true\n", 3))];
        yield return ["inherited-function-prototype-descriptors", """
            function declaration() {}
            const functions: any[] = [declaration, function() {}, () => {}], prototype: any = Function.prototype;
            const original: any = Object.getOwnPropertyDescriptor(prototype, 'call'), helper = prototype.call, value = () => 7;
            const data: boolean[] = [], accessors: boolean[] = [], masks: boolean[] = [];
            const state = { calls: 0, correct: true };
            Object.defineProperty(prototype, 'call', { value, writable: true, configurable: true });
            for (const fn of functions) data.push(fn['call'] === value);
            Object.defineProperty(prototype, 'call', {
                get: function(this: any) { state.calls++; state.correct = state.correct && functions.includes(this); return helper; }, configurable: true
            });
            for (const fn of functions) accessors.push(fn['call'] === helper);
            Object.defineProperty(prototype, 'call', { get: undefined });
            for (const fn of functions) masks.push(fn['call'] === undefined);
            Object.defineProperty(prototype, 'call', original);
            console.log(data.join(','), accessors.join(','), masks.join(','), state.calls, state.correct);
            console.log(functions[0]['call'] === helper, functions[1]['call'] === helper, functions[2]['call'] === helper);
            """, "true,true,true true,true,true true,true,true 3 true\ntrue true true\n"];
        yield return ["custom-null-restored-prototype", """
            function declaration() { return 1; }
            const functions: any[] = [declaration, function() { return 2; }, () => 3];
            const shared = Function.prototype, key = Symbol('own');
            for (const fn of functions) {
                fn.marker = 11; fn[key] = 13;
                const custom: any = Object.create(shared);
                Object.defineProperty(custom, 'call', { get: function(this: any) { return this.marker; }, configurable: true });
                Object.setPrototypeOf(fn, custom);
                console.log(fn['call'], fn['apply'] === shared.apply, fn['marker'], fn[key]);
                Object.setPrototypeOf(fn, null);
                console.log(fn['call'] === undefined, fn['apply'] === undefined, fn['bind'] === undefined, fn['marker'], fn[key]);
                Object.setPrototypeOf(fn, shared);
                console.log(fn['call'] === shared.call, fn['apply'] === shared.apply, fn['bind'] === shared.bind, Reflect.apply(fn, null, []));
            }
            """, "11 true 11 13\ntrue true true 11 13\ntrue true true 1\n"
                + "11 true 11 13\ntrue true true 11 13\ntrue true true 2\n"
                + "11 true 11 13\ntrue true true 11 13\ntrue true true 3\n"];
        yield return ["object-key-string-symbol-and-abrupt", """
            function declaration() {}
            const functions: any[] = [declaration, function() {}, () => {}], symbol = Symbol('own'), marker: any = {};
            for (const fn of functions) {
                fn[symbol] = 7;
                const state = { calls: 0, hint: '', receivers: 0, keys: 0 };
                const stringKey: any = { [Symbol.toPrimitive](hint: string) { state.calls++; state.hint = hint; return 'call'; } };
                const symbolKey: any = { [Symbol.toPrimitive](hint: string) { state.calls++; state.hint = hint; return symbol; } };
                function receiver(): any { state.receivers++; return fn; }
                function property(): any { state.keys++; return stringKey; }
                console.log(typeof receiver()[property()] === 'function', state.calls, state.hint, state.receivers, state.keys);
                console.log(fn[symbolKey], state.calls, state.hint);
                const bad: any = { [Symbol.toPrimitive](hint: string) { state.calls++; throw marker; } };
                try { fn[bad]; } catch (error) { console.log(error === marker, state.calls); }
            }
            """, string.Concat(Enumerable.Repeat("true 1 string 1 1\n7 2 string\ntrue 3\n", 3))];
    }

    public static IEnumerable<object[]> ParityCases()
        => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Where(mode => mode == ExecutionMode.Interpreted || (string)row[0] switch
            {
                // #2624: compiled helpers bypass the canonical Function.prototype descriptors.
                "canonical-helper-identity" or "inherited-function-prototype-descriptors" => false,
                // #2625: compiled bracket reads stringify object keys that produce Symbols.
                "object-key-string-symbol-and-abrupt" => false,
                _ => true
            })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void BracketReadsUseOrdinaryFunctionPropertySemantics(string name, string source, string expected, ExecutionMode mode)
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
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
