using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorPrototypeGraphTests
{
    public const string IssueSource = """
        class C { *g() {} static *sg() {} async *ag() {} static async *sag() {} }
        const instance: any = new C(), constructor: any = C;
        for (const f of [instance.g, constructor.sg, instance.ag, constructor.sag]) {
            console.log(Reflect.ownKeys(f.prototype).length,
                Object.getPrototypeOf(f()) === f.prototype,
                Object.getPrototypeOf(f).prototype === Object.getPrototypeOf(f.prototype),
                Object.getPrototypeOf(f.prototype) === Object.prototype);
        }
        """;

    public const string LivePrototypeSource = """
        function* declared(): Generator<any, any, any> { yield 1; }
        async function* asynchronous(): AsyncGenerator<any, any, any> { yield 2; }
        function* another() {}
        async function* anotherAsync() {}
        const sync: any = declared, asyncValue: any = asynchronous;
        const directBefore: any[] = [declared(), asynchronous()];
        console.log(Object.getPrototypeOf(sync) === Object.getPrototypeOf(another),
            Object.getPrototypeOf(asyncValue) === Object.getPrototypeOf(anotherAsync),
            sync.prototype !== (another as any).prototype,
            asyncValue.prototype !== (anotherAsync as any).prototype,
            Object.getPrototypeOf(sync) !== Object.getPrototypeOf(asyncValue));
        console.log(Object.getPrototypeOf(directBefore[0]) === sync.prototype,
            Object.getPrototypeOf(directBefore[1]) === asyncValue.prototype);
        for (const fn of [sync, asyncValue]) {
            const original: any = fn.prototype, intrinsic: any = Object.getPrototypeOf(original);
            const earlier: any = fn();
            console.log(Reflect.ownKeys(original).length, Object.getPrototypeOf(earlier) === original,
                earlier.next === intrinsic.next);
            const custom: any = Object.create(null); custom.marker = "object";
            fn.prototype = custom;
            const later: any = fn();
            console.log(Object.getPrototypeOf(later) === custom, later.marker,
                later.next === undefined && later.return === undefined && later.throw === undefined,
                earlier.next === intrinsic.next);
            const callable: any = function replacement() {};
            fn.prototype = callable;
            const afterCallable: any = fn();
            console.log(Object.getPrototypeOf(afterCallable) === callable, afterCallable.next === undefined);
            let fallback = true;
            for (const primitive of [null, undefined, 7, "text", true, 1n, Symbol("primitive")]) {
                fn.prototype = primitive;
                fallback = fallback && Object.getPrototypeOf(fn()) === intrinsic
                    && Object.hasOwn(fn, "prototype") && fn.prototype === primitive;
            }
            console.log(fallback, Object.getPrototypeOf(earlier) === original,
                Object.getPrototypeOf(later) === custom);
        }
        const syncReplacement = {}, asyncReplacement = {};
        sync.prototype = syncReplacement; asyncValue.prototype = asyncReplacement;
        console.log(Object.getPrototypeOf(declared()) === syncReplacement,
            Object.getPrototypeOf(asynchronous()) === asyncReplacement);
        """;

    public const string IntrinsicPropertiesSource = """
        function* declared(): Generator<any, any, any> { yield 1; }
        async function* asynchronous(): AsyncGenerator<any, any, any> { yield 2; }
        function inspect(fn: any) {
            const original: any = fn.prototype, intrinsic: any = Object.getPrototypeOf(original);
            const next: any = intrinsic.next, earlier: any = fn();
            const descriptor: any = Object.getOwnPropertyDescriptor(intrinsic, "next");
            console.log(earlier.next === next, earlier.return === intrinsic.return,
                earlier.throw === intrinsic.throw, next.name, next.length,
                descriptor.writable, descriptor.enumerable, descriptor.configurable);
            const state: any = { iterator: null, gets: 0, valid: true };
            const custom: any = Object.create(intrinsic);
            Object.defineProperty(custom, "next", {
                configurable: true,
                get() {
                    state.gets++; state.valid = state.valid && this === state.iterator;
                    return next;
                }
            });
            fn.prototype = custom;
            state.iterator = fn();
            console.log(state.iterator.next === next, state.gets, state.valid);
            delete custom.next;
            const replacement: any = function() {};
            Object.defineProperty(intrinsic, "next", { value: replacement });
            console.log(state.iterator.next === replacement, earlier.next === replacement);
            delete intrinsic.next;
            console.log(state.iterator.next === undefined, earlier.next === undefined);
            Object.defineProperty(intrinsic, "next", descriptor);
            console.log(state.iterator.next === next, earlier.next === next);
            Object.defineProperty(earlier, "next", { value: replacement, configurable: true });
            console.log(earlier.next === replacement, state.iterator.next === next);
            delete earlier.next;
            console.log(earlier.next === next);
            const functionIntrinsic: any = Object.getPrototypeOf(fn);
            Object.defineProperty(functionIntrinsic, "observer", {
                configurable: true, get() { return this; }
            });
            console.log(fn.observer === fn);
            delete functionIntrinsic.observer;
            console.log(fn.observer === undefined);
        }
        inspect(declared); inspect(asynchronous);
        """;

    public const string IntrinsicInvocationSource = """
        function* declared(): Generator<any, any, any> {
            const value = yield 1;
            yield value;
        }
        async function* asynchronous(): AsyncGenerator<any, any, any> {
            const value = yield 1;
            yield value;
        }
        function rejectsReceiver(method: any) {
            try { method.call({}); return false; }
            catch (error) { return error instanceof TypeError; }
        }
        const sync: any = declared, syncIntrinsic: any = Object.getPrototypeOf(sync.prototype);
        const syncNext: any = syncIntrinsic.next, syncReturn: any = syncIntrinsic.return,
            syncThrow: any = syncIntrinsic.throw;
        const syncIterator: any = declared();
        const first = syncNext.call(syncIterator), resumed = syncNext.call(syncIterator, 7),
            completed = syncNext.call(syncIterator);
        const syncOmittedIterator: any = declared();
        syncNext.call(syncOmittedIterator);
        const omitted = syncNext.call(syncOmittedIterator);
        syncNext.call(syncOmittedIterator);
        console.log(first.value, first.done, resumed.value, resumed.done,
            completed.value === undefined, completed.done, omitted.value === undefined && omitted.done === false);
        const syncReturning: any = declared();
        syncNext.call(syncReturning);
        const closedResult = syncReturn.call(syncReturning), afterClose = syncNext.call(syncReturning);
        console.log(closedResult.value === undefined, closedResult.done, afterClose.value === undefined, afterClose.done);
        try { syncThrow.call(syncIterator); console.log("accepted"); }
        catch (error) { console.log(error === undefined); }
        console.log(rejectsReceiver(syncNext), rejectsReceiver(syncReturn), rejectsReceiver(syncThrow));

        async function rejectsAsyncReceiver(method: any) {
            try { await method.call({}); return false; }
            catch (error) { return error instanceof TypeError; }
        }
        async function run() {
            const target: any = asynchronous, intrinsic: any = Object.getPrototypeOf(target.prototype);
            const next: any = intrinsic.next, returnMethod: any = intrinsic.return, throwMethod: any = intrinsic.throw;
            const iterator: any = asynchronous();
            const first = await next.call(iterator);
            const resumed = await next.call(iterator, 9);
            const completed = await next.call(iterator);
            const omittedIterator: any = asynchronous();
            await next.call(omittedIterator);
            const omitted = await next.call(omittedIterator);
            await next.call(omittedIterator);
            console.log(first.value, first.done, resumed.value, resumed.done,
                completed.value === undefined, completed.done, omitted.value === undefined && omitted.done === false);
            const returning: any = asynchronous();
            await next.call(returning);
            const closedResult = await returnMethod.call(returning);
            const afterClose = await next.call(returning);
            console.log(closedResult.value === undefined, closedResult.done, afterClose.value === undefined, afterClose.done);
            try { await throwMethod.call(iterator); console.log("accepted"); }
            catch (error) { console.log(error === undefined); }
            const invalidNext = await rejectsAsyncReceiver(next);
            const invalidReturn = await rejectsAsyncReceiver(returnMethod);
            const invalidThrow = await rejectsAsyncReceiver(throwMethod);
            console.log(invalidNext, invalidReturn, invalidThrow);
        }
        run();
        """;

    public const string EmptyConstructorSource = """
        function* declared() {}
        async function* asynchronous() {}
        const sync: any = declared, asyncValue: any = asynchronous, marker = {};
        const SyncConstructor: any = Object.getPrototypeOf(sync).constructor;
        const generated: any = SyncConstructor(), other: any = SyncConstructor();
        console.log(generated.name, generated.length, generated instanceof SyncConstructor,
            Object.getPrototypeOf(generated) === SyncConstructor.prototype, generated !== other);
        const intrinsic: any = Object.getPrototypeOf(sync.prototype), iterator: any = generated();
        console.log(Object.getPrototypeOf(generated.prototype) === intrinsic, Reflect.ownKeys(generated.prototype).length);
        const result = iterator.next();
        console.log(Object.getPrototypeOf(iterator) === generated.prototype,
            result.value === undefined, result.done, iterator.next === intrinsic.next);
        const returned = generated().return(7);
        console.log(returned.value === 7, returned.done);
        try { generated().throw(marker); } catch (error) { console.log(error === marker); }
        try { new generated(); } catch (error) { console.log(error instanceof TypeError); }
        async function run() {
            const AsyncConstructor: any = Object.getPrototypeOf(asyncValue).constructor;
            const generated: any = AsyncConstructor(), other: any = AsyncConstructor();
            console.log(generated.name, generated.length, generated instanceof AsyncConstructor,
                Object.getPrototypeOf(generated) === AsyncConstructor.prototype, generated !== other);
            const intrinsic: any = Object.getPrototypeOf(asyncValue.prototype), iterator: any = generated();
            console.log(Object.getPrototypeOf(generated.prototype) === intrinsic, Reflect.ownKeys(generated.prototype).length);
            const result = await iterator.next();
            console.log(Object.getPrototypeOf(iterator) === generated.prototype,
                result.value === undefined, result.done, iterator.next === intrinsic.next);
            const returned = await generated().return(9);
            console.log(returned.value === 9, returned.done);
            try { await generated().throw(marker); } catch (error) { console.log(error === marker); }
            try { new generated(); } catch (error) { console.log(error instanceof TypeError); }
        }
        run();
        """;

    public const string EmptyAsyncReturnSource = """
        async function* asynchronous() {}
        const target: any = asynchronous, marker = {};
        const Constructor: any = Object.getPrototypeOf(target).constructor, generated: any = Constructor();
        async function run() {
            let resolve: any;
            const pending = new Promise<any>(complete => { resolve = complete; });
            const iterator: any = generated(), returned = iterator.return(pending), next = iterator.next();
            const state = { returned: false, next: false };
            returned.then(() => { state.returned = true; });
            next.then(() => { state.next = true; });
            await Promise.resolve();
            console.log(state.returned, state.next);
            resolve(11);
            const first = await returned, second = await next;
            console.log(first.value === 11, first.done, second.value === undefined, second.done);
            const thenable: any = { then(complete: any) { complete(12); } };
            const adopted = await generated().return(thenable);
            console.log(adopted.value === 12, adopted.done);
            const rejectedIterator: any = generated();
            try { await rejectedIterator.return(Promise.reject(marker)); }
            catch (error) { console.log(error === marker); }
            const after = await rejectedIterator.next();
            console.log(after.value === undefined, after.done);
            const poisoned: any = { get then() { throw marker; } };
            const rejection = generated().return(poisoned);
            console.log(typeof rejection.then);
            try { await rejection; } catch (error) { console.log(error === marker); }
            const promise = Promise.resolve(13);
            try { await generated().throw(promise); } catch (error) { console.log(error === promise); }
            const reentrant: any = generated(), progress = { next: false };
            let release: any, nested: any;
            const delayed: any = { get then() {
                nested = reentrant.next(); nested.then(() => { progress.next = true; });
                return function(complete: any) { release = complete; };
            } };
            const waiting = reentrant.return(delayed);
            await Promise.resolve();
            console.log(progress.next);
            release(14);
            const resolved = await waiting, following = await nested;
            console.log(resolved.value === 14, resolved.done, following.value === undefined, following.done);
        }
        run();
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["intrinsic-invocation", IntrinsicInvocationSource,
            "1 false 7 false true true true\ntrue true true true\ntrue\ntrue true true\n"
                + "1 false 9 false true true true\ntrue true true true\ntrue\ntrue true true\n"];
        yield return ["issue", IssueSource, string.Concat(Enumerable.Repeat("0 true true false\n", 4))];
        yield return ["live-prototype", LivePrototypeSource,
            "true true true true true\ntrue true\n" + string.Concat(Enumerable.Repeat(
                "0 true true\ntrue object true true\ntrue true\ntrue true true\n", 2)) + "true true\n"];
        yield return ["intrinsic-properties", IntrinsicPropertiesSource,
            string.Concat(Enumerable.Repeat("true true true next 1 true false true\ntrue 1 true\n"
                + "true true\ntrue true\ntrue true\ntrue true\ntrue\ntrue\ntrue\n", 2))];
        yield return ["empty-constructor", EmptyConstructorSource,
            string.Concat(Enumerable.Repeat("anonymous 0 true true true\ntrue 0\ntrue true true true\ntrue true\ntrue\ntrue\n", 2))];
        yield return ["empty-async-return", EmptyAsyncReturnSource,
            "false false\ntrue true true true\ntrue true\ntrue\ntrue true\nfunction\ntrue\ntrue\nfalse\ntrue true true true\n"];
    }

    private const string InvalidSyncReceiverStatement = """
        console.log(rejectsReceiver(syncNext), rejectsReceiver(syncReturn), rejectsReceiver(syncThrow));
        """;

    private const string InvalidAsyncReceiverStatements = """
            const invalidNext = await rejectsAsyncReceiver(next);
            const invalidReturn = await rejectsAsyncReceiver(returnMethod);
            const invalidThrow = await rejectsAsyncReceiver(throwMethod);
            console.log(invalidNext, invalidReturn, invalidThrow);
        """;

    // Intrinsic invalid-receiver brands (#2328) remain compiled regressions.
    public static IEnumerable<object[]> SharedCases()
    {
        foreach (var row in Cases().Where(row => (string)row[0] is "issue" or "live-prototype" or "empty-constructor" or "intrinsic-properties"))
            yield return row;
        yield return ["intrinsic-invocation-shared",
            IntrinsicInvocationSource.Replace(InvalidSyncReceiverStatement, "")
                .Replace(InvalidAsyncReceiverStatements, ""),
            "1 false 7 false true true true\ntrue true true true\ntrue\n"
                + "1 false 9 false true true true\ntrue true true true\ntrue\n"];
    }

    public static IEnumerable<object[]> CompiledOnlyCases() => Cases()
        // Keep the established compiled assertion site for intrinsic receiver brands
        // and empty async constructor return adoption.
        .Where(row => (string)row[0] is "intrinsic-invocation" or "empty-async-return");

    public static IEnumerable<object[]> ModeCases() => SharedCases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }))
        .Concat(Cases().Where(row => (string)row[0] == "empty-async-return")
            .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Interpreted }));

    [Theory, MemberData(nameof(ModeCases))]
    public void GeneratorIntrinsicsAndLivePrototypeReadsFollowTheOrdinaryObjectGraph(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }

    [Theory, MemberData(nameof(CompiledOnlyCases))]
    public void CompiledGeneratorIteratorsRetainOwnDescriptorsAndGuestReceiverErrors(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunCompiled(source));
    }
}
