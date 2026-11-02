using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ReflectArrayLikeArgumentListTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["numeric-storage-and-value-forms", """
            function sum(a: any, b: any) { return a + b; }
            function Value(this: any, a: any, b: any) { this.value = a + b; }
            const target: any = Value, apply: any = Reflect.apply, construct: any = Reflect.construct;
            console.log(Reflect.apply(sum, null, [7, 9]), apply(sum, null, [11, 13]));
            console.log((Reflect.construct(target, [7, 9]) as any).value,
                construct(target, [11, 13]).value);
            const receiver: any = { tag: "bound" };
            function readThis(this: any) { "use strict"; return this; }
            const bind: any = Function.prototype.bind;
            const numberBound: any = Reflect.apply(bind, readThis, [7]);
            const booleanBound: any = Reflect.apply(bind, readThis, [false]);
            console.log(numberBound() === 7, booleanBound() === false);
            const Bound: any = target.bind(receiver, 2);
            console.log(construct(Bound, [3]).value, receiver.tag);
            """, "16 24\n16 24\ntrue true\n5 bound\n"];
        yield return ["bind-callee-strictness", """
            function strictRead(this: any) { "use strict"; return this; }
            function sloppyRead(this: any) { return this; }
            const bind: any = Function.prototype.bind;
            for (const value of [7, false, "text"]) {
                const strictBound: any = Reflect.apply(bind, strictRead, [value]);
                const sloppyBound: any = Reflect.apply(bind, sloppyRead, [value]);
                const boxed: any = sloppyBound();
                console.log(strictBound() === value, typeof boxed, boxed.valueOf() === value,
                    boxed !== sloppyBound());
            }
            """, "true object true true\ntrue object true true\ntrue object true true\n"];
        yield return ["representations-holes-and-inherited-indices", """
            function format(...values: any[]) {
                return values.map(v => v === undefined ? "U" : String(v)).join("|") + ":" + values.length;
            }
            function Value(this: any, ...values: any[]) { this.value = format(...values); }
            function getArguments(...values: any[]): any { return arguments; }
            const inherited: any = Object.create({ 1: "inherited" });
            inherited[0] = "own"; inherited.length = 3;
            Object.defineProperty(inherited, "2", { value: "hidden", enumerable: false });
            for (const args of [[1, 2], ["x", "y"], [1, , 3], new Uint8Array([4, 5]),
                new String("ab"), getArguments("first", "last"), inherited]) {
                // #3113 tracks the ambient checker rejecting this valid ArrayLike union.
                console.log(Reflect.apply(format, null, args as any),
                    (Reflect.construct(Value as any, args as any) as any).value);
            }
            """, "1|2:2 1|2:2\nx|y:2 x|y:2\n1|U|3:3 1|U|3:3\n4|5:2 4|5:2\na|b:2 a|b:2\nfirst|last:2 first|last:2\nown|inherited|hidden:3 own|inherited|hidden:3\n"];
        yield return ["length-snapshot-index-order-and-iterator-ignored", """
            const state: any = { trace: "", second: "old", receiver: null };
            function makeArguments(): any {
                const prototype: any = {};
                Object.defineProperty(prototype, "1", { get: function() {
                    state.trace += "b"; state.receiver = this; return state.second;
                } });
                const args: any = Object.create(prototype);
                Object.defineProperty(args, "length", { configurable: true, get: function() {
                    state.trace += "l"; return { valueOf() { state.trace += "v"; return 3; } };
                } });
                Object.defineProperty(args, "0", { get: function() {
                    state.trace += "a"; state.second = "changed";
                    Object.defineProperty(args, "length", { value: 1 }); return "first";
                } });
                Object.defineProperty(args, Symbol.iterator, { get: function() {
                    state.trace += "i"; throw "iterator must not be read";
                } });
                return args;
            }
            function Target(this: any, a: any, b: any, c: any) {
                state.trace += "t"; this.result = [a, b, c === undefined].join("|"); return this.result;
            }
            for (const construct of [false, true]) {
                state.trace = ""; state.second = "old"; state.receiver = null;
                const args: any = makeArguments();
                const result: any = construct ? Reflect.construct(Target as any, args)
                    : Reflect.apply(Target, {}, args);
                console.log(construct ? result.result : result, state.trace, state.receiver === args);
            }
            """, "first|changed|true lvabt true\nfirst|changed|true lvabt true\n"];
        yield return ["target-and-newtarget-before-list-access", """
            const state = { reads: 0 };
            const token = {}, args: any = { get length() { state.reads++; throw token; } };
            function Target() {}
            function check(run: () => any) {
                try { run(); console.log(false); }
                catch (error) { console.log(error instanceof TypeError, state.reads); }
            }
            check(() => Reflect.apply({} as any, null, args));
            check(() => Reflect.construct({} as any, args));
            check(() => Reflect.construct(Target, args, (() => 0) as any));
            // #3114 tracks ambient optional-parameter checking; validate the present value at runtime.
            check(() => Reflect.construct(Target, args, undefined as any));
            """, "true 0\ntrue 0\ntrue 0\ntrue 0\n"];
        yield return ["strict-reflect-lists-and-apply-nullish-controls", """
            const state = { calls: 0 };
            function Target(this: any) { state.calls++; }
            function rejects(run: () => any) {
                try { run(); return false; } catch (error) { return error instanceof TypeError; }
            }
            for (const args of [null, undefined, 7, false, "bad", Symbol("args"), 1n]) {
                console.log(rejects(() => Reflect.apply(Target, null, args as any)),
                    rejects(() => Reflect.construct(Target, args as any)), state.calls);
            }
            const target: any = function(...values: any[]) { return values.length; };
            console.log(target.apply(null, null), target.apply(null, undefined),
                Reflect.apply(target, null, []));
            """, "true true 0\ntrue true 0\ntrue true 0\ntrue true 0\ntrue true 0\ntrue true 0\ntrue true 0\n0 0 0\n"];
        yield return ["abrupt-length-coercion-index-and-target", """
            const state = { trace: "", calls: 0 }, token = {};
            function Target(this: any) { state.trace += "t"; state.calls++; throw token; }
            function makeArguments(stage: string): any {
                return {
                    get length() {
                        state.trace += "l"; if (stage === "length") throw token;
                        return { valueOf() { state.trace += "v"; if (stage === "coercion") throw token; return 2; } };
                    },
                    get 0() { state.trace += "a"; if (stage === "first") throw token; return 1; },
                    get 1() { state.trace += "b"; if (stage === "later") throw token; return 2; }
                };
            }
            for (const construct of [false, true]) {
                for (const stage of ["length", "coercion", "first", "later", "target"]) {
                    state.trace = ""; state.calls = 0; const args: any = makeArguments(stage);
                    try {
                        if (construct) Reflect.construct(Target as any, args);
                        else Reflect.apply(Target, null, args);
                    } catch (error) { console.log(error === token, state.trace, state.calls); }
                }
            }
            """, "true l 0\ntrue lv 0\ntrue lva 0\ntrue lvab 0\ntrue lvabt 1\ntrue l 0\ntrue lv 0\ntrue lva 0\ntrue lvab 0\ntrue lvabt 1\n"];
        yield return ["length-normalization-and-bigint-symbol-refusal", """
            const state = { calls: 0 };
            function Target(this: any, ...values: any[]) { state.calls++; this.length = values.length; return values.length; }
            for (const length of [undefined, NaN, -3, 1.9, "2"]) {
                const args: any = { 0: "a", 1: "b", length };
                console.log(Reflect.apply(Target, {}, args), (Reflect.construct(Target as any, args) as any).length);
            }
            function rejects(run: () => any) {
                try { run(); return false; } catch (error) { return error instanceof TypeError; }
            }
            state.calls = 0;
            for (const length of [1n, Symbol("length")]) {
                const args: any = { length };
                console.log(rejects(() => Reflect.apply(Target, {}, args)),
                    rejects(() => Reflect.construct(Target as any, args)), state.calls);
            }
            """, "0 0\n0 0\n0 0\n1 1\n2 2\ntrue true 0\ntrue true 0\n"];
        yield return ["huge-length-preserves-first-abrupt-index", """
            const state = { trace: "" }, token = {};
            function Target() { state.trace += "t"; }
            for (const construct of [false, true]) {
                state.trace = "";
                const args: any = { get length() { state.trace += "l"; return 1000000; },
                    get 0() { state.trace += "a"; throw token; } };
                try {
                    if (construct) Reflect.construct(Target, args);
                    else Reflect.apply(Target, null, args);
                } catch (error) { console.log(error === token, state.trace); }
            }
            """, "true la\ntrue la\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NativeGuestReadsTheSameArrayLikeArguments(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(); var types = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        var compiler = new ILCompiler("reflect_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        compiler.EnableNativeAotProjectOutput(); compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes(); using var image = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(image));
        Assert.Equal(expected, TestHarness.RunCompiledWithTimeout(() => Assembly.Load(bytes), TimeSpan.FromSeconds(20)));
    }
}
