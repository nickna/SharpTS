using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class FunctionInternalPrototypeLookupTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["repro", """
            function F() {}
            const fn: any = F;
            const custom = { marker: 7 };
            Object.setPrototypeOf(fn, custom);
            console.log(Object.getPrototypeOf(fn) === custom, fn.marker);
            Object.setPrototypeOf(fn, null);
            console.log(Object.getPrototypeOf(fn) === null, typeof fn.bind);
            """ + "\n", "true 7\ntrue undefined\n"];
        yield return ["forms", """
            function declaration() { return 1; }
            const expression = function () { return 2; };
            const arrow = () => 3;
            const custom = { marker: 7 };

            Object.setPrototypeOf(declaration, custom);
            console.log("declaration", (declaration as any).marker, Reflect.apply(declaration, null, []));
            Object.setPrototypeOf(declaration, null);
            console.log("declaration-null", typeof (declaration as any).bind, Object.getPrototypeOf(declaration) === null);

            Object.setPrototypeOf(expression, custom);
            console.log("expression", (expression as any).marker, Reflect.apply(expression, null, []));
            Object.setPrototypeOf(expression, null);
            console.log("expression-null", typeof (expression as any).bind, Object.getPrototypeOf(expression) === null);

            Object.setPrototypeOf(arrow, custom);
            console.log("arrow", (arrow as any).marker, Reflect.apply(arrow, null, []));
            Object.setPrototypeOf(arrow, null);
            console.log("arrow-null", typeof (arrow as any).bind, Object.getPrototypeOf(arrow) === null);

            function dynamic() { return 4; }
            const fn: any = dynamic;
            Object.setPrototypeOf(fn, custom);
            console.log("any", fn.marker, Reflect.apply(fn, null, []));
            Object.setPrototypeOf(fn, null);
            console.log("any-null", typeof fn.bind, Object.getPrototypeOf(fn) === null);
            """ + "\n", "declaration 7 1\ndeclaration-null undefined true\nexpression 7 2\nexpression-null undefined true\narrow 7 3\narrow-null undefined true\nany 7 4\nany-null undefined true\n"];
        yield return ["reflection-control", """
            function F() {}
            const fn: any = F;
            fn.own = 9;
            const custom = { marker: 7 };
            Object.setPrototypeOf(fn, custom);
            console.log(fn.own, Reflect.get(fn, "marker"), "marker" in fn, Reflect.has(fn, "marker"));
            Object.setPrototypeOf(fn, null);
            console.log(fn.own, typeof Reflect.get(fn, "bind"), "bind" in fn, Reflect.has(fn, "bind"));
            """ + "\n", "9 7 true true\n9 undefined false false\n"];
        yield return ["metadata-control", """
            function F(value: number) {}
            const fn: any = F;
            delete fn.name;
            delete fn.length;
            const custom = { name: "custom", length: 9 };
            Object.setPrototypeOf(fn, custom);
            console.log(fn.name, fn["length"], Reflect.get(fn, "name"));
            Object.setPrototypeOf(fn, null);
            console.log(fn.name, fn.length, Reflect.get(fn, "length"));
            """ + "\n", "custom 9 custom\nundefined undefined undefined\n"];
        yield return ["accessor-own-and-internal-prototype-preservation", """
            function owner(value: number) { return value + 1; }
            const fn: any = owner;
            const originalInternal = Object.getPrototypeOf(fn);
            const ownPrototype = fn.prototype;
            let getterCalls = 0;
            const custom: any = { own: 70, name: "inherited", length: 99, prototype: { inherited: true } };
            const installed: any = Object.defineProperty(custom, "marker", {
                configurable: true,
                get: function (this: any) {
                    getterCalls++;
                    return this === fn ? "owner" : this.tag;
                }
            });
            fn.own = 9;
            Object.setPrototypeOf(fn, custom);
            console.log(fn.marker, fn["marker"], Reflect.get(fn, "marker"), Reflect.get(fn, "marker", { tag: "alternate" }), getterCalls);
            console.log("marker" in fn, Reflect.has(fn, "marker"), Object.hasOwn(fn, "marker"), getterCalls);
            console.log(fn.own, fn.name, fn.length, fn.prototype === ownPrototype, Object.getPrototypeOf(fn) === custom, Reflect.apply(fn, null, [4]));
            Object.setPrototypeOf(fn, null);
            console.log(typeof fn.marker, "marker" in fn, Reflect.has(fn, "marker"), typeof fn.call, typeof fn.apply, typeof fn.bind, fn.prototype === ownPrototype, Reflect.apply(fn, null, [5]));
            Object.setPrototypeOf(fn, originalInternal);
            const bound: any = fn.bind(null, 4);
            console.log(typeof fn.call, typeof fn.apply, typeof fn.bind, fn.call(null, 4), fn.apply(null, [4]), bound(), fn.prototype === ownPrototype, getterCalls);
            """ + "\n", "owner owner owner alternate 4\ntrue true false 4\n9 owner 1 true true 5\nundefined false false undefined undefined undefined true 6\nfunction function function 5 5 5 true 4\n"];
        yield return ["nonconstructible-inherited-prototype-ownership", """
            const arrow: any = (value: number) => value + 1;
            const object: any = { method(value: number) { return value + 2; } };
            const native: any = Math.abs;
            for (const target of [arrow, object.method, native]) {
                const original = Object.getPrototypeOf(target);
                let reads = 0;
                const inherited = {};
                const selected: any = Object.create(original);
                Object.defineProperty(selected, "prototype", {
                    get: function () { reads++; return inherited; },
                    configurable: true
                });
                Object.setPrototypeOf(target, selected);
                console.log(Object.hasOwn(target, "prototype"),
                    Object.getOwnPropertyDescriptor(target, "prototype") === undefined,
                    "prototype" in target, Reflect.has(target, "prototype"), reads);
                console.log(target.prototype === inherited, target["prototype"] === inherited,
                    Reflect.get(target, "prototype") === inherited, reads);
                const own = {};
                Object.defineProperty(target, "prototype", { value: own, writable: true, configurable: true });
                const descriptor: any = Object.getOwnPropertyDescriptor(target, "prototype");
                console.log(Object.hasOwn(target, "prototype"), descriptor.value === own,
                    descriptor.writable, descriptor.configurable, reads);
                console.log(delete target.prototype, Object.hasOwn(target, "prototype"),
                    Object.getOwnPropertyDescriptor(target, "prototype") === undefined, reads);
                console.log(target.prototype === inherited, Reflect.apply(target, null, [-3]), reads);
                Object.setPrototypeOf(target, original);
            }
            """ + "\n", "false true true true 0\ntrue true true 3\ntrue true true true 3\ntrue false true 3\ntrue -2 4\nfalse true true true 0\ntrue true true 3\ntrue true true true 3\ntrue false true 3\ntrue -1 4\nfalse true true true 0\ntrue true true 3\ntrue true true true 3\ntrue false true 3\ntrue 3 4\n"];
    }

    public static IEnumerable<object[]> StandaloneCases()
    {
        foreach (var row in Cases())
            yield return row;
        foreach (var row in FunctionPrototypeMutationTests.Cases())
            if ((string)row[0] is "ordinary-function-forms" or "inherited-data-getter-and-symbol" or "deleted-metadata")
                yield return row;
        foreach (var row in FunctionIntrinsicPrototypeMutationTests.Cases())
            if ((string)row[0] is "inherited-reads-receivers-and-presence" or "deleted-intrinsics-reveal-live-parent"
                or "absent-callable-metadata-and-inherited-write")
                yield return row;
        foreach (var row in FunctionBracketPropertyTests.Cases())
            if ((string)row[0] == "custom-null-restored-prototype")
                yield return row;
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ReadsAndPresenceUseTheActualInternalPrototype(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
