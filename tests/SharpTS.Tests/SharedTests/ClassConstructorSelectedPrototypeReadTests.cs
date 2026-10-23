using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassConstructorSelectedPrototypeReadTests
{
    public const string IssueSource = """
        class Parent { static inherited = 3; static get masked() { return 4; } }
        class Child extends Parent {}
        const child: any = Child, custom: any = { inherited: 12 };
        Object.defineProperty(custom, 'masked', { get: undefined, configurable: true });
        Object.setPrototypeOf(child, custom);
        console.log(child.inherited, Reflect.get(child, 'inherited'), child.masked === undefined);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-derived-custom-data-and-mask", IssueSource, "12 12 true\n"];
        yield return ["typed-derived-selected-prototype", """
            class Parent { static inherited = 3; static get masked() { return 4; } }
            class Child extends Parent {}
            const custom: any = { inherited: 12 };
            Object.defineProperty(custom, 'masked', { get: undefined, configurable: true });
            Object.setPrototypeOf(Child, custom);
            console.log(Child.inherited, Child.masked === undefined);
            const child: any = Child;
            console.log(child.inherited, Reflect.get(child, 'inherited'), child.masked === undefined);
            """, "12 true\n12 12 true\n"];
        yield return ["original-local-base-getter", """
            function run() {
                class Owner { static marker = 7; }
                const owner: any = Owner, state = { gets: 0 };
                const custom: any = { get value() { state.gets++; return (this as any).marker; } };
                Object.setPrototypeOf(owner, custom);
                function key(): any { return 'value'; }
                console.log(owner.value, owner[key()], Reflect.get(owner, 'value'), state.gets);
            }
            run();
            """, "7 7 7 3\n"];
        yield return ["top-level-base-selected-getter-control", """
            class Owner { static marker = 7; }
            const owner: any = Owner, state = { gets: 0 };
            const custom: any = { get value() { state.gets++; return (this as any).marker; } };
            Object.setPrototypeOf(owner, custom);
            function key(): any { return 'value'; }
            console.log(owner.value, owner[key()], Reflect.get(owner, 'value'), state.gets);
            """, "7 7 7 3\n"];
        yield return ["own-descriptor-precedence", """
            class Parent {}
            class Child extends Parent {
                static data = 9;
                static marker = 7;
                static get value() { return this.marker; }
            }
            const child: any = Child, state = { gets: 0 };
            const custom: any = {
                get data() { state.gets++; return 12; },
                get value() { state.gets++; return 13; }
            };
            Object.setPrototypeOf(child, custom);
            function key(name: string): any { return name; }
            console.log(child.data, child[key('data')], Reflect.get(child, 'data'),
                child.value, child[key('value')], Reflect.get(child, 'value'), state.gets);
            """, "9 9 9 7 7 7 0\n"];
        yield return ["selected-getter-receivers", """
            class Parent { static get value() { return -1; } }
            class Child extends Parent { static marker = 7; }
            const child: any = Child, seen: any[] = [];
            const custom: any = {
                get value() { seen.push(this); return (this as any).marker; }
            };
            Object.setPrototypeOf(child, custom);
            function key(): any { return 'value'; }
            const alternate: any = { marker: 9 };
            console.log(child.value, child[key()], Reflect.get(child, 'value'),
                Reflect.get(child, 'value', alternate));
            console.log(seen[0] === child, seen[1] === child,
                seen[2] === child, seen[3] === alternate);
            """, "7 7 7 9\ntrue true true true\n"];
        yield return ["selected-method-identity", """
            function run() {
                class Parent { static method() { return -1; } }
                class Child extends Parent { static marker = 7; }
                const child: any = Child;
                const custom: any = { method(this: any) { return this.marker; } };
                const method = custom.method;
                Object.setPrototypeOf(child, custom);
                function key(): any { return 'method'; }
                console.log(child.method === method, child[key()] === method,
                    Reflect.get(child, 'method') === method);
                console.log(child.method());
            }
            run();
            """, "true true true\n7\n"];
        yield return ["missing-selected-property-hides-original-parent", """
            class Parent { static original = 3; }
            class Child extends Parent {}
            const child: any = Child;
            Object.setPrototypeOf(child, { selected: 12 });
            function key(): any { return 'original'; }
            console.log(child.original === undefined, child[key()] === undefined,
                Reflect.get(child, 'original') === undefined, child.missing === undefined);
            """, "true true true true\n"];
        yield return ["default-constructor-prototype-control", """
            class Parent {
                static inherited = 3;
                static marker = 5;
                static get value() { return this.marker; }
                static method() { return this.marker; }
            }
            class Child extends Parent { static marker = 7; }
            const child: any = Child, parentConstructor: any = Parent;
            function key(): any { return 'inherited'; }
            console.log(child.inherited, child[key()], Reflect.get(child, 'inherited'), child.value);
            console.log(child.method === parentConstructor.method, child.method());
            console.log(Object.getPrototypeOf(child) === Parent);
            """, "3 3 3 7\ntrue 7\ntrue\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ConstructorReadsUseSelectedPrototypeDescriptors(
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
