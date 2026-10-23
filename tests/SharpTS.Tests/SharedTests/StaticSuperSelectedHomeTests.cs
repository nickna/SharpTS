using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StaticSuperSelectedHomeTests
{
    public const string IssueSource = """
        class First { static value = 1; }
        class Derived extends First { static read() { return super.value; } }
        const owner: any = Derived;
        console.log(owner.read());
        Object.setPrototypeOf(owner, { value: 2 });
        console.log(owner.read());
        Object.setPrototypeOf(owner, null);
        try { console.log(owner.read()); } catch (error) { console.log(error instanceof TypeError); }
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-selected-data-and-null", IssueSource, "1\n2\ntrue\n"];
        yield return ["borrowed-getter-keeps-home-and-actual-receiver", """
            const state = { oldGets: 0 }, seen: any[] = [];
            class First { static get value() { state.oldGets++; return -1; } }
            class Home extends First { static marker = 7; static read() { return super.value; } }
            class Borrow { static marker = 9; }
            const owner: any = Home, read: any = owner.read, alternate: any = { marker: 11 };
            Object.setPrototypeOf(owner, {
                get value() { seen.push(this); return (this as any).marker; }
            });
            Object.setPrototypeOf(Borrow, { value: 100 });
            console.log(owner.read(), read.call(Borrow), read.call(alternate));
            console.log(seen[0] === Home, seen[1] === Borrow, seen[2] === alternate, state.oldGets);
            """, "7 9 11\ntrue true true 0\n"];
        yield return ["escaped-arrows-keep-home-and-lexical-this", """
            class First { static get value(): any { return (this as any).marker + 1; } }
            class Home extends First {
                static marker = 7;
                static make() { return () => super.value; }
            }
            const owner: any = Home, make: any = owner.make, alternate: any = { marker: 9 };
            const ordinary: any = owner.make(), borrowed: any = make.call(alternate), seen: any[] = [];
            Object.setPrototypeOf(owner, {
                get value() { seen.push(this); return (this as any).marker * 2; }
            });
            console.log(ordinary(), borrowed(), ordinary.call({ marker: 100 }), borrowed.call(Home));
            console.log(seen[0] === Home, seen[1] === alternate, seen[2] === Home, seen[3] === alternate);
            """, "14 18 14 18\ntrue true true true\n"];
        yield return ["local-declaration-homes-remain-independent", """
            class First { static value = 1; }
            function make(): any {
                class Home extends First {
                    static read() { return super.value; }
                    static makeArrow() { return () => super.value; }
                }
                return Home;
            }
            const first: any = make(), second: any = make();
            const firstRead: any = first.read, secondRead: any = second.read, arrow: any = first.makeArrow();
            Object.setPrototypeOf(first, { value: 11 });
            Object.setPrototypeOf(second, { value: 22 });
            console.log(firstRead.call(second), secondRead.call(first), arrow());
            Object.setPrototypeOf(first, { value: 33 });
            console.log(firstRead.call(second), secondRead.call(first), arrow());
            """, "11 22 11\n33 22 33\n"];
        yield return ["class-expression-homes-preserve-method-values", """
            class First { static method(): any { return this; } }
            function make(): any {
                return class extends First {
                    static read() { return super.method; }
                    static invoke() { return super.method(); }
                };
            }
            const first: any = make(), second: any = make();
            const firstMethod: any = function(this: any) { return this; };
            const secondMethod: any = function(this: any) { return this; };
            Object.setPrototypeOf(first, { method: firstMethod });
            Object.setPrototypeOf(second, { method: secondMethod });
            const firstRead: any = first.read, secondRead: any = second.read, firstInvoke: any = first.invoke;
            console.log(first.read() === firstMethod, second.read() === secondMethod,
                firstRead.call(second) === firstMethod, secondRead.call(first) === secondMethod);
            console.log(first.invoke() === first, second.invoke() === second, firstInvoke.call(second) === second);
            """, "true true true true\ntrue true true\n"];
        yield return ["async-static-read-resolves-home-after-suspension", """
            class First { static value = 1; }
            class Home extends First {
                static marker = 7;
                static async read() {
                    await new Promise<void>(resolve => queueMicrotask(() => resolve()));
                    return super.value;
                }
            }
            const owner: any = Home, read: any = owner.read, alternate: any = { marker: 9 }, seen: any[] = [];
            async function run() {
                const pending = read.call(alternate);
                Object.setPrototypeOf(owner, {
                    get value() { seen.push(this); return (this as any).marker + 20; }
                });
                console.log(await pending, await owner.read());
                console.log(seen[0] === alternate, seen[1] === owner);
                const rejected = read.call(alternate);
                Object.setPrototypeOf(owner, null);
                try { await rejected; } catch (error) { console.log(error instanceof TypeError); }
            }
            run();
            """, "29 27\ntrue true\ntrue\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void StaticSuperReadsUseTheSelectedPrototypeOfTheirLexicalHome(
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
