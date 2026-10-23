using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassPrototypeReflectDeleteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            class C {}
            const prototype: any = C.prototype;
            prototype.extra = 7;
            console.log(Reflect.deleteProperty(prototype, "extra"), Object.hasOwn(prototype, "extra"), prototype.extra);
            """, "true false undefined\n"];
        yield return ["string-data-and-accessors-obey-configurability", """
            let reads = 0;
            const C = class {};
            const prototype: any = C.prototype;
            Object.defineProperty(prototype, "data", { value: 3, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(prototype, "access", { get() { reads++; return 7; }, configurable: true });
            Object.defineProperty(prototype, "locked", { value: 9, writable: true, configurable: false });
            console.log(Reflect.deleteProperty(prototype, "data"), Object.hasOwn(prototype, "data"));
            console.log(Reflect.deleteProperty(prototype, "access"), Object.getOwnPropertyDescriptor(prototype, "access") === undefined, reads);
            console.log(Reflect.deleteProperty(prototype, "locked"), delete prototype.locked, Object.hasOwn(prototype, "locked"), prototype.locked);
            function strictRemove(target: any) {
                "use strict";
                try { delete target.locked; console.log("missed"); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            strictRemove(prototype);
            """, "true false\ntrue true 0\nfalse false true 9\ntrue\n"];
        yield return ["symbol-data-accessors-and-direct-delete-parity", """
            class C {}
            const prototype: any = C.prototype;
            const reflected = Symbol("reflected"), direct = Symbol("direct"), access = Symbol("access"), locked = Symbol("locked");
            let reads = 0;
            prototype[reflected] = 3; prototype[direct] = 5;
            Object.defineProperty(prototype, access, { get() { reads++; return 7; }, configurable: true });
            Object.defineProperty(prototype, locked, { value: 9, configurable: false });
            console.log(Reflect.deleteProperty(prototype, reflected), delete prototype[direct], Object.hasOwn(prototype, reflected), Object.hasOwn(prototype, direct));
            console.log(Reflect.deleteProperty(prototype, access), reads, Object.getOwnPropertyDescriptor(prototype, access) === undefined);
            console.log(Reflect.deleteProperty(prototype, locked), Object.hasOwn(prototype, locked), prototype[locked]);
            console.log(Reflect.deleteProperty(prototype, reflected), Reflect.deleteProperty(prototype, "absent"), reads);
            """, "true true false false\ntrue 0 true\nfalse true 9\ntrue true 0\n"];
        yield return ["declared-method-accessor-and-constructor-own-reflection", """
            let reads = 0;
            class C {
                method() { return 7; }
                get value() { reads++; return 9; }
            }
            const prototype: any = C.prototype;
            const held = Object.getOwnPropertyDescriptor(prototype, "method")!.value;
            console.log(Reflect.deleteProperty(prototype, "method"), Object.hasOwn(prototype, "method"), Object.getOwnPropertyDescriptor(prototype, "method") === undefined, held.call(prototype));
            console.log(Reflect.deleteProperty(prototype, "value"), Object.hasOwn(prototype, "value"), reads);
            console.log(Reflect.deleteProperty(prototype, "constructor"), Object.hasOwn(prototype, "constructor"), Reflect.getOwnPropertyDescriptor(prototype, "constructor") === undefined);
            console.log(Object.getOwnPropertyNames(prototype).length, Reflect.ownKeys(prototype).length, reads);
            """, "true false true 7\ntrue false 0\ntrue false true\n0 0 0\n"];
        yield return ["deletion-is-own-only-and-preserves-inherited-properties", """
            class Parent {}
            class Child<T> extends Parent {}
            const parent: any = Parent.prototype, child: any = Child.prototype;
            Object.defineProperty(parent, "inherited", { value: 5, writable: true, configurable: false });
            child.shadow = 7;
            Object.defineProperty(parent, "shadow", { value: 3, configurable: true });
            console.log(Reflect.deleteProperty(child, "inherited"), Object.hasOwn(child, "inherited"), Object.hasOwn(parent, "inherited"), parent.inherited);
            console.log(Reflect.deleteProperty(child, "shadow"), Object.hasOwn(child, "shadow"), child.shadow, Object.hasOwn(parent, "shadow"));
            console.log(Reflect.deleteProperty(child, "absent"), Object.hasOwn(child, "absent"));
            """, "true false true 5\ntrue false 3 true\ntrue false\n"];
        yield return ["absent-and-present-properties-on-nonextensible-prototypes", """
            class C {}
            const open: any = C.prototype;
            open.extra = 1;
            Object.preventExtensions(open);
            console.log(Reflect.deleteProperty(open, "extra"), Object.hasOwn(open, "extra"), Reflect.deleteProperty(open, "absent"));
            """, "true false true\n"];
        yield return ["sealed-and-frozen-prototype-properties", """
            class S {}
            class F {}
            const sealed: any = S.prototype, frozen: any = F.prototype;
            sealed.extra = 2; frozen.extra = 3;
            Object.seal(sealed); Object.freeze(frozen);
            console.log(Reflect.deleteProperty(sealed, "extra"), Object.hasOwn(sealed, "extra"), Reflect.deleteProperty(sealed, "absent"));
            console.log(Reflect.deleteProperty(frozen, "extra"), Object.hasOwn(frozen, "extra"), Reflect.deleteProperty(frozen, "absent"));
            """, "false true true\nfalse true true\n"];
    }

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
        {
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
        }
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ReflectDeletePreservesClassPrototypePropertySemantics(
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
