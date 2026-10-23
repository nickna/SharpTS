using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncGeneratorSyncDelegatedThrowTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            const events: string[] = [];
            const token = 'token';
            function* inner(): any {
                try { yield 1; }
                catch (error) {
                    events.push('caught:' + String(error === token));
                    yield 2;
                }
                events.push('inner-complete');
                return 9;
            }
            async function* outer(): any {
                try { return yield* inner(); }
                finally { events.push('outer'); }
            }
            async function run() {
                const iterator: any = outer();
                const first: any = await iterator.next();
                console.log('first', first.value, first.done);
                const thrown: any = await iterator.throw(token);
                console.log('throw', thrown.value, thrown.done, events.join(','));
                const next: any = await iterator.next();
                console.log('next', next.value, next.done, events.join(','));
                const closed: any = await iterator.next();
                console.log('closed', closed.done, events.join(','));
            }
            const task = run().catch((error: any) => console.log('unexpected', error === token, events.join(',')));
            """.ReplaceLineEndings("\n") + "\n", "first 1 false\nthrow 2 false caught:true\nnext 9 true caught:true,inner-complete,outer\nclosed true caught:true,inner-complete,outer\n"];
        yield return ["done-and-uncaught-delegate-throw", """
            const token = 'token';
            const returnEvents: string[] = [];
            const catchEvents: string[] = [];
            function* returningInner(): any {
                try { yield 1; }
                catch (error) {
                    returnEvents.push('inner-caught:' + String(error === token));
                    return 7;
                }
            }
            async function* continuingOuter(): any {
                try {
                    const value: any = yield* returningInner();
                    returnEvents.push('after:' + String(value));
                    yield value + 1;
                    return 9;
                } finally { returnEvents.push('outer-finally'); }
            }
            function* throwingInner(): any {
                try { yield 2; }
                finally { catchEvents.push('delegate-finally'); }
            }
            async function* catchingOuter(): any {
                try { yield* throwingInner(); catchEvents.push('unreachable'); }
                catch (error) {
                    catchEvents.push('outer-caught:' + String(error === token));
                    yield 3;
                    catchEvents.push('resumed');
                } finally { catchEvents.push('catching-finally'); }
                return 4;
            }
            async function run() {
                const returning: any = continuingOuter();
                const first: any = await returning.next();
                console.log('return-first', first.value, first.done);
                const completedThrow: any = await returning.throw(token);
                console.log('return-throw', completedThrow.value, completedThrow.done, returnEvents.join(','));
                const returned: any = await returning.next();
                console.log('return-next', returned.value, returned.done, returnEvents.join(','));
                const catching: any = catchingOuter();
                const catchFirst: any = await catching.next();
                console.log('catch-first', catchFirst.value, catchFirst.done);
                const uncaught: any = await catching.throw(token);
                console.log('catch-throw', uncaught.value, uncaught.done, catchEvents.join(','));
                const next: any = await catching.next();
                console.log('catch-next', next.value, next.done, catchEvents.join(','));
                const closed: any = await catching.next();
                console.log('catch-closed', closed.done, catchEvents.join(','));
            }
            const task = run().catch((error: any) => console.log('unexpected', error === token, returnEvents.join(','), catchEvents.join(',')));
            """.ReplaceLineEndings("\n") + "\n", "return-first 1 false\nreturn-throw 8 false inner-caught:true,after:7\nreturn-next 9 true inner-caught:true,after:7,outer-finally\ncatch-first 2 false\ncatch-throw 3 false delegate-finally,outer-caught:true\ncatch-next 4 true delegate-finally,outer-caught:true,resumed,catching-finally\ncatch-closed true delegate-finally,outer-caught:true,resumed,catching-finally\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SynchronousDelegatesReceiveThrowsAndResumeOuterCatch(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        AssertStrictSource(source);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "async-generator-sync-delegated-throw-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
