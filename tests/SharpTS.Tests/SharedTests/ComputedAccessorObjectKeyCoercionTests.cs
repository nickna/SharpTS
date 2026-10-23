using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ComputedAccessorObjectKeyCoercionTests
{
    public const string SymbolIssueSource = """
        const symbol = Symbol("key");
        let coercions = 0;
        const key: any = {
            [Symbol.toPrimitive](hint: string) { coercions++; console.log(hint); return symbol; }
        };
        class C { get [key]() { return 7; } }
        console.log(coercions, (new C() as any)[symbol]);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["symbol-producing-exotic-key-issue", SymbolIssueSource, "string\n1 7\n"];
        yield return ["string-producing-and-direct-symbol-controls", """
            const first = Symbol('same'), second = Symbol('same'), hints: string[] = [];
            const key: any = { [Symbol.toPrimitive](hint: string) { hints.push(hint); return 'slot'; } };
            class C {
                get [key]() { return 1; }
                get [first]() { return 2; }
                get [second]() { return 3; }
                static get [first]() { return 4; }
            }
            const instance: any = new C(), owner: any = C;
            console.log(hints.join(','), instance.slot, instance[first], instance[second], owner[first]);
            """, "string 1 2 3 4\n"];
        yield return ["issue", """
            let coercions = 0;
            const key: any = { toString() { coercions++; return "result"; } };
            class C { get [key]() { return 7; } }
            const instance: any = new C();
            console.log(coercions, instance.result);
            """, "1 7\n"];
        yield return ["declaration-expression-generic-string-keys", """
            let coercions = 0;
            const key: any = { toString() { coercions++; return "slot"; } };
            class Declaration {
                get [key]() { return 1; }
                static get [key]() { return 2; }
            }
            const Expression = class {
                get [key]() { return 3; }
                static get [key]() { return 4; }
            };
            class Generic<T> {
                get [key]() { return 5; }
                static get [key]() { return 6; }
            }
            console.log(coercions);
            const declaration: any = new Declaration(), expression: any = new Expression();
            const first: any = new Generic<number>(), second: any = new Generic<string>();
            console.log(declaration.slot, (Declaration as any).slot, expression.slot, (Expression as any).slot,
                first.slot, second.slot, (Generic as any).slot, coercions);
            """, "6\n1 2 3 4 5 5 6 6\n"];
        yield return ["exotic-key-order-and-pairing", """
            const symbol = Symbol("pair"), events: string[] = [];
            function key(label: string, useSymbol: boolean): any {
                events.push("eval:" + label);
                return {
                    [Symbol.toPrimitive](hint: string) {
                        events.push("coerce:" + label + ":" + hint);
                        return useSymbol ? symbol : "slot";
                    },
                    toString() { events.push("wrong"); return "wrong"; }
                };
            }
            class C<T> {
                stored = 0;
                static stored = 0;
                get [key("get", false)]() { return this.stored; }
                set [key("set", false)](value: number) { this.stored = value; }
                static get [key("static-get", true)]() { return this.stored; }
                static set [key("static-set", true)](value: number) { this.stored = value; }
                static ready = (events.push("init"), true);
            }
            console.log(events.join(","));
            const first: any = new C<number>(), second: any = new C<string>(), constructor: any = C;
            first.slot = 11; second.slot = 22; constructor[symbol] = 33;
            console.log(first.slot, second.slot, constructor[symbol], C.stored, events.join(","));
            const instanceDescriptor = Object.getOwnPropertyDescriptor(C.prototype, "slot")!;
            const staticDescriptor = Object.getOwnPropertyDescriptor(C, symbol)!;
            console.log(typeof instanceDescriptor.get, typeof instanceDescriptor.set,
                typeof staticDescriptor.get, typeof staticDescriptor.set);
            """, "eval:get,coerce:get:string,eval:set,coerce:set:string,eval:static-get,coerce:static-get:string,eval:static-set,coerce:static-set:string,init\n11 22 33 33 eval:get,coerce:get:string,eval:set,coerce:set:string,eval:static-get,coerce:static-get:string,eval:static-set,coerce:static-set:string,init\nfunction function function function\n"];
        yield return ["ordinary-string-hint-fallbacks", """
            const events: string[] = [], symbol = Symbol("fallback");
            const first: any = {
                toString() { events.push("first:string"); return "first"; },
                valueOf() { events.push("first:value"); return "wrong"; }
            };
            const second: any = {
                toString() { events.push("second:string"); return {}; },
                valueOf() { events.push("second:value"); return "second"; }
            };
            const third: any = {
                [Symbol.toPrimitive]: null,
                toString() { events.push("third:string"); return {}; },
                valueOf() { events.push("third:value"); return symbol; }
            };
            const C = class {
                get [first]() { return 1; }
                get [second]() { return 2; }
                static get [third]() { return 3; }
            };
            console.log(events.join(","));
            const instance: any = new C(), constructor: any = C;
            console.log(instance.first, instance.second, constructor[symbol], events.join(","));
            """, "first:string,second:string,second:value,third:string,third:value\n1 2 3 first:string,second:string,second:value,third:string,third:value\n"];
        yield return ["per-accessor-coercion-result-is-retained", """
            const symbol = Symbol("setter"), events: string[] = [];
            let calls = 0;
            const key: any = {
                [Symbol.toPrimitive](hint: string) {
                    events.push(hint);
                    calls++;
                    return calls === 1 ? "read" : symbol;
                }
            };
            class C {
                stored = 4;
                get [key]() { return this.stored; }
                set [key](value: number) { this.stored = value; }
            }
            const instance: any = new C();
            console.log(calls, events.join(","), instance.read, instance[symbol]);
            instance[symbol] = 9;
            const read = Object.getOwnPropertyDescriptor(C.prototype, "read")!;
            const write = Object.getOwnPropertyDescriptor(C.prototype, symbol)!;
            console.log(instance.read, instance.stored, calls, typeof read.get, typeof read.set,
                typeof write.get, typeof write.set);
            """, "2 string,string 4 undefined\n9 9 2 function undefined undefined function\n"];
        yield return ["abrupt-coercion-and-invalid-results", """
            const events: string[] = [], token = { tag: "sentinel" };
            const exotic: any = {
                [Symbol.toPrimitive](hint: string) { events.push("hook:" + hint); throw token; }
            };
            function later(): any { events.push("later"); return "later"; }
            try {
                class C {
                    get [exotic]() { return 1; }
                    get [later()]() { return 2; }
                    static initialized = (events.push("init"), true);
                }
            } catch (error) { console.log(error === token, events.join(",")); }
            events.length = 0;
            const ordinary: any = {
                toString() { events.push("string"); throw token; },
                valueOf() { events.push("value"); return "wrong"; }
            };
            try {
                class C<T> { static get [ordinary]() { return 1; } }
            } catch (error) { console.log(error === token, events.join(",")); }
            events.length = 0;
            const invalid: any = {
                [Symbol.toPrimitive](hint: string) { events.push("invalid:" + hint); return {}; },
                toString() { events.push("wrong"); return "wrong"; }
            };
            try { const C = class { get [invalid]() { return 1; } }; }
            catch (error) { console.log(error instanceof TypeError, events.join(",")); }
            const noncallable: any = { [Symbol.toPrimitive]: 1, toString() { return "wrong"; } };
            try { const C = class { static get [noncallable]() { return 1; } }; }
            catch (error) { console.log(error instanceof TypeError); }
            """, "true hook:string\ntrue string\ntrue invalid:string\ntrue\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    public static IEnumerable<object[]> StandaloneCases() => Cases().Where(row =>
        (string)row[0] is not ("issue" or "declaration-expression-generic-string-keys"));

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedAccessorKeysUseGuestPropertyKeyCoercion(
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
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
