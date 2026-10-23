using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StrictConstructorExpandoTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", """
            class C {}
            const owner: any = C;
            owner.value = 1;
            function strictWrite(target: any, value: any) { "use strict"; target.value = value; }
            strictWrite(owner, 2);
            console.log(owner.value);
            console.log(delete owner.value);
            strictWrite(owner, 3);
            console.log(owner.value);
            """, "2\ntrue\n3\n"];
        yield return ["lifecycle", """
            function write(target: any, value: any) { "use strict"; target.value = value; }
            function indexed(target: any, key: string, value: any) { "use strict"; target[key] = value; }
            class C {}
            class Generic<T> {}
            const owners: any[] = [C, Generic, class Named {}, class {}];
            for (const owner of owners) {
                for (const value of [null, undefined, 3]) {
                    write(owner, value);
                    indexed(owner, "value", value);
                    const descriptor: any = Object.getOwnPropertyDescriptor(owner, "value");
                    console.log(owner.value === value, descriptor.value === value, Object.hasOwn(owner, "value"),
                        descriptor.writable, descriptor.enumerable, descriptor.configurable);
                    delete owner.value;
                }
                Object.defineProperty(owner, "value", { value: 4, writable: true, enumerable: false, configurable: true });
                write(owner, 5);
                const descriptor: any = Object.getOwnPropertyDescriptor(owner, "value");
                console.log(owner.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            }
            """, string.Concat(Enumerable.Repeat(
                "true true true true true true\ntrue true true true true true\ntrue true true true true true\n5 true false true\n", 4))];
        yield return ["strict-guards", """
            function write(target: any, key: string, value: any) { "use strict"; target[key] = value; }
            let calls = 0, received: any;
            class C {}
            const owner: any = C;
            Object.defineProperty(owner, "existing", { value: 1, writable: true, configurable: true });
            Object.defineProperty(owner, "setter", {
                set: function(value: any) { calls++; received = value; console.log(this === C); }, configurable: true
            });
            Object.defineProperty(owner, "getter", { get: function() { return 1; }, configurable: true });
            Object.defineProperty(owner, "empty", { set: undefined, configurable: true });
            Object.preventExtensions(owner);
            write(owner, "existing", 2);
            write(owner, "setter", 7);
            console.log(owner.existing, calls, received);
            for (const key of ["missing", "getter", "empty"]) {
                try { write(owner, key, 9); } catch (error) { console.log(error instanceof TypeError); }
            }
            console.log(Object.hasOwn(owner, "missing"), owner.getter);
            class Frozen {}
            const frozen: any = Frozen;
            frozen.value = 1;
            Object.freeze(frozen);
            try { write(frozen, "value", 2); } catch (error) { console.log(error instanceof TypeError); }
            console.log(frozen.value);
            """, "true\n2 1 7\ntrue\ntrue\ntrue\nfalse 1\ntrue\n1\n"];
        yield return ["inheritance", """
            function write(target: any, value: any) { "use strict"; target.value = value; }
            let calls = 0, received: any;
            class Base {}
            class Derived extends Base {}
            Object.defineProperty(Base, "value", {
                set: function(value: any) { calls++; received = value; console.log(this === Derived); }, configurable: true
            });
            Object.preventExtensions(Derived);
            write(Derived, 7);
            console.log(calls, received, Object.hasOwn(Derived, "value"));
            class ReadonlyBase {}
            class ReadonlyChild extends ReadonlyBase {}
            Object.defineProperty(ReadonlyBase, "value", { value: 1, writable: false });
            try { write(ReadonlyChild, 2); } catch (error) { console.log(error instanceof TypeError); }
            console.log((ReadonlyBase as any).value, Object.hasOwn(ReadonlyChild, "value"));
            class Far {}
            class Middle extends Far { static value() { return 2; } }
            class Leaf extends Middle {}
            Object.defineProperty(Far, "value", { value: 1, writable: false });
            write(Leaf, 3);
            console.log((Leaf as any).value, typeof Middle.value, (Far as any).value);
            """, "true\n1 7 false\ntrue\n1 false\n3 function 1\n"];
        yield return ["prototype-tail", """
            function write(target: any, key: string, value: any) { "use strict"; target[key] = value; }
            const functions: any = Function.prototype, objects: any = Object.prototype;
            const key = "__strictConstructorTail";
            class C {}
            Object.defineProperty(functions, key, { get: function() { return 1; }, configurable: true });
            try { write(C, key, 2); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.hasOwn(C, key));
            delete functions[key];
            Object.defineProperty(objects, key, { value: 1, writable: false, configurable: true });
            try { write(C, key, 2); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.hasOwn(C, key));
            Object.defineProperty(functions, key, { value: 3, writable: true, configurable: true });
            write(C, key, 4);
            console.log((C as any)[key], functions[key], objects[key]);
            delete (C as any)[key];
            delete functions[key];
            delete objects[key];
            let calls = 0;
            Object.defineProperty(functions, key, {
                set: function(value: any) { calls++; console.log(this === C, value); }, configurable: true
            });
            Object.preventExtensions(C);
            write(C, key, 5);
            console.log(calls, Object.hasOwn(C, key));
            delete functions[key];
            class Named {}
            const named: any = Named;
            delete named.name;
            try { write(named, "name", "replacement"); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.hasOwn(named, "name"));
            """, "true\nfalse\ntrue\nfalse\n4 3 1\ntrue 5\n1 false\ntrue\nfalse\n"];
        yield return ["prototype-metadata", """
            function write(target: any, key: string, value: any) { "use strict"; target[key] = value; }
            const functions: any = Function.prototype;
            for (const key of ["name", "length"]) {
                const descriptor: any = Object.getOwnPropertyDescriptor(functions, key);
                console.log(descriptor.value === (key === "name" ? "" : 0),
                    descriptor.writable, descriptor.enumerable, descriptor.configurable);
                class C {}
                const owner: any = C;
                delete owner[key];
                try { write(owner, key, 7); } catch (error) { console.log(error instanceof TypeError); }
                console.log(Object.hasOwn(owner, key));
                delete functions[key];
                write(owner, key, 8);
                console.log(owner[key], Object.hasOwn(owner, key));
                delete owner[key];
                Object.defineProperty(functions, key, { value: 9, writable: true, configurable: true });
                write(owner, key, 10);
                console.log(owner[key], functions[key]);
                Object.defineProperty(functions, key, descriptor);
            }
            """, string.Concat(Enumerable.Repeat("true false false true\ntrue\nfalse\n8 true\n10 9\n", 2))];
        yield return ["proxy-tail", """
            function write(target: any, key: string, value: any) { "use strict"; target[key] = value; }
            class C {}
            const functions: any = Function.prototype, original = Object.getPrototypeOf(functions);
            const key = "__strictConstructorProxyTail";
            let calls = 0, accept = true;
            const events: string[] = [];
            const proxy: any = new Proxy({}, {
                set(target: any, property: any, value: any, receiver: any) {
                    if (property !== key || receiver !== C) return Reflect.set(target, property, value, receiver);
                    calls++;
                    events.push("set:" + value + ":" + (receiver === C));
                    return accept;
                },
                getOwnPropertyDescriptor() { throw new Error("wrong descriptor trap"); },
                getPrototypeOf() { throw new Error("wrong prototype trap"); }
            });
            Object.setPrototypeOf(functions, proxy);
            write(C, key, 9);
            events.push(calls + ":" + Object.hasOwn(C, key));
            accept = false;
            try { write(C, key, 10); } catch (error) { events.push(String(error instanceof TypeError)); }
            events.push(calls + ":" + Object.hasOwn(C, key));
            Object.setPrototypeOf(functions, original);
            console.log(events.join("|"));
            """, "set:9:true|1:false|set:10:true|true|2:false\n"];
    }

    public static IEnumerable<object[]> ParityCases()
        => Cases().SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ParityCases))]
    public void StrictAssignmentCreatesOrdinaryConstructorProperties(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
