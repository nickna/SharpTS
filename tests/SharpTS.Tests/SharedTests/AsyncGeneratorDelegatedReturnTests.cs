using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class AsyncGeneratorDelegatedReturnTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["delegated-return", """
            const events: string[] = [];
            async function* inner(): any {
                try { yield 1; yield 2; }
                finally { events.push('inner'); }
            }
            async function* outer(): any {
                try { yield* inner(); }
                finally { events.push('outer'); }
            }
            async function run() {
                const iterator: any = outer();
                const first: any = await iterator.next();
                console.log(first.value, first.done);
                const result: any = await iterator.return(7);
                console.log(result.value, result.done, events.join(','));
                console.log((await iterator.next()).done, events.join(','));
            }
            run().catch((error: any) => console.log('unexpected', error && error.name));
            """, "1 false\n7 true inner,outer\ntrue inner,outer\n"];
        yield return ["bounded-delegated-return-protocol-v2", """
            const yieldingEvents: string[] = [];
            async function* yieldingInner(): any {
                try { yield 1; }
                finally {
                    yieldingEvents.push('inner-before');
                    yield 3;
                    yieldingEvents.push('inner-after');
                }
            }
            async function* yieldingOuter(): any {
                try {
                    const result: any = yield* yieldingInner();
                    yieldingEvents.push('after:' + String(result));
                    return 11;
                } finally { yieldingEvents.push('outer'); }
            }
            const overrideEvents: string[] = [];
            async function* overrideInner(): any {
                try { yield 1; }
                finally {
                    await new Promise<number>(resolve => setTimeout(() => resolve(0), 1));
                    overrideEvents.push('inner');
                    return 9;
                }
            }
            async function* overrideOuter(): any {
                try { yield* overrideInner(); overrideEvents.push('after'); }
                finally { overrideEvents.push('outer'); }
            }
            const syncEvents: string[] = [];
            function* syncInner(): any {
                try { yield 2; }
                finally { syncEvents.push('inner'); }
            }
            async function* syncOuter(): any {
                try { yield* syncInner(); }
                finally { syncEvents.push('outer'); }
            }
            const arrayEvents: string[] = [];
            async function* arrayOuter(): any {
                try { yield* [4, 5]; }
                finally { arrayEvents.push('outer'); }
            }
            const errorEvents: string[] = [];
            async function* errorInner(): any {
                try { yield 1; }
                finally { errorEvents.push('inner-throw'); throw new Error('stop'); }
            }
            async function* errorOuter(): any {
                try { yield* errorInner(); }
                catch (error: any) {
                    errorEvents.push('caught:' + String(error instanceof Error && error.message === 'stop'));
                    yield 6;
                    errorEvents.push('continued');
                } finally { errorEvents.push('outer'); }
                return 12;
            }
            async function run() {
                const yielding: any = yieldingOuter();
                const yieldingFirst: any = await yielding.next();
                console.log('yield-first', yieldingFirst.value, yieldingFirst.done);
                const yieldingReturn: any = await yielding.return(7);
                console.log('yield-return', yieldingReturn.value, yieldingReturn.done, yieldingEvents.join(','));
                const yieldingNext: any = await yielding.next();
                console.log('yield-next', yieldingNext.value, yieldingNext.done, yieldingEvents.join(','));
                const yieldingClosed: any = await yielding.next();
                console.log('yield-closed', yieldingClosed.done, yieldingEvents.join(','));
                const override: any = overrideOuter();
                const overrideFirst: any = await override.next();
                console.log('override-first', overrideFirst.value, overrideFirst.done);
                const overrideReturn: any = await override.return(7);
                console.log('override-return', overrideReturn.value, overrideReturn.done, overrideEvents.join(','));
                const sync: any = syncOuter();
                const syncFirst: any = await sync.next();
                console.log('sync-first', syncFirst.value, syncFirst.done);
                const syncReturn: any = await sync.return(7);
                console.log('sync-return', syncReturn.value, syncReturn.done, syncEvents.join(','));
                const array: any = arrayOuter();
                const arrayFirst: any = await array.next();
                console.log('array-first', arrayFirst.value, arrayFirst.done);
                const arrayReturn: any = await array.return(7);
                console.log('array-return', arrayReturn.value, arrayReturn.done, arrayEvents.join(','));
                const error: any = errorOuter();
                const errorFirst: any = await error.next();
                console.log('error-first', errorFirst.value, errorFirst.done);
                const errorReturn: any = await error.return(7);
                console.log('error-return', errorReturn.value, errorReturn.done, errorEvents.join(','));
                const errorNext: any = await error.next();
                console.log('error-next', errorNext.value, errorNext.done, errorEvents.join(','));
            }
            const task = run().catch((error: any) => console.log('unexpected', error && error.name));
            """ + "\n", "yield-first 1 false\nyield-return 3 false inner-before\nyield-next 11 true inner-before,inner-after,after:7,outer\nyield-closed true inner-before,inner-after,after:7,outer\noverride-first 1 false\noverride-return 9 true inner,outer\nsync-first 2 false\nsync-return 7 true inner,outer\narray-first 4 false\narray-return 7 true outer\nerror-first 1 false\nerror-return 6 false inner-throw,caught:true\nerror-next 12 true inner-throw,caught:true,continued,outer\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DelegatedReturnsPreserveProtocolResultsAndEnclosingCleanup(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
