using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2IteratorCloseCompletionTests
{
    [Theory, ModeData]
    public void BreakPropagatesCloseFailureWhileBodyThrowsKeepTheirValue(ExecutionMode mode)
    {
        const string source = """
            function make(returnValue: any) {
                return { [Symbol.iterator]() { return {
                    next() { return { value: 1, done: false }; },
                    return: returnValue
                }; } };
            }
            const fail: any = function() { throw 'close'; };
            try { for (const x of make(fail)) break; } catch (e) { console.log(e); }
            try { for (const x of make(fail)) throw 'body'; } catch (e) { console.log(e); }
            try { for (const x of make(1)) break; } catch (e) { console.log(e instanceof TypeError); }
            try { for (const x of make(function() { return 1; })) break; }
            catch (e) { console.log(e instanceof TypeError); }
            """;
        Assert.Equal("close\nbody\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }
}
