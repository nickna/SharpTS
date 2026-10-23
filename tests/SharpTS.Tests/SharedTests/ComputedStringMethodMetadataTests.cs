using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ComputedStringMethodMetadataTests
{
    public const string IssueSource = """
        class C { [""]() { return 1; } static [""]() { return 2; } }
        const E: any = class { [""]() { return 3; } static [""]() { return 4; } };
        for (const constructor of [C, E]) {
            const owner: any = constructor, object: any = new owner();
            const instance: any = object[""], statik: any = owner[""];
            const instanceDescriptor: any = Object.getOwnPropertyDescriptor(owner.prototype, "");
            const staticDescriptor: any = Object.getOwnPropertyDescriptor(owner, "");
            console.log(instance(), statik(), JSON.stringify(instance.name), JSON.stringify(statik.name));
            console.log(instanceDescriptor !== undefined, staticDescriptor !== undefined);
            if (instanceDescriptor !== undefined) console.log(instanceDescriptor.value === instance, instanceDescriptor.writable, instanceDescriptor.enumerable, instanceDescriptor.configurable);
            if (staticDescriptor !== undefined) console.log(staticDescriptor.value === statik, staticDescriptor.writable, staticDescriptor.enumerable, staticDescriptor.configurable);
        }
        """;

    public const string KindsSource = """
        const plain = "", asynchronous = "async", generator = 7, asyncGenerator = "asyncGenerator";
        const keys = [plain, asynchronous, generator, asyncGenerator];
        class Declared {
            [plain](first: any) { return this; }
            async [asynchronous](first: any) { await Promise.resolve(0); return this; }
            *[generator](first: any) { yield this; }
            async *[asyncGenerator](first: any) { await Promise.resolve(0); yield this; }
            static [plain](first: any) { return this; }
            static async [asynchronous](first: any) { await Promise.resolve(0); return this; }
            static *[generator](first: any) { yield this; }
            static async *[asyncGenerator](first: any) { await Promise.resolve(0); yield this; }
        }
        const Expression: any = class {
            [plain](first: any) { return this; }
            async [asynchronous](first: any) { await Promise.resolve(0); return this; }
            *[generator](first: any) { yield this; }
            async *[asyncGenerator](first: any) { await Promise.resolve(0); yield this; }
            static [plain](first: any) { return this; }
            static async [asynchronous](first: any) { await Promise.resolve(0); return this; }
            static *[generator](first: any) { yield this; }
            static async *[asyncGenerator](first: any) { await Promise.resolve(0); yield this; }
        };
        async function run() {
            for (const constructor of [Declared, Expression]) {
                const owner: any = constructor, instance: any = new owner(), receiver = {};
                for (const key of keys) {
                    const im: any = instance[key], sm: any = owner[key];
                    const id: any = Object.getOwnPropertyDescriptor(owner.prototype, key);
                    const sd: any = Object.getOwnPropertyDescriptor(owner, key);
                    console.log(JSON.stringify(im.name), JSON.stringify(sm.name), im.length, sm.length,
                        im === owner.prototype[key], im === id.value, sm === sd.value,
                        id.writable && sd.writable, id.enumerable || sd.enumerable, id.configurable && sd.configurable);
                }
                console.log(instance[plain].call(receiver, 1) === receiver,
                    owner[plain].call(receiver, 1) === receiver,
                    await instance[asynchronous].call(receiver, 1) === receiver,
                    await owner[asynchronous].call(receiver, 1) === receiver);
                const iterator = instance[generator].call(receiver, 1);
                const staticIterator = owner[generator].call(receiver, 1);
                const asyncIterator = instance[asyncGenerator].call(receiver, 1);
                const staticAsyncIterator = owner[asyncGenerator].call(receiver, 1);
                console.log(iterator.next().value === receiver, staticIterator.next().value === receiver,
                    (await asyncIterator.next()).value === receiver, (await staticAsyncIterator.next()).value === receiver);
                console.log(Object.getPrototypeOf(iterator) === instance[generator].prototype,
                    Object.getPrototypeOf(staticIterator) === owner[generator].prototype,
                    Object.getPrototypeOf(asyncIterator) === instance[asyncGenerator].prototype,
                    Object.getPrototypeOf(staticAsyncIterator) === owner[asyncGenerator].prototype);
            }
        }
        run().then(() => console.log("done"), error => console.log("unexpected", error));
        """;

    public const string DeferredSource = """
        const state = { calls: 0, order: "" };
        function key(label: string, value: string | number): any {
            state.calls++; state.order += label; return value;
        }
        function makeDeclared() {
            class Declared {
                [key("a", "")]() { return 1; }
                static [key("b", 3)]() { return 2; }
                [key("c", "text")]() { return 3; }
            }
            return Declared;
        }
        const D: any = makeDeclared();
        const E: any = class {
            [key("d", "")]() { return 4; }
            static [key("e", 3)]() { return 5; }
            [key("f", "text")]() { return 6; }
        };
        console.log(state.order, state.calls);
        for (const owner of [D, E]) {
            const instance: any = new owner();
            console.log(instance[""](), owner[3](), instance.text(), JSON.stringify(instance[""].name), owner[3].name, instance.text.name);
            console.log(instance[""] === owner.prototype[""],
                instance.text === Object.getOwnPropertyDescriptor(owner.prototype, "text")!.value,
                owner[3] === Object.getOwnPropertyDescriptor(owner, "3")!.value);
        }
        console.log(state.order, state.calls);
        """;

    public const string MutationSource = """
        class Declared { [""]() { return this; } static [""]() { return this; } }
        const Expression: any = class { [""]() { return this; } static [""]() { return this; } };
        for (const constructor of [Declared, Expression]) {
            const owner: any = constructor, instance: any = new owner(), prototype: any = owner.prototype;
            const im: any = instance[""], sm: any = owner[""];
            Object.defineProperty(im, "name", { value: "instance-renamed", configurable: true });
            Object.defineProperty(sm, "name", { value: "static-renamed", configurable: true });
            console.log(instance[""] === im, owner[""] === sm, instance[""].name, owner[""].name);
            console.log(delete im.name, delete sm.name, Object.hasOwn(im, "name"), Object.hasOwn(sm, "name"),
                instance[""] === im, owner[""] === sm);
            console.log(JSON.stringify(im.name), JSON.stringify(sm.name));
            console.log(delete prototype[""], delete owner[""], typeof instance[""], typeof new owner()[""], typeof owner[""],
                Object.getOwnPropertyDescriptor(prototype, "") === undefined, Object.getOwnPropertyDescriptor(owner, "") === undefined);
            Object.defineProperty(prototype, "", { value: undefined, writable: true, configurable: true });
            Object.defineProperty(owner, "", { value: undefined, writable: true, configurable: true });
            console.log(Object.hasOwn(prototype, ""), Object.hasOwn(owner, ""), typeof instance[""], typeof owner[""]);
            const replacement = function(this: any) { return this; };
            Object.defineProperty(prototype, "", { value: replacement, writable: true, configurable: true });
            Object.defineProperty(owner, "", { value: replacement, writable: true, configurable: true });
            console.log(instance[""] === replacement, owner[""] === replacement,
                instance[""]() === instance, owner[""]() === owner);
            delete prototype[""]; delete owner[""];
            console.log(typeof instance[""], typeof owner[""], typeof new owner()[""]);
        }
        """;

    public const string GenericSource = """
        const key = "method";
        class Base<T extends number> {
            [key](first: any) { return this; }
            static [key](first: any) { return this; }
        }
        class Middle<T extends number> extends Base<T> {}
        class Derived extends Middle<number> {}
        const base: any = Base, derived: any = Derived, instance: any = new Derived(), receiver = {};
        const im: any = instance[key], sm: any = base[key];
        console.log(im.name, sm.name, im === base.prototype[key], im === derived.prototype[key],
            im === Object.getOwnPropertyDescriptor(base.prototype, key)!.value);
        console.log(sm === base[key], sm === derived[key], sm === Object.getOwnPropertyDescriptor(base, key)!.value,
            Object.getOwnPropertyDescriptor(derived, key) === undefined);
        console.log(im.call(receiver, 1) === receiver, sm.call(receiver, 1) === receiver,
            derived[key]() === derived, instance[key]() === instance);
        """;

    public const string ExpressionsSource = """
        function make(label: string) {
            return class {
                [""]() { return label; }
                static [""]() { return label; }
                *["items"]() { yield label; }
                static *["items"]() { yield label; }
            };
        }
        const First: any = make("first"), Second: any = make("second");
        const first: any = new First(), second: any = new Second();
        console.log(first[""](), second[""](), First[""](), Second[""]());
        console.log(first[""] !== second[""], First[""] !== Second[""], first.items !== second.items, First.items !== Second.items);
        for (const owner of [First, Second]) {
            const instance: any = new owner();
            console.log(instance[""] === owner.prototype[""], instance[""] === Object.getOwnPropertyDescriptor(owner.prototype, "")!.value,
                owner[""] === Object.getOwnPropertyDescriptor(owner, "")!.value);
            console.log(instance.items().next().value, owner.items().next().value, instance.items.name, owner.items.name);
        }
        """;

    public const string SourceOrderSource = """
        function key(): string { return "m"; }
        class A {
            m() { return 1; }
            [key()]() { return 2; }
            static m() { return 3; }
            static [key()]() { return 4; }
        }
        class B {
            [key()]() { return 2; }
            m() { return 1; }
            static [key()]() { return 4; }
            static m() { return 3; }
        }
        const a: any = new A(), b: any = new B(), ac: any = A, bc: any = B;
        console.log(a.m(), ac.m(), b.m(), bc.m());
        for (const owner of [ac, bc]) {
            const instance: any = new owner();
            console.log(instance.m === owner.prototype.m,
                instance.m === Object.getOwnPropertyDescriptor(owner.prototype, "m")!.value,
                owner.m === Object.getOwnPropertyDescriptor(owner, "m")!.value);
        }
        """;

    public const string StaticInitializationSource = """
        const replacement = function() { return 9; };
        class Changed {
            static [""]() { return 1; }
            static {
                const owner: any = this;
                console.log(owner[""]());
                Object.defineProperty(owner, "", { value: replacement, configurable: true });
            }
        }
        class Deleted {
            static [""]() { return 2; }
            static { const owner: any = this; console.log(owner[""]()); delete owner[""]; }
        }
        const changed: any = Changed, deleted: any = Deleted;
        console.log(changed[""] === replacement, changed[""](), typeof deleted[""], Object.hasOwn(deleted, ""));
        """;

    public const string AbstractOwnerSource = """
        function key(): string { return "m"; }
        abstract class Base { [key()]() { return 1; } }
        class Derived extends Base {}
        const instance: any = new Derived(), ordinary: any = {};
        console.log(instance.m(), typeof ordinary.m, Object.hasOwn(Object.prototype, "m"));
        """;

    public const string AccessorOrderSource = """
        function key(): string { return "m"; }
        class A {
            [key()]() { return 1; }
            get [key()]() { return function() { return 2; }; }
            static [key()]() { return 3; }
            static get [key()]() { return function() { return 4; }; }
        }
        class B {
            get [key()]() { return function() { return 1; }; }
            [key()]() { return 2; }
            static get [key()]() { return function() { return 3; }; }
            static [key()]() { return 4; }
        }
        const a: any = new A(), b: any = new B(), ac: any = A, bc: any = B;
        console.log(a.m(), ac.m(), b.m(), bc.m());
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", IssueSource, "1 2 \"\" \"\"\ntrue true\ntrue true false true\ntrue true false true\n3 4 \"\" \"\"\ntrue true\ntrue true false true\ntrue true false true\n"];
        var kinds = "\"\" \"\" 1 1 true true true true false true\n"
            + "\"async\" \"async\" 1 1 true true true true false true\n"
            + "\"7\" \"7\" 1 1 true true true true false true\n"
            + "\"asyncGenerator\" \"asyncGenerator\" 1 1 true true true true false true\n"
            + "true true true true\ntrue true true true\ntrue true true true\n";
        yield return ["ordinary-keys-all-function-kinds", KindsSource, kinds + kinds + "done\n"];
        yield return ["deferred-key-evaluation-once", DeferredSource, "abcdef 6\n1 2 3 \"\" 3 text\ntrue true true\n4 5 6 \"\" 3 text\ntrue true true\nabcdef 6\n"];
        var mutation = "true true instance-renamed static-renamed\ntrue true false false true true\n\"\" \"\"\n"
            + "true true undefined undefined undefined true true\ntrue true undefined undefined\n"
            + "true true true true\nundefined undefined undefined\n";
        yield return ["live-function-metadata-and-method-descriptors", MutationSource, mutation + mutation];
        yield return ["generic-owner-and-borrowed-receiver", GenericSource, "method method true true true\ntrue true true true\ntrue true true true\n"];
        yield return ["per-evaluation-expression-identity", ExpressionsSource, "first second first second\ntrue true true true\ntrue true true\nfirst first items items\ntrue true true\nsecond second items items\n"];
        yield return ["ordinary-computed-definition-order", SourceOrderSource, "2 4 1 3\ntrue true true\ntrue true true\n"];
        yield return ["static-initialization-keeps-live-method-mutation", StaticInitializationSource, "1\n2\ntrue 9 undefined false\n"];
        yield return ["abstract-owner-does-not-pollute-object-prototype", AbstractOwnerSource, "1 undefined false\n"];
        yield return ["computed-method-accessor-source-order", AccessorOrderSource, "2 4 2 4\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
            foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            {
                // #2423: existing interpreter instance reads resurrect deleted prototype methods
                // and do not return the replacement value. Exercise the new compiled publication path.
                if (mode == ExecutionMode.Interpreted && (string)row[0] == "live-function-metadata-and-method-descriptors")
                    continue;
                // #2235: the interpreter retains an earlier accessor after a later computed method.
                if (mode == ExecutionMode.Interpreted && (string)row[0] == "computed-method-accessor-source-order")
                    continue;
                yield return [mode, row[0], row[1], row[2]];
            }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void OrdinaryComputedMethodsHaveCanonicalNamesAndLiveDescriptors(
        ExecutionMode mode, string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
