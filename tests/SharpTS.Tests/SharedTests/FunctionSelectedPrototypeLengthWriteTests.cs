using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class FunctionSelectedPrototypeLengthWriteTests
{
    public const string IssueSource = """
        "use strict";
        function original(value: any) {}
        const target: any = original;
        delete target.length;
        Object.setPrototypeOf(target, { length: 7 });
        try {
            target.length = 8;
            console.log(Reflect.get(target, "length"));
        } catch (error: any) { console.log(error.name); }
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-deleted-length-selected-writable", IssueSource, "8\n"];
        yield return ["own-descriptors-take-precedence", """
            function original(value: any) {}
            const target: any = original;
            Object.defineProperty(target, 'length', { value: 7, writable: true, configurable: true });
            function write(target: any, value: number) { 'use strict'; target.length = value; }
            write(target, 8);
            const own = Object.getOwnPropertyDescriptor(target, 'length')!;
            console.log(Reflect.get(target, 'length'), own.writable, own.enumerable, own.configurable);
            function blocked() {}
            const readonly: any = blocked, selected = { length: 7 };
            Object.defineProperty(readonly, 'length', { value: 3, writable: false, configurable: true });
            Object.setPrototypeOf(readonly, selected);
            try { write(readonly, 8); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Reflect.get(readonly, 'length'), Object.hasOwn(readonly, 'length'), selected.length);
            function unchanged(value: any) {}
            const defaultTarget: any = unchanged;
            delete defaultTarget.length;
            try { write(defaultTarget, 8); } catch (error) { console.log(error instanceof TypeError); }
            console.log(Reflect.get(defaultTarget, 'length'), Object.hasOwn(defaultTarget, 'length'));
            """, "8 true false true\ntrue\n3 true 7\ntrue\n0 false\n"];
        yield return ["selected-readonly-strict-and-sloppy", """
            function original(value: any) {}
            const target: any = original, selected: any = {};
            delete target.length;
            Object.defineProperty(selected, 'length', { value: 7, writable: false, configurable: true });
            Object.setPrototypeOf(target, selected);
            function strictWrite(target: any) { 'use strict'; target.length = 8; }
            function sloppyWrite(target: any) { target.length = 9; }
            try { strictWrite(target); } catch (error) { console.log(error instanceof TypeError); }
            sloppyWrite(target);
            console.log(Reflect.get(target, 'length'), Object.hasOwn(target, 'length'), selected.length);
            """, "true\n7 false 7\n"];
        yield return ["nonextensible-refusal-and-null-parent-creation", """
            function original(value: any) {}
            const target: any = original, selected = { length: 7 };
            delete target.length;
            Object.setPrototypeOf(target, selected);
            Object.preventExtensions(target);
            function strictWrite(target: any) { 'use strict'; target.length = 8; }
            function sloppyWrite(target: any) { target.length = 9; }
            try { strictWrite(target); } catch (error) { console.log(error instanceof TypeError); }
            sloppyWrite(target);
            console.log(Reflect.get(target, 'length'), Object.hasOwn(target, 'length'), Object.isExtensible(target));
            function unparented() {}
            const empty: any = unparented;
            delete empty.length;
            Object.setPrototypeOf(empty, null);
            strictWrite(empty);
            const own = Object.getOwnPropertyDescriptor(empty, 'length')!;
            console.log(Reflect.get(empty, 'length'), own.writable, own.enumerable, own.configurable);
            """, "true\n7 false false\n8 true true true\n"];
        yield return ["selected-setter-keeps-function-receiver", """
            function original(value: any) {}
            const target: any = original, selected: any = {}, seen: any[] = [];
            delete target.length;
            target.marker = 0;
            const setter = function(this: any, value: number) { seen.push(this); this.marker = value; };
            Object.defineProperty(selected, 'length', { set: setter, configurable: true });
            Object.setPrototypeOf(target, selected);
            Object.preventExtensions(target);
            let keys = 0;
            function key(): any { keys++; return 'length'; }
            function write(target: any) { 'use strict'; target.length = 8; target[key()] = 9; }
            write(target);
            console.log(Reflect.get(target, 'marker'), Reflect.get(target, 'length') === undefined,
                seen.length, seen[0] === target, seen[1] === target, keys);
            console.log(Object.hasOwn(target, 'length'),
                Object.getOwnPropertyDescriptor(selected, 'length')!.set === setter, Object.isExtensible(target));
            """, "9 true 2 true true 1\nfalse true false\n"];
        yield return ["expression-and-arrow-create-own-data-once", """
            const targets: any[] = [function(value: any) {}, (value: any) => value];
            let keys = 0, values = 0;
            function key(): any { keys++; return 'length'; }
            function value() { values++; return 8; }
            function write(target: any) { 'use strict'; target[key()] = value(); }
            for (const target of targets) {
                const selected = { length: 7 };
                delete target.length;
                Object.setPrototypeOf(target, selected);
                write(target);
                const own = Object.getOwnPropertyDescriptor(target, 'length')!;
                console.log(Reflect.get(target, 'length'), Object.hasOwn(target, 'length'), selected.length,
                    own.writable, own.enumerable, own.configurable);
            }
            console.log(keys, values);
            """, "8 true 7 true true true\n8 true 7 true true true\n2 2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LengthWritesUseActualOwnAndSelectedInheritedDescriptors(
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
