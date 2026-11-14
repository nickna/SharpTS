using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class YieldStarResultAccessorTests
{
    public const string Source = """
        function run(request, property, marker) {
            console.log(request, property);
            const result = {
                get done() {
                    console.log('read done');
                    if (property === 'done') throw marker;
                    return true;
                },
                get value() { console.log('read value'); throw marker; }
            };
            const iterator = {
                next() { return { value: 1, done: false }; },
                return() { return result; },
                throw() { return result; }
            };
            function* outer() {
                try {
                    try { yield* { [Symbol.iterator]() { return iterator; } }; }
                    catch (error) { console.log('caught', error === marker); yield 'recovered'; }
                } finally { console.log('finally'); yield 'cleanup'; }
            }
            const generator = outer();
            generator.next();
            const resumed = request === 'return' ? generator.return(9) : generator.throw(9);
            console.log(resumed.value, resumed.done);
            const cleanup = generator.next();
            console.log(cleanup.value, cleanup.done);
            console.log(generator.next().done);
        }
        run('return', 'done', {});
        run('return', 'value', null);
        run('throw', 'done', 0);
        run('throw', 'value', {});
        """;

    public const string Expected = "return done\nread done\ncaught true\nrecovered false\nfinally\ncleanup false\ntrue\n" +
        "return value\nread done\nread value\ncaught true\nrecovered false\nfinally\ncleanup false\ntrue\n" +
        "throw done\nread done\ncaught true\nrecovered false\nfinally\ncleanup false\ntrue\n" +
        "throw value\nread done\nread value\ncaught true\nrecovered false\nfinally\ncleanup false\ntrue\n";

    [Theory, ModeData]
    public void ForwardedResultAccessorFailuresReachYieldingHandlers(ExecutionMode mode)
    {
        Assert.Equal(Expected, TestHarness.RunUnchecked(Source, mode));
    }
}
