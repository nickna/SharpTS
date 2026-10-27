using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2YieldingForOfCloseTests
{
    [Theory, ModeData]
    public void SuspendedLoopsCloseOnBreakReturnThrowAndInjectedReturn(ExecutionMode mode)
    {
        const string source = """
            const state = { closed: 0 };
            function* inner() { try { yield 1; yield 2; } finally { state.closed++; } }
            function* breaking() { for (const x of inner()) { yield x; break; } }
            function* returning() { for (const x of inner()) { yield x; return 7; } }
            function* throwing() { for (const x of inner()) { yield x; throw 'body'; } }
            function* suspended() { for (const x of inner()) { yield x; } }
            const a = breaking(); console.log(a.next().value, state.closed);
            console.log(a.next().done, state.closed);
            const b = returning(); b.next(); console.log(b.next().value, state.closed);
            const c = throwing(); c.next(); try { c.next(); } catch (e) { console.log(e, state.closed); }
            const d = suspended(); d.next(); console.log(d.return(9).value, state.closed);
            """;
        Assert.Equal("1 0\ntrue 1\n7 2\nbody 3\n9 4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CaughtBodyThrowsAndContinueKeepIteratorOpenUntilExhaustion(ExecutionMode mode)
    {
        const string source = """
            let closed = 0;
            function* inner() { try { yield 1; yield 2; } finally { closed++; } }
            function* outer() {
                for (const x of inner()) {
                    yield x;
                    try { throw 'caught'; } catch (e) { console.log(e, closed); }
                    continue;
                }
            }
            const it = outer();
            console.log(it.next().value, closed);
            console.log(it.next().value, closed);
            console.log(it.next().done, closed);
            """;
        Assert.Equal("1 0\ncaught 0\n2 0\ncaught 0\ntrue 1\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void CloseRunsAfterBodyFinallyAndPreservesThrowCompletion()
    {
        const string source = """
            let order = '';
            const iterable: any = {
                [Symbol.iterator]() {
                    return {
                        next() { return { value: 1, done: false }; },
                        return() { order += 'c'; throw 'close'; }
                    };
                }
            };
            function* throwing() {
                for (const x of iterable) {
                    try { yield x; throw 'body'; } finally { order += 'f'; }
                }
            }
            const a = throwing(); a.next();
            try { a.next(); } catch (e) { console.log(e, order); }
            order = '';
            function* breaking() { for (const x of iterable) { yield x; break; } }
            const b = breaking(); b.next();
            try { b.next(); } catch (e) { console.log(e, order); }
            """;
        Assert.Equal("body fc\nclose c\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
