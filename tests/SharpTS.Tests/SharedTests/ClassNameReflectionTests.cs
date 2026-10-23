using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassNameReflectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            class C {}
            const owner: any = C;
            const descriptor: any = Object.getOwnPropertyDescriptor(owner, "name");
            console.log(JSON.stringify([owner.name, Reflect.get(owner, "name"), descriptor.value]));
            console.log(Reflect.getOwnPropertyDescriptor(owner, "name")!.value);
            """, "[\"C\",\"C\",\"C\"]\nC\n"];
        yield return ["declaration-expression-and-generic-names", """
            class Declared {}
            const Explicit = class Internal {};
            class Generic<T> {}
            function report(owner: any) {
                const descriptor = Object.getOwnPropertyDescriptor(owner, "name")!;
                console.log(JSON.stringify([owner.name, Reflect.get(owner, "name"), descriptor.value]));
            }
            report(Declared); report(Explicit); report(Generic);
            """, "[\"Declared\",\"Declared\",\"Declared\"]\n[\"Internal\",\"Internal\",\"Internal\"]\n[\"Generic\",\"Generic\",\"Generic\"]\n"];
        yield return ["intrinsic-name-flags-and-readonly-writes", """
            class Named {}
            const owner: any = Named;
            const descriptor = Object.getOwnPropertyDescriptor(owner, "name")!;
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable, Object.hasOwn(owner, "name"));
            owner.name = "ignored";
            console.log(owner.name, Reflect.get(owner, "name"));
            function strictWrite(target: any) {
                "use strict";
                try { target.name = "wrong"; console.log("missed"); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            strictWrite(owner);
            console.log(owner.name, Object.getOwnPropertyDescriptor(owner, "name")!.value);
            """, "false false true true\nNamed Named\ntrue\nNamed Named\n"];
        yield return ["own-name-redefinition-and-partial-value-updates", """
            class Original {}
            const owner: any = Original;
            Object.defineProperty(owner, "name", { value: "renamed", writable: true, configurable: true });
            owner.name = "written";
            console.log(JSON.stringify([owner.name, Reflect.get(owner, "name"), Object.getOwnPropertyDescriptor(owner, "name")!.value]));
            console.log(Reflect.defineProperty(owner, "name", { value: "reflected" }));
            const descriptor = Object.getOwnPropertyDescriptor(owner, "name")!;
            console.log(JSON.stringify([owner.name, Reflect.get(owner, "name"), descriptor.value]));
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "[\"written\",\"written\",\"written\"]\ntrue\n[\"reflected\",\"reflected\",\"reflected\"]\ntrue false true\n"];
        yield return ["deletion-recreation-and-parent-name-inheritance", """
            class Parent {}
            class Child extends Parent {}
            const parent: any = Parent, child: any = Child;
            console.log(child.name, Object.hasOwn(child, "name"));
            console.log(delete child.name, Object.hasOwn(child, "name"), Object.getOwnPropertyDescriptor(child, "name") === undefined);
            console.log(child.name, Reflect.get(child, "name"), parent.name);
            Object.defineProperty(child, "name", { value: "own", writable: true, enumerable: true, configurable: true });
            console.log(child.name, Object.hasOwn(child, "name"), Object.getOwnPropertyDescriptor(child, "name")!.enumerable);
            console.log(Reflect.deleteProperty(child, "name"), child.name, parent.name, Object.hasOwn(child, "name"));
            """, "Child true\ntrue false true\nParent Parent Parent\nown true true\ntrue Parent Parent false\n"];
        yield return ["own-accessor-receiver-masks-and-abrupt-values", """
            let reads = 0;
            class Parent {}
            class Child extends Parent {}
            const parent: any = Parent, child: any = Child, alternate: any = { tag: "alternate" };
            parent.tag = "parent"; child.tag = "child";
            const getter = function(this: any) { reads++; return this.tag; };
            Object.defineProperty(parent, "name", { get: getter, configurable: true });
            console.log(parent.name, Reflect.get(parent, "name", alternate), Object.getOwnPropertyDescriptor(parent, "name")!.get === getter, reads);
            delete child.name;
            console.log(child.name, Object.hasOwn(child, "name"), reads);
            Object.defineProperty(child, "name", { get: undefined, configurable: true });
            console.log(child.name === undefined, Reflect.get(child, "name") === undefined, reads);
            const token = { marker: 1 };
            Object.defineProperty(parent, "name", { get() { throw token; }, configurable: true });
            try { Reflect.get(parent, "name"); } catch (error) { console.log(error === token); }
            console.log(typeof Object.getOwnPropertyDescriptor(parent, "name")!.get, reads);
            """, "parent alternate true 2\nchild false 3\ntrue true 3\ntrue\nfunction 3\n"];
        yield return ["computed-declaration-metadata-fields", """
            const nameKey = "name", lengthKey = "length";
            class Declared { static [nameKey] = "declared"; static [lengthKey] = 7; }
            const owner: any = Declared;
            console.log(owner.name, owner.length, Object.getOwnPropertyDescriptor(owner, "name")!.enumerable, Object.getOwnPropertyDescriptor(owner, "length")!.writable);
            """, "declared 7 true true\n"];
        yield return ["computed-expression-fields-and-method-accessor-metadata", """
            const nameKey = "name", lengthKey = "length";
            const Expression = class Explicit { static [nameKey] = "expression"; static [lengthKey] = 9; };
            class Method { static [nameKey]() { return 11; } static get [lengthKey]() { return 13; } }
            const Accessor = class Internal { static get [nameKey]() { return "getter"; } static [lengthKey]() { return 17; } };
            const expression: any = Expression, method: any = Method, accessor: any = Accessor;
            console.log(expression.name, expression.length, Object.getOwnPropertyDescriptor(expression, "name")!.enumerable, Object.getOwnPropertyDescriptor(expression, "length")!.writable);
            console.log(method.name(), method.length, Object.getOwnPropertyDescriptor(method, "name")!.enumerable, typeof Object.getOwnPropertyDescriptor(method, "length")!.get);
            console.log(accessor.name, accessor.length(), typeof Object.getOwnPropertyDescriptor(accessor, "name")!.get, Object.getOwnPropertyDescriptor(accessor, "length")!.enumerable);
            """, "expression 9 true true\n11 13 false function\ngetter 17 function false\n"];
        yield return ["metadata-integrity", """
        class Sealed {}
        const sealed: any = Sealed;
        Object.seal(sealed);
        const sn: any = Object.getOwnPropertyDescriptor(sealed, "name");
        const sl: any = Object.getOwnPropertyDescriptor(sealed, "length");
        console.log("seal", sn.writable, sn.enumerable, sn.configurable, sl.writable, sl.enumerable, sl.configurable);
        console.log(Reflect.deleteProperty(sealed, "name"), Reflect.deleteProperty(sealed, "length"),
            Reflect.defineProperty(sealed, "name", { value: "blocked" }), Reflect.defineProperty(sealed, "length", { value: 99 }),
            Object.hasOwn(sealed, "name"), Object.hasOwn(sealed, "length"));
        class Frozen {}
        const frozen: any = Frozen;
        Object.freeze(frozen);
        const fn: any = Object.getOwnPropertyDescriptor(frozen, "name");
        const fl: any = Object.getOwnPropertyDescriptor(frozen, "length");
        console.log("freeze", fn.writable, fn.enumerable, fn.configurable, fl.writable, fl.enumerable, fl.configurable);
        console.log(Reflect.deleteProperty(frozen, "name"), Reflect.deleteProperty(frozen, "length"),
            Reflect.defineProperty(frozen, "name", { value: "blocked" }), Reflect.defineProperty(frozen, "length", { value: 99 }),
            Object.hasOwn(frozen, "name"), Object.hasOwn(frozen, "length"));
        class Parent {}
        class Child extends Parent {}
        const child: any = Child;
        console.log("delete", Reflect.deleteProperty(child, "name"), Object.hasOwn(child, "name"), child.name, Reflect.get(child, "name"));
        Object.preventExtensions(child);
        console.log("recreate", Reflect.defineProperty(child, "name", { value: "blocked" }),
            Object.getOwnPropertyDescriptor(child, "name") === undefined,
            Object.getOwnPropertyNames(child).includes("name"), child.name, Reflect.get(child, "name"));
        """, "seal false false false false false false\nfalse false false false true true\nfreeze false false false false false false\nfalse false false false true true\ndelete true false Parent Parent\nrecreate false true false Parent Parent\n"];

    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ClassNameReadsAndDescriptorsShareOwnMetadata(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
