using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrivateSuspendedArgumentsTests
{
    public const string IssueSource = """
        class C {
            static async #asynchronous(value: any, ...rest: any[]) {
                await Promise.resolve(0);
                return arguments.length;
            }
            static *#generator(value: any, ...rest: any[]) { yield arguments.length; }
            static async *#asyncGenerator(value: any, ...rest: any[]) {
                await Promise.resolve(0);
                yield arguments.length;
            }
            static asynchronous() { return this.#asynchronous(1, 2); }
            static generator() { return this.#generator(1, 2); }
            static asyncGenerator() { return this.#asyncGenerator(1, 2); }
        }
        async function run() {
            console.log("async", await C.asynchronous());
            console.log("generator", C.generator().next().value);
            console.log("async-generator", (await C.asyncGenerator().next()).value);
        }
        run().catch((error: any) => console.log("unexpected", error.name, error.message));
        """;

    public const string CallsSource = """
        class C {
            async #asynchronous(value: any = 7, ...unused: any[]) { await Promise.resolve(0); return [this instanceof C, arguments.length, arguments[0], arguments[2]].join("|"); }
            *#generator(value: any = 7, ...unused: any[]) { yield [this instanceof C, arguments.length, arguments[0], arguments[2]].join("|"); }
            async *#asyncGenerator(value: any = 7, ...unused: any[]) { await Promise.resolve(0); yield [this instanceof C, arguments.length, arguments[0], arguments[2]].join("|"); }
            direct() { return [this.#asynchronous(1, 2, 3), this.#generator(4, 5, 6), this.#asyncGenerator(7, 8, 9),
                this.#asynchronous(), this.#generator(), this.#asyncGenerator()]; }
            extracted() { return [this.#asynchronous, this.#generator, this.#asyncGenerator]; }
        }
        function make() {
            return class Named {
                static async #asynchronous(...unused: any[]) { await Promise.resolve(0); return [this === Named, arguments.length, arguments[0], arguments[2]].join("|"); }
                static *#generator(...unused: any[]) { yield [this === Named, arguments.length, arguments[0], arguments[2]].join("|"); }
                static async *#asyncGenerator(...unused: any[]) { await Promise.resolve(0); yield [this === Named, arguments.length, arguments[0], arguments[2]].join("|"); }
                static direct() { return [this.#asynchronous(10, 11, 12), this.#generator(13, 14, 15), this.#asyncGenerator(16, 17, 18)]; }
                static extracted() { return [this.#asynchronous, this.#generator, this.#asyncGenerator]; }
            };
        }
        async function run() {
            const value = new C(), Expression: any = make();
            const direct: any = value.direct(), extracted: any = value.extracted();
            console.log(await direct[0], direct[1].next().value, (await direct[2].next()).value);
            console.log(await direct[3], direct[4].next().value, (await direct[5].next()).value);
            const reflected: any = Reflect.apply(extracted[2], value, [7, 8, 9]);
            console.log(await extracted[0].call(value, 1, 2, 3), extracted[1].apply(value, [4, 5, 6]).next().value,
                (await reflected.next()).value);
            const staticDirect: any = Expression.direct(), staticExtracted: any = Expression.extracted();
            console.log(await staticDirect[0], staticDirect[1].next().value, (await staticDirect[2].next()).value);
            const bound: any = staticExtracted[0].bind(Expression, 10).bind(null, 11);
            console.log(await bound(12), staticExtracted[1].call(Expression, 13, 14, 15).next().value,
                (await staticExtracted[2].apply(Expression, [16, 17, 18]).next()).value);
        }
        run().catch((error: any) => console.log("unexpected", error.name, error.message));
        """;

    public const string DefaultsRestSource = """
        class C {
            static async #asynchronous(value: any = 7, read: any = () => arguments[0], ...rest: any[]) {
                value = 9;
                await Promise.resolve(0);
                return [arguments.length, typeof arguments[0], value, read(), rest.join(","),
                    Object.prototype.toString.call(arguments), Array.isArray(arguments)].join("|");
            }
            static *#generator(value: any = 7, read: any = () => arguments[0], ...rest: any[]) {
                value = 9;
                yield [arguments.length, typeof arguments[0], value, read(), rest.join(","),
                    Object.prototype.toString.call(arguments), Array.isArray(arguments)].join("|");
            }
            static async *#asyncGenerator(value: any = 7, read: any = () => arguments[0], ...rest: any[]) {
                value = 9;
                await Promise.resolve(0);
                yield [arguments.length, typeof arguments[0], value, read(), rest.join(","),
                    Object.prototype.toString.call(arguments), Array.isArray(arguments)].join("|");
            }
            static async #defaultOnly(value: any = arguments.length) { await Promise.resolve(0); return value; }
            static *#defaultGenerator(value: any = arguments.length) { yield value; }
            static async *#defaultAsyncGenerator(value: any = arguments.length) { yield value; }
            static async #named(__this: any, ...rest: any[]) {
                await Promise.resolve(0);
                return [__this, arguments.length, arguments[0], rest.join(",")].join("|");
            }
            static inspect() { return [this.#asynchronous, this.#generator, this.#asyncGenerator]; }
            static defaults() { return [this.#defaultOnly(), this.#defaultGenerator(undefined), this.#defaultAsyncGenerator()]; }
            static named() { return this.#named("guest", ...[8, 9]); }
        }
        async function run() {
            const readers: any = C.inspect();
            const reflected: any = Reflect.apply(readers[2], C, [undefined, undefined, 8, 9]);
            console.log(await readers[0].call(C), readers[1].apply(C, [undefined]).next().value,
                (await reflected.next()).value);
            console.log(await readers[0].call(C, 5, undefined, 8), readers[1].call(C, 5, undefined, 8).next().value,
                (await readers[2].call(C, 5, undefined, 8).next()).value);
            const defaults: any = C.defaults();
            console.log(await defaults[0], defaults[1].next().value, (await defaults[2].next()).value);
            console.log(await C.named());
        }
        run().catch((error: any) => console.log("unexpected", error.name, error.message));
        """;

    public const string PendingSnapshotsSource = """
        let release: any;
        const gate = new Promise<any>((resolve: any) => { release = resolve; });
        let releaseGenerators: any;
        const generatorGate = new Promise<any>((resolve: any) => { releaseGenerators = resolve; });
        class C {
            static async #asynchronous(wait: any, label: any) {
                const read = () => arguments;
                await wait;
                arguments[1] += "!";
                return read;
            }
            static *#generator(label: any) {
                const read = () => arguments;
                yield read;
                arguments[0] += "!";
                yield read;
            }
            static async *#asyncGenerator(wait: any, label: any) {
                const read = () => arguments;
                await wait;
                yield read;
                await Promise.resolve(0);
                arguments[1] += "!";
                yield read;
            }
            static asynchronous(wait: any, label: any) { return this.#asynchronous(wait, label); }
            static generator(label: any) { return this.#generator(label); }
            static asyncGenerator(wait: any, label: any) { return this.#asyncGenerator(wait, label); }
        }
        function intervening(...unused: any[]) { return arguments.length; }
        async function run() {
            const left = C.asynchronous(gate, "left"), right = C.asynchronous(gate, "right");
            console.log(intervening(1, 2, 3));
            release(0);
            const leftRead: any = await left, rightRead: any = await right;
            console.log(leftRead()[1], rightRead()[1], leftRead().length, leftRead() !== rightRead());
            const first = C.generator("first"), second = C.generator("second");
            const firstRead: any = first.next().value, secondRead: any = second.next().value;
            console.log(firstRead()[0], secondRead()[0], firstRead() !== secondRead());
            const firstAgain: any = first.next().value, secondAgain: any = second.next().value;
            console.log(firstAgain()[0], secondAgain()[0], firstAgain() === firstRead(), secondAgain() === secondRead());
            const one = C.asyncGenerator(generatorGate, "one"), two = C.asyncGenerator(generatorGate, "two");
            const nextOne = one.next(), nextTwo = two.next();
            releaseGenerators(0);
            const oneRead: any = (await nextOne).value, twoRead: any = (await nextTwo).value;
            console.log(oneRead()[1], twoRead()[1], oneRead().length, oneRead() !== twoRead());
            const oneAgain: any = (await one.next()).value, twoAgain: any = (await two.next()).value;
            console.log(oneAgain()[1], twoAgain()[1], oneAgain() === oneRead(), twoAgain() === twoRead());
        }
        run().catch((error: any) => console.log("unexpected", error.name, error.message));
        """;

    public static string SnapshotsSource => PendingSnapshotsSource.Replace(
        "await wait;", "await Promise.resolve(0);", StringComparison.Ordinal);

    public const string ExpressionControlsSource = """
        const sync: any = function(count: any = arguments.length) { return count; };
        const asynchronous: any = async function(count: any = arguments.length) { await Promise.resolve(0); return count; };
        const generator: any = function*(count: any = arguments.length) { yield count; };
        const asyncGenerator: any = async function*(count: any = arguments.length) { await Promise.resolve(0); yield count; };
        async function declaration(...unused: any[]) {
            const arrow = async () => arguments[0];
            const own: any = async function(...inner: any[]) { await Promise.resolve(0); return arguments[0]; };
            await Promise.resolve(0);
            return [await arrow(), await own("inner")].join("|");
        }
        async function captured() {
            const prefix = "p";
            const own: any = async function(value: any) {
                await 0;
                return [prefix, arguments.length, arguments[0]].join("|");
            };
            return await own("a");
        }
        async function run() {
            console.log(sync(undefined), await asynchronous(undefined), generator(undefined).next().value,
                (await asyncGenerator(undefined).next()).value);
            console.log(await declaration("outer"));
            console.log(await captured());
        }
        run().catch((error: any) => console.log("unexpected", error.name, error.message));
        """;

    public const string OrdinaryOwnPendingSource = """
        let release: any;
        const gate: Promise<number> = new Promise<number>(resolve => { release = resolve; });

        async function outer(...unused: any[]): Promise<string> {
            const lexical = () => arguments[0];
            const own: any = async function(...inner: any[]): Promise<string> {
                await gate;
                const read = () => arguments[0];
                return [arguments[0], read(), arguments.length].join("|");
            };
            const pending: Promise<string> = own("inner", "tail");
            const before = lexical();
            const value = await pending;
            return before + "|" + value;
        }

        const task = outer("outer");
        console.log("waiting");
        release(0);
        task.then((value: string) => console.log(value),
            (error: any) => console.log("unexpected", error.name, error.message));
        """ + "\n";

    public const string LexicalArrowPendingSource = """
        let release: any;
        const gate: Promise<number> = new Promise<number>(resolve => { release = resolve; });

        async function owner(...values: any[]): Promise<string> {
            const read = async (): Promise<string> => {
                await gate;
                return [arguments[0], arguments.length].join("|");
            };
            return await read();
        }

        const task = owner("lexical", "tail");
        console.log("waiting");
        release(0);
        task.then((value: string) => console.log(value),
            (error: any) => console.log("unexpected", error.name, error.message));
        """ + "\n";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue", IssueSource, "async 2\ngenerator 2\nasync-generator 2\n"];
        yield return ["calls", CallsSource, "true|3|1|3 true|3|4|6 true|3|7|9\ntrue|0|| true|0|| true|0||\ntrue|3|1|3 true|3|4|6 true|3|7|9\ntrue|3|10|12 true|3|13|15 true|3|16|18\ntrue|3|10|12 true|3|13|15 true|3|16|18\n"];
        yield return ["defaults-rest", DefaultsRestSource, "0|undefined|9|||[object Arguments]|false 1|undefined|9|||[object Arguments]|false 4|undefined|9||8,9|[object Arguments]|false\n3|number|9|5|8|[object Arguments]|false 3|number|9|5|8|[object Arguments]|false 3|number|9|5|8|[object Arguments]|false\n0 1 0\nguest|3|guest|8,9\n"];
        yield return ["snapshots", SnapshotsSource, "3\nleft! right! 2 true\nfirst second true\nfirst! second! true true\none two 2 true\none! two! true true\n"];
        yield return ["pending-snapshots", PendingSnapshotsSource, "3\nleft! right! 2 true\nfirst second true\nfirst! second! true true\none two 2 true\none! two! true true\n"];
        yield return ["expression-controls", ExpressionControlsSource, "1 1 1 1\nouter|inner\np|1|a\n"];
        yield return ["ordinary-own-pending", OrdinaryOwnPendingSource, "waiting\nouter|inner|inner|2\n"];
        yield return ["lexical-arrow-pending", LexicalArrowPendingSource, "waiting\nlexical|2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
        .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SuspendedCallsRetainTheirOwnRawArguments(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
