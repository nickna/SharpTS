using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ExplicitSuperReplacementReceiverTests
{
    // The compiler API tests exercise receiver selection with an empty TypeMap.
    [Theory]
    [InlineData("class Child extends Middle", "constructor() { super(); }")]
    [InlineData("class Child extends Middle", "constructor() { const initialize = () => super(); initialize(); }")]
    [InlineData("class Child extends Middle", "")]
    [InlineData("const Child = class extends Middle", "constructor() { super(); }")]
    [InlineData("const Child = class extends Middle", "constructor() { const initialize = () => super(); initialize(); }")]
    public void CompilerApiInitializesElementsOnTheReplacement(string child, string constructor)
    {
        string source = $$"""
            const replacement = {};
            class Base { constructor() { return replacement; } }
            class Middle extends Base {}
            {{child}} {
                field = 7;
                getReceiver = () => this;
                {{constructor}}
            }
            const result = new Child();
            console.log(result === replacement, result.field, result.getReceiver?.() === result);
            """;

        Assert.Equal("true 7 true\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void CompilerApiBindsNestedClassFieldArrowsToTheReplacement()
    {
        const string source = """
            const replacement = {};
            class Base { constructor() { return replacement; } }
            function make() {
                return class extends Base {
                    field = 7;
                    getReceiver = () => this;
                    constructor() { super(); }
                };
            }
            const Child = make();
            const result = new Child();
            console.log(result === replacement, result.field, result.getReceiver() === result);
            """;

        Assert.Equal("true 7 true\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void CompilerApiBindsRuntimeParentFieldArrowsToTheReplacement()
    {
        const string source = """
            const replacement = {};
            class Base { constructor() { return replacement; } }
            function make(Parent) {
                class Child extends Parent {
                    field = 7;
                    getReceiver = () => this;
                    constructor() { super(); }
                }
                return Child;
            }
            const Child = make(Base);
            const result = new Child();
            console.log(result === replacement, result.field, result.getReceiver() === result);
            """;

        Assert.Equal("true 7 true\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", """
            const replacement: any = { value: 43 };
            class Base { value = 1; constructor() { return replacement; } }
            class Derived extends Base {
                child = 44;
                constructor() { super(); this.value = 45; }
            }
            const result: any = new Derived();
            console.log(result === replacement, result.value, result.child, replacement.value, replacement.child);
            """, "true 45 44 45 44\n"];
        yield return ["multilevel-and-super-expression", """
            const state: any = { replacement: { value: 10 }, trace: [], original: null, selected: null };
            class Base {
                base = (state.trace.push("base.field"), 1);
                constructor() { state.original = this; state.trace.push("base.body"); return state.replacement; }
            }
            class Middle extends Base {
                middle = (state.trace.push("middle.field"), (this as any).value + 1);
            }
            class Leaf extends Middle {
                child = (state.trace.push("child.field"), (this as any).value + 2);
                constructor() {
                    const selected = (state.selected = super(), state.trace.push("after-expression"), this);
                    (selected as any).value += 3;
                    state.trace.push("body");
                }
            }
            const value: any = new Leaf();
            console.log(state.trace.join("|"), value === state.replacement, state.selected === value,
                value.value, value.middle, value.child, Object.hasOwn(value, "base"), state.original.base);
            class Implicit extends Middle { last = (this as any).value + 2; }
            state.trace = []; state.replacement = { value: 20 };
            const implicit: any = new Implicit();
            console.log(state.trace.join("|"), implicit === state.replacement, implicit.middle, implicit.last);
            """, "base.field|base.body|middle.field|child.field|after-expression|body true true 13 11 12 false 1\nbase.field|base.body|middle.field true 21 22\n"];
        yield return ["aliases-field-arrows-and-parameter-property", """
            const replacement: any = { count: 5 };
            const state: any = {};
            class Base { constructor() { return replacement; } }
            class Derived extends Base {
                read = () => (this as any).count;
                constructor(public parameter: number) {
                    super();
                    const alias: any = this; alias.count++;
                    state.sync = () => this;
                    state.async = async () => this;
                }
            }
            const value: any = new Derived(7);
            const read: any = replacement.read;
            console.log(value === replacement, replacement.parameter, state.sync() === replacement, read());
            class WrittenAssignment extends Base {
                constructor(public value: number) { super(); value = 8; this.value = value; }
            }
            state.async().then((receiver: any) => {
                console.log(receiver === replacement, receiver.count);
                console.log(new WrittenAssignment(7) === replacement, replacement.value);
            });
            """, "true 7 true 6\ntrue 6\ntrue 8\n"];
        yield return ["ordinary-constructor-async-arrow-creation", """
            const state: any = {};
            class C {
                constructor() { state.sync = () => this; state.async = async () => this; }
            }
            const value: any = new C();
            console.log(typeof state.sync, typeof state.async);
            if (typeof state.async === "function")
                state.async().then((receiver: any) => console.log(receiver === value));
            """, "function function\ntrue\n"];
        yield return ["pre-super-lexical-arrow-binding", """
            const replacement: any = {}, state: any = {};
            class Base { constructor() { return replacement; } }
            class Derived extends Base {
                child = 1;
                constructor() {
                    const before = () => this;
                    super(); state.before = before;
                }
            }
            console.log(new Derived() === replacement, state.before() === replacement, replacement.child);
            """, "true true 1\n"];
        yield return ["class-expression-and-evaluated-declaration", """
            function expression(seed: number, replacement: any): any {
                const Base = class { constructor() { return replacement; } };
                return class extends Base {
                    child = seed;
                    constructor() { super(); (this as any).value = seed + 1; }
                };
            }
            function declaration(seed: number, replacement: any): any {
                class Base { constructor() { return replacement; } }
                class Local extends Base {
                    child = seed;
                    constructor() { super(); (this as any).value = seed + 1; }
                }
                return Local;
            }
            const first: any = {}, second: any = {}, third: any = {}, fourth: any = {};
            const A: any = expression(10, first), B: any = expression(20, second);
            const C: any = declaration(30, third), D: any = declaration(40, fourth);
            console.log(new A() === first, first.value, first.child, new B() === second, second.value, second.child);
            console.log(new C() === third, third.value, third.child, new D() === fourth, fourth.value, fourth.child);
            """, "true 11 10 true 21 20\ntrue 31 30 true 41 40\n"];
        yield return ["generic-bound-and-alternate-newtarget", """
            const state: any = { replacement: {}, target: null, argument: null };
            class Base<T> {
                constructor(value: T) { state.argument = value; state.target = new.target; return state.replacement; }
            }
            class Derived extends Base<number> {
                child = 2;
                constructor(value: number) { super(value); (this as any).value = value + 1; }
            }
            const Bound: any = Derived.bind(null, 7);
            const bound: any = new Bound();
            console.log(bound === state.replacement, bound.child, bound.value, state.argument, state.target === Derived);
            function Alternate() {}
            state.replacement = {};
            const reflected: any = Reflect.construct(Derived, [9], Alternate);
            console.log(reflected === state.replacement, reflected.child, reflected.value,
                state.argument, state.target === Alternate, Object.getPrototypeOf(reflected) === Object.prototype);
            """, "true 2 8 7 true\ntrue 2 10 9 true true\n"];
        yield return ["replacement-object-kinds-and-prototypes", """
            class Other { own = 4; }
            class Base { constructor(value: any) { return value; } }
            class Derived extends Base {
                child = 2;
                constructor(value: any) { super(value); (this as any).written = 3; }
            }
            const values: any[] = [{}, [], function() { return 4; }, new Other()];
            for (const replacement of values) {
                const prototype = Object.getPrototypeOf(replacement);
                const result: any = new Derived(replacement);
                console.log(result === replacement, result.child, result.written,
                    Object.getPrototypeOf(result) === prototype, result instanceof Derived);
            }
            """, "true 2 3 true false\ntrue 2 3 true false\ntrue 2 3 true false\ntrue 2 3 true false\n"];
        yield return ["own-field-definition-string-and-symbol", """
            const state = { gets: 0, sets: 0 };
            const symbol: unique symbol = Symbol("field");
            const replacement: any = Object.create({ set inherited(value: any) { state.sets++; } });
            Object.defineProperty(replacement, "child", { configurable: true,
                get: () => { state.gets++; return -1; }, set: (value: any) => { state.sets++; } });
            Object.defineProperty(replacement, symbol, { configurable: true,
                get: () => { state.gets++; return -2; } });
            class Base { constructor() { return replacement; } }
            class Derived extends Base {
                child = 1;
                inherited = 2;
                [symbol] = 3;
                constructor() { super(); }
            }
            const result: any = new Derived();
            console.log(result === replacement, result.child, result.inherited, result[symbol], state.gets, state.sets);
            for (const key of ["child", "inherited", symbol]) {
                const descriptor: any = Object.getOwnPropertyDescriptor(result, key);
                console.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable,
                    Object.hasOwn(descriptor, "get"));
            }
            """, "true 1 2 3 0 0\n1 true true true false\n2 true true true false\n3 true true true false\n"];
        yield return ["abrupt-nonextensible-and-nonconfigurable-fields", """
            const state: any = { replacement: null, trace: [] };
            class Base { constructor() { return state.replacement; } }
            class Derived extends Base {
                first = (state.trace.push("first"), 1);
                later = (state.trace.push("later"), 2);
                constructor() { super(); state.trace.push("body"); }
            }
            const locked: any = {};
            Object.defineProperty(locked, "first", { value: 0, configurable: false });
            for (const replacement of [Object.freeze({}), Object.preventExtensions({}), locked]) {
                state.replacement = replacement; state.trace = [];
                try { new Derived(); console.log(false); }
                catch (error) { console.log(error instanceof TypeError, state.trace.join("|"),
                    Object.hasOwn(replacement, "later")); }
            }
            """, "true first false\ntrue first false\ntrue first false\n"];
        yield return ["selected-private-fields-methods-and-compound", """
            const state: any = { replacement: {}, original: null, childInitializers: 0 };
            class Base {
                #base = 1;
                constructor() { state.original = this; return state.replacement; }
                static has(value: any) { return #base in value; }
            }
            class Derived extends Base {
                #child = (state.childInitializers++, 3);
                #read() { return this.#child; }
                constructor() {
                    super();
                    state.read = () => this.#read();
                    state.add = (value: number) => this.#child += value;
                    state.has = (value: any) => #child in value;
                    state.other = (value: any) => value.#child;
                }
            }
            const value: any = new Derived();
            console.log(value === state.replacement, state.read(), state.add(4), state.read(),
                state.has(value), Base.has(value), Base.has(state.original));
            try { state.other({}); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            try { new Derived(); console.log(false); }
            catch (error) { console.log(error instanceof TypeError, state.childInitializers); }
            """, "true 3 7 7 true false true\ntrue\ntrue 1\n"];
        yield return ["private-definition-and-duplicate-brand", """
            const state: any = { trace: [] };
            function make(seed: number, replacement: any): any {
                class Base { constructor() { return replacement; } }
                class Derived extends Base {
                    #value = (state.trace.push("init:" + seed), seed);
                    constructor() { super(); }
                    static read(value: any) { return value.#value; }
                }
                return Derived;
            }
            const first: any = {}, second: any = {};
            const A: any = make(1, first), B: any = make(2, second);
            console.log(new A() === first, A.read(first));
            try { new A(); console.log(false); }
            catch (error) { console.log(error instanceof TypeError, state.trace.join("|")); }
            console.log(new B() === second, B.read(second));
            try { A.read(second); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            """, "true 1\ntrue init:1|init:1\ntrue 2\ntrue\n"];
        yield return ["selected-fallback-return-and-finally", """
            const state: any = { replacement: {}, other: {}, final: {} };
            class Base { constructor() { return state.replacement; } }
            class Implicit extends Base { child = 1; }
            class Derived extends Base {
                child = 2;
                constructor(mode: string) {
                    super();
                    try {
                        if (mode === "object") return state.other;
                        if (mode === "primitive") return (1 as any);
                        return;
                    } finally { if (mode === "finally") return state.final; }
                }
            }
            console.log(new Implicit() === state.replacement, state.replacement.child);
            console.log(new Derived("undefined") === state.replacement, state.replacement.child);
            console.log(new Derived("object") === state.other, Object.hasOwn(state.other, "child"));
            console.log(new Derived("finally") === state.final, Object.hasOwn(state.final, "child"));
            try { new Derived("primitive"); console.log(false); }
            catch (error) { console.log(error instanceof TypeError, state.replacement.child); }
            """, "true 1\ntrue 2\ntrue false\ntrue false\ntrue 2\n"];
        yield return ["native-array-parent-returning-record", """
            const state: any = { replacement: {}, array: false, length: 0 };
            class Base extends Array<any> {
                constructor() {
                    super(1, 2); state.array = Array.isArray(this); state.length = this.length;
                    return state.replacement;
                }
            }
            class Derived extends Base {
                child = 3;
                constructor() { super(); (this as any).tag = "array"; }
            }
            const value: any = new Derived();
            console.log(value === state.replacement, value.child, value.tag, state.array, state.length,
                Array.isArray(value), value instanceof Derived);
            """, "true 3 array true 2 false false\n"];
        yield return ["native-error-and-promise-parents-returning-records", """
            const state: any = { error: {}, promise: {}, message: "", nativePromise: false, executor: 0 };
            const NativeError: any = Error, NativePromise: any = Promise;
            class ErrorBase extends NativeError {
                constructor() { super("message"); state.message = (this as any).message; return state.error; }
            }
            class ErrorChild extends ErrorBase {
                child = 3;
                constructor() { super(); (this as any).tag = "error"; }
            }
            class PromiseBase extends NativePromise {
                constructor() {
                    super((resolve: any) => { state.executor++; resolve(7); });
                    state.nativePromise = this instanceof Promise; return state.promise;
                }
            }
            class PromiseChild extends PromiseBase {
                child = 4;
                constructor() { super(); (this as any).tag = "promise"; }
            }
            const error: any = new ErrorChild(), promise: any = new PromiseChild();
            console.log(error === state.error, error.child, error.tag, state.message, error instanceof Error);
            console.log(promise === state.promise, promise.child, promise.tag, state.nativePromise,
                state.executor, promise instanceof Promise);
            """, "true 3 error message false\ntrue 4 promise true 1 false\n"];
    }

    // Runtime aliases of native Error/Promise parents remain unavailable in
    // compiled heritage; the interpreter retains the selected-record contracts.
    public static IEnumerable<object[]> CompiledCases()
    {
        foreach (var row in Cases().Where(row =>
            (string)row[0] != "native-error-and-promise-parents-returning-records"))
            yield return row;

        // The descriptor/brand control is also selected for the interpreter below.
        yield return ["selected-auto-accessor-backing-and-brand", """
            const replacement: any = {};
            class Base { constructor() { return replacement; } }
            class Derived extends Base {
                accessor label: string = "first";
                constructor() { super(); }
            }
            const descriptor: any = Object.getOwnPropertyDescriptor(Derived.prototype, "label");
            const value: any = new Derived();
            console.log(value === replacement, descriptor.get.call(value), Object.hasOwn(value, "label"));
            descriptor.set.call(value, "second");
            console.log(descriptor.get.call(value));
            try { descriptor.get.call({}); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            try { new Derived(); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            const rawState: any = {};
            class Raw {
                accessor text: string;
                accessor count: number;
                accessor flag: boolean;
                constructor(replace: boolean) {
                    this.text = "ready"; this.count = 1; this.flag = true;
                    if (replace) return ({} as any);
                    rawState.receiver = this;
                }
            }
            const raw: any = new Raw(false);
            console.log(raw === rawState.receiver, raw instanceof Raw);
            class C<T> {
                accessor label: string = "first";
                constructor(replacement: any) { if (replacement) return replacement; }
            }
            const generic: any = new C<number>(null);
            const genericDescriptor: any = Object.getOwnPropertyDescriptor(C.prototype, "label");
            console.log(generic.label, genericDescriptor.get.call(generic));
            generic.label = "second";
            console.log(generic.label, genericDescriptor.get.call(generic));
            """, "true first false\nsecond\ntrue\ntrue\ntrue true\nfirst first\nsecond second\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .Concat(CompiledCases().Where(row => (string)row[0] == "selected-auto-accessor-backing-and-brand"))
        .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Interpreted })
        .Concat(CompiledCases().Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Compiled }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SuperReplacementReceivesDerivedElementsAndConstructorEffects(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void HostedProxyReplacementPreservesDefinitionKeysAndThrownValue(ExecutionMode mode)
    {
        // Proxy's standalone deployment boundary is tracked by #2178.
        const string source = """
            const state: any = { trace: [] };
            const token = {}, target: any = {};
            const symbol: unique symbol = Symbol("field");
            const replacement: any = new Proxy(target, { defineProperty(object, key, descriptor) {
                state.trace.push(key === symbol ? "symbol" : key);
                if (key === symbol) throw token;
                console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
                return Reflect.defineProperty(object, key, descriptor);
            } });
            class Base { constructor() { return replacement; } }
            class Derived extends Base {
                first = (state.trace.push("init:first"), 1);
                [symbol] = (state.trace.push("init:symbol"), 2);
                later = (state.trace.push("init:later"), 3);
                constructor() { super(); }
            }
            try { new Derived(); console.log(false); }
            catch (error) { console.log(error === token, state.trace.join("|"), target.first,
                Object.hasOwn(target, "later"), Object.hasOwn(target, symbol)); }
            state.trace = [];
            const stringReplacement: any = new Proxy({}, { defineProperty(object, key, descriptor) {
                state.trace.push("string:trap"); throw token;
            } });
            class StringBase { constructor() { return stringReplacement; } }
            class StringChild extends StringBase {
                field = (state.trace.push("string:init"), 1);
                later = (state.trace.push("string:later"), 2);
            }
            try { new StringChild(); console.log(false); }
            catch (error) { console.log(error === token, state.trace.join("|")); }
            """;
        Assert.Equal("true true true\ntrue init:first|first|init:symbol|symbol 1 false false\ntrue string:init|string:trap\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DefinePropertyPreservesProxySymbolKey(ExecutionMode mode)
    {
        const string source = """
            const symbol: unique symbol = Symbol("direct");
            const target: any = {};
            const proxy: any = new Proxy(target, { defineProperty(object, key, descriptor) {
                console.log(key === symbol, descriptor.value, descriptor.writable,
                    descriptor.enumerable, descriptor.configurable);
                return Reflect.defineProperty(object, key, descriptor);
            } });
            console.log(Object.defineProperty(proxy, symbol,
                { value: 4, writable: true, enumerable: true, configurable: true }) === proxy,
                target[symbol], Object.hasOwn(target, symbol));
            """;
        Assert.Equal("true 4 true true true\ntrue 4 true\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void InterpretedJavaScriptCompletionAllowsEarlyObjectReturnAndRejectsMissingSuper()
    {
        // These valid JavaScript constructor shapes have separate TS diagnostics.
        const string source = """
            const replacement: any = { child: 1 };
            class Base { constructor() { return replacement; } }
            class Early extends Base { child = 2; constructor() { return replacement; } }
            console.log(new Early() === replacement, replacement.child);
            class Missing extends Base { constructor() {} }
            try { new Missing(); console.log(false); }
            catch (error) { console.log(error instanceof ReferenceError); }
            """;
        Assert.Equal("true 1\ntrue\n", TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }
}
