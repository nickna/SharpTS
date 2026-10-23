using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class DestructuredParameterDefaultScopeTests
{
    public const string IssueSource = """
        function run([value]: any[], next = value) { console.log(value, next); }
        run([1]);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-earlier-array-binding", IssueSource, "1 1\n"];
        yield return ["ordinary-identifier-default-control", """
            function run(first: number = 7, next: number = first) { console.log(first, next); }
            run();
            run(9);
            """, "7 7\n9 9\n"];
        yield return ["object-alias-and-nested-default-order", """
            const trace: string[] = [];
            function mark(name: string, value: number): number { trace.push(name); return value; }
            function run({ source: value = mark("value", 2), nested: { count = mark("count", 3) } }: any = { nested: {} },
                next: any = (trace.push("next"), value + count)) {
                console.log(value, count, next, trace.join(","));
            }
            run();
            function within([first = 1, next = first + 1]: number[] = [], later = next) {
                console.log(first, next, later);
            }
            within();
            """, "2 3 5 value,count,next\n1 2 2\n"];
        yield return ["supplied-later-argument-and-inferred-pattern-default", """
            const state = { defaults: 0 };
            function run([value]: number[], next: number = (state.defaults++, value)) {
                console.log(value, next, state.defaults);
            }
            run([7], 20);
            run([8]);
            function inferred([value]: number[], { next = value } = {}) { console.log(value, next); }
            inferred([1]);
            """, "7 20 0\n8 8 1\n1 1\n"];
        yield return ["array-hole-and-rest-bindings", """
            function run([first, , ...rest]: number[], next = first + rest.length) {
                console.log(first, rest.join(","), next);
            }
            run([1, 99, 2, 3]);
            """, "1 2,3 3\n"];
        yield return ["synchronous-callable-forms", """
            const expression = function([value]: any[], next = value) { return next; };
            const arrow = ([value]: any[], next = value) => next;
            const object = { run({ source: value }: any, next = value) { return next; } };
            class Owner {
                run([value]: any[], next = value) { return next; }
                static run({ source: value }: any, next = value) { return next; }
            }
            console.log(expression([1]), arrow([2]), object.run({ source: 3 }),
                new Owner().run([4]), Owner.run({ source: 5 }));
            """, "1 2 3 4 5\n"];
        yield return ["captured-pattern-binding-and-body-var-separation", """
            function shared([value]: any[] = [1], read: any = () => value, next: any = (value = 2)) {
                console.log(value, next, read());
                value = 3;
                console.log(read());
            }
            shared();
            function split([value]: any[] = [1], read: any = () => value, later: any = (value = 4)) {
                var value: any;
                console.log(value, read());
                value = 5;
                console.log(value, read());
            }
            split();
            """, "2 2 2\n3\n4 4\n5 4\n"];
        yield return ["pattern-prologue-effects-run-once", """
            const trace: string[] = [];
            const provided = { get source() { trace.push("get"); return undefined; } };
            function mark(name: string, value: number): number { trace.push(name); return value; }
            function run({ source: value = mark("binding", 2) }: any = (trace.push("whole"), provided),
                next: any = (trace.push("later"), value)) {
                console.log(value, next, trace.join(","));
                trace.length = 0;
            }
            run();
            run(provided, 9);
            """, "2 2 whole,get,binding,later\n2 9 get,binding\n"];
        yield return ["constructor-live-parent-and-private-method", """
            class Ordinary {
                value: any;
                read: any;
                constructor([value]: any[] = [1], read: any = () => value, next = value) {
                    this.value = next;
                    this.read = read;
                    value = 4;
                }
            }
            class First { from = "first"; }
            class Alternate { from = "alternate"; }
            class Derived extends First {
                value: any;
                constructor([value]: any[] = [2], next = value) { super(); this.value = next; }
            }
            Object.setPrototypeOf(Derived, Alternate);
            class Owner {
                #read([value]: any[], next = value) { return next; }
                read() { return this.#read([3]); }
            }
            const ordinary = new Ordinary();
            const instance: any = new Derived();
            console.log(ordinary.value, ordinary.read(), instance.from, instance.value, new Owner().read());
            """, "1 4 alternate 2 3\n"];
    }

    public static IEnumerable<object[]> SuspendedCases()
    {
        yield return ["async-declaration-binding-before-default-and-await", """
            const state = { binding: 0, later: 0 };
            async function run([value = (state.binding++, 1)]: any[], next = (state.later++, value)) {
                console.log("before", value, next, state.binding, state.later);
                await Promise.resolve();
                console.log("after", value, next, state.binding, state.later);
            }
            run([]);
            """, "before 1 1 1 1\nafter 1 1 1 1\n"];
        yield return ["async-arrow-binding-before-default-and-await", """
            const state = { binding: 0, later: 0 };
            const run = async ([value = (state.binding++, 1)]: any[], next = (state.later++, value)) => {
                console.log("before", value, next, state.binding, state.later);
                await Promise.resolve();
                console.log("after", value, next, state.binding, state.later);
            };
            run([]);
            """, "before 1 1 1 1\nafter 1 1 1 1\n"];
        yield return ["generator-declaration-binding-before-default-and-yield", """
            const state = { binding: 0, later: 0 };
            function* run([value = (state.binding++, 1)]: any[], next = (state.later++, value)) {
                console.log("before", value, next, state.binding, state.later);
                yield next;
                console.log("after", value, next, state.binding, state.later);
            }
            const iterator = run([]);
            iterator.next();
            iterator.next();
            """, "before 1 1 1 1\nafter 1 1 1 1\n"];
        yield return ["async-generator-binding-before-default-await-and-yield", """
            const state = { binding: 0, later: 0 };
            async function* run([value = (state.binding++, 1)]: any[], next = (state.later++, value)) {
                console.log("before", value, next, state.binding, state.later);
                await Promise.resolve();
                yield next;
                console.log("after", value, next, state.binding, state.later);
            }
            const iterator = run([]);
            iterator.next().then(() => iterator.next());
            """, "before 1 1 1 1\nafter 1 1 1 1\n"];
        yield return ["generator-expression-binding-before-default-and-yield", """
            const state = { binding: 0, later: 0 };
            const run = function* ([value = (state.binding++, 1)]: any[], next = (state.later++, value)) {
                console.log("before", value, next, state.binding, state.later);
                yield next;
                console.log("after", value, next, state.binding, state.later);
            };
            const iterator = run([]);
            iterator.next();
            iterator.next();
            """, "before 1 1 1 1\nafter 1 1 1 1\n"];
        yield return ["generator-pattern-default-captures-stay-live-across-yield", """
            function* declaration([value]: any[], read: any = () => value, next = value) {
                yield next + ":" + read();
                value = 2;
                yield next + ":" + read();
            }
            const declared = declaration([1]);
            console.log("declaration", declared.next().value, declared.next().value);
            const expression = function* ([value]: any[], read: any = () => value, next = value) {
                yield next + ":" + read();
                value = 2;
                yield next + ":" + read();
            };
            const expressed = expression([1]);
            console.log("expression", expressed.next().value, expressed.next().value);
            async function* asynchronous([value]: any[], read: any = () => value, next = value) {
                yield next + ":" + read();
                value = 2;
                yield next + ":" + read();
            }
            const asyncIterator = asynchronous([1]);
            asyncIterator.next().then(first => {
                console.log("async first", first.value);
                return asyncIterator.next();
            }).then(second => console.log("async second", second.value));
            """, "declaration 1:1 1:2\nexpression 1:1 1:2\nasync first 1:1\nasync second 1:2\n"];
    }

    public static IEnumerable<object[]> StandaloneCases() => Cases().Concat(SuspendedCases()).Concat(
        ParameterDefaultBodyScopeTests.Cases()
            .Where(row => (string)row[0] == "destructured-defaults-copy-and-order"));

    public static IEnumerable<object[]> ModeCases() => Cases().Concat(SuspendedCases()).SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LaterDefaultsCanReadEarlierPatternBindings(
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
