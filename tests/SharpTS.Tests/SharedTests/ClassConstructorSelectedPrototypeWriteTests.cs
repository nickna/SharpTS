using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassConstructorSelectedPrototypeWriteTests
{
    public const string IssueSource = """
        class Parent {}
        Object.defineProperty(Parent, 'slot', { value: 1, writable: false, configurable: true });
        class Child extends Parent {}
        const child: any = Child, selected: any = { slot: 2 };
        Object.setPrototypeOf(child, selected);
        function write(target: any) { 'use strict'; target.slot = 7; }
        try { write(child); console.log('accepted'); }
        catch (error) { console.log('threw', error instanceof TypeError); }
        console.log(Object.hasOwn(child, 'slot'), (Parent as any).slot, selected.slot);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-replaced-readonly-parent", IssueSource, "accepted\ntrue 1 2\n"];
        yield return ["sloppy-writes-follow-selected-descriptor", """
            class Parent {}
            Object.defineProperty(Parent, 'slot', { value: 1, writable: false, configurable: true });
            class Child extends Parent {}
            const child: any = Child, selected: any = { slot: 2 };
            Object.setPrototypeOf(child, selected);
            function write(target: any) { target.slot = 7; }
            write(child);
            console.log(child.slot, Object.hasOwn(child, 'slot'), (Parent as any).slot, selected.slot);
            class Writable { static slot = 3; }
            class Blocked extends Writable {}
            const blocked: any = Blocked, readonlySelected: any = {};
            Object.defineProperty(readonlySelected, 'slot', { value: 4, writable: false, configurable: true });
            Object.setPrototypeOf(blocked, readonlySelected);
            write(blocked);
            console.log(blocked.slot, Object.hasOwn(blocked, 'slot'), Writable.slot, readonlySelected.slot);
            """, "7 true 1 2\n4 false 3 4\n"];
        yield return ["typed-class-write-creates-own-property", """
            class Parent { static slot = 1; }
            Object.defineProperty(Parent, 'slot', { value: 1, writable: false, configurable: true });
            class Child extends Parent {}
            const selected: any = { slot: 2 };
            Object.setPrototypeOf(Child, selected);
            function write() { 'use strict'; Child.slot = 7; }
            write();
            console.log(Child.slot, Parent.slot, selected.slot, Object.hasOwn(Child, 'slot'));
            """, "7 1 2 true\n"];
        yield return ["own-readonly-masks-selected-writable-data", """
            class Parent {}
            class Child extends Parent {}
            const child: any = Child, selected: any = { slot: 2 };
            Object.defineProperty(child, 'slot', { value: 5, writable: false, configurable: true });
            Object.setPrototypeOf(child, selected);
            function write(target: any) { 'use strict'; target.slot = 7; }
            try { write(child); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Reflect.set(child, 'slot', 9), child.slot, selected.slot, Object.hasOwn(child, 'slot'));
            """, "true\nfalse 5 2 true\n"];
        yield return ["selected-readonly-masks-original-writable-data", """
            class Parent { static slot = 1; }
            class Child extends Parent {}
            const child: any = Child, selected: any = {};
            Object.defineProperty(selected, 'slot', { value: 2, writable: false, configurable: true });
            Object.setPrototypeOf(child, selected);
            function write(target: any) { 'use strict'; target.slot = 7; }
            try { write(child); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Reflect.set(child, 'slot', 9), child.slot, Parent.slot, selected.slot,
                Object.hasOwn(child, 'slot'));
            """, "true\nfalse 2 1 2 false\n"];
        yield return ["selected-setter-receives-actual-receiver", """
            class Parent {}
            Object.defineProperty(Parent, 'slot', { value: 1, writable: false, configurable: true });
            class Child extends Parent { static marker = 0; }
            const child: any = Child, selected: any = {}, alternate: any = { marker: 0 }, seen: any[] = [];
            Object.defineProperty(selected, 'slot', { set: function(this: any, value: number) {
                seen.push(this); this.marker = value;
            }, configurable: true });
            Object.setPrototypeOf(child, selected);
            function key(): any { return 'slot'; }
            function write(target: any) { 'use strict'; target.slot = 7; target[key()] = 8; }
            write(child);
            console.log(Reflect.set(child, 'slot', 9, alternate));
            console.log(child.marker, alternate.marker, seen.length, seen[0] === child, seen[1] === child,
                seen[2] === alternate, Object.hasOwn(child, 'slot'), (Parent as any).slot);
            """, "true\n8 9 3 true true true false 1\n"];
        yield return ["nonextensible-and-integrity-rules", """
            const state = { sets: 0 };
            class Parent {}
            class Child extends Parent { static existing = 3; static marker = 0; }
            const child: any = Child, selected: any = { fresh: 2 };
            Object.defineProperty(selected, 'slot', { set: function(this: any, value: number) {
                state.sets++; this.marker = value;
            }, configurable: true });
            Object.setPrototypeOf(child, selected);
            Object.preventExtensions(child);
            function write(target: any, key: string, value: number) { 'use strict'; target[key] = value; }
            try { write(child, 'fresh', 7); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Reflect.set(child, 'fresh', 8));
            write(child, 'slot', 9);
            write(child, 'existing', 4);
            console.log(child.existing, child.marker, state.sets, Object.hasOwn(child, 'fresh'),
                selected.fresh, Object.isExtensible(child));
            class Sealed { static own = 1; }
            const sealed: any = Sealed;
            Object.setPrototypeOf(sealed, { fresh: 2 });
            Object.seal(sealed);
            write(sealed, 'own', 2);
            console.log(Object.isSealed(sealed), sealed.own, Reflect.set(sealed, 'fresh', 3));
            class Frozen { static own = 1; }
            const frozen: any = Frozen;
            Object.setPrototypeOf(frozen, { fresh: 2 });
            Object.freeze(frozen);
            try { write(frozen, 'own', 2); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Object.isFrozen(frozen), frozen.own, Reflect.set(frozen, 'fresh', 3));
            """, "true\nfalse\n4 9 1 false 2 false\ntrue 2 false\ntrue\ntrue 1 false\n"];
        yield return ["local-declaration-and-expression-carriers", """
            function declared(): any {
                class Parent {}
                Object.defineProperty(Parent, 'slot', { value: 1, writable: false, configurable: true });
                class Child extends Parent {}
                return { parent: Parent, child: Child };
            }
            function expression(): any {
                const Parent = class {};
                Object.defineProperty(Parent, 'slot', { value: 1, writable: false, configurable: true });
                const Child = class extends Parent {};
                return { parent: Parent, child: Child };
            }
            function write(target: any) { 'use strict'; target.slot = 7; }
            for (const factory of [declared, expression]) {
                const entry: any = factory(), child: any = entry.child, selected: any = { slot: 2 };
                Object.setPrototypeOf(child, selected);
                write(child);
                console.log(child.slot, entry.parent.slot, selected.slot, Object.hasOwn(child, 'slot'));
            }
            """, "7 1 2 true\n7 1 2 true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ConstructorWritesUseSelectedPrototypeDescriptors(
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
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
