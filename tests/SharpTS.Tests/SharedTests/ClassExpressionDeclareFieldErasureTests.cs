using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassExpressionDeclareFieldErasureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", """
            const Model = class {
                declare id: number;
                name: string = "default";
            };
            const m = new Model();
            console.log(m.id);
            console.log(m.name);
            m.id = 123;
            console.log(m.id);
            """, "undefined\ndefault\n123\n"];
        yield return ["presence-ordinary-field-and-initializer-order", """
            const events: string[] = [];
            const Model = class {
                first = (events.push("first"), 1);
                declare erased: number;
                ordinary: any;
                last = (events.push("last"), 2);
            };
            const value: any = new Model();
            console.log(Object.hasOwn(value, "erased"), "erased" in value, value.erased === undefined);
            console.log(Object.hasOwn(value, "ordinary"), Object.hasOwn(value, "first"), Object.hasOwn(value, "last"), events.join(","));
            console.log(Object.keys(value).join(","));
            """, "false false true\ntrue true true first,last\nfirst,ordinary,last\n"];
        yield return ["inherited-data-and-accessors-remain-visible", """
            let setters = 0, stored = 7, receiver = false;
            let current: any;
            class Parent { data = 5; }
            Object.defineProperty(Parent.prototype, "value", {
                get() { return stored; },
                set(value: number) { setters++; receiver = this === current; stored = value; },
                configurable: true
            });
            const Child = class extends Parent {
                declare data: number;
                declare value: number;
            };
            current = new Child();
            console.log(current.data, current.value, setters, Object.hasOwn(current, "data"), Object.hasOwn(current, "value"));
            current.value = 9;
            console.log(current.value, setters, receiver, Object.hasOwn(current, "value"));
            """, "5 7 0 true false\n9 1 true false\n"];
        yield return ["readonly-and-typescript-private-declarations-erase", """
            const Model = class {
                declare readonly id: number;
                declare private internal: number;
                read() { return this.internal; }
            };
            const value: any = new Model();
            console.log(Object.hasOwn(value, "id"), Object.hasOwn(value, "internal"), value.id === undefined, value.read() === undefined);
            value.id = 17; value.internal = 19;
            const descriptor = Object.getOwnPropertyDescriptor(value, "id")!;
            console.log(value.id, value.read(), descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "false false true true\n17 19 true true true\n"];
        yield return ["static-declarations-erase-before-live-writes", """
            const Model = class {
                declare static version: string;
                declare static readonly token: number;
                static actual = 3;
            };
            const target: any = Model;
            console.log(Object.hasOwn(target, "version"), Object.hasOwn(target, "token"), target.version === undefined, target.token === undefined, target.actual);
            target.version = "v1"; target.token = 7;
            console.log(target.version, target.token, Object.hasOwn(target, "version"), Object.getOwnPropertyDescriptor(target, "token")!.writable);
            """, "false false true true 3\nv1 7 true true\n"];
        yield return ["native-array-subclass-erasure", """
            const Values = class extends Array<number> {
                declare tag: string;
                actual = 5;
                constructor() { super(1, 2); }
            };
            const value: any = new Values();
            console.log(Array.isArray(value), value.length, value[0], value[1], value.actual);
            console.log(Object.hasOwn(value, "tag"), "tag" in value, value.tag === undefined);
            """, "true 2 1 2 5\nfalse false true\n"];
        yield return ["native-promise-subclass-preserves-state", """
            const Pending = class extends Promise<number> {
                declare tag: string;
                actual = 5;
                constructor(executor: any) { super(executor); }
            };
            const value: any = new Pending((resolve: any) => resolve(7));
            console.log(Object.hasOwn(value, "tag"), value.actual, typeof value.then);
            value.then((result: number) => console.log(result, value.actual));
            """, "false 5 function\n7 5\n"];
        yield return ["instances-and-later-own-property-descriptors", """
            const Model = class { declare id: number; actual = 3; };
            const first: any = new Model(), second: any = new Model();
            console.log(Object.hasOwn(first, "id"), Object.hasOwn(second, "id"));
            first.id = 17;
            console.log(first.id, Object.hasOwn(second, "id"), second.id === undefined);
            const descriptor = Object.getOwnPropertyDescriptor(first, "id")!;
            console.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """, "false false\n17 false true\n17 true true true\n"];
    }

    // Compiler gaps: inherited field presence (#2374) and erased static reads (#2711).
    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        ((string)row[0] is "inherited-data-and-accessors-remain-visible"
            or "static-declarations-erase-before-live-writes"
            ? new[] { ExecutionMode.Interpreted }
            : new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DeclareFieldsAreErasedWithoutChangingRuntimeFields(
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
