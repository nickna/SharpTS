using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2DeclaredArraySubclassIterationTests
{
    [Theory, ModeData]
    public void Issue2277Regression(ExecutionMode mode)
    {
        const string source = """
            const state = { calls: 0 };
            class Reverse extends Array<any> {
                *[Symbol.iterator](): any {
                    state.calls++;
                    yield this[1];
                    yield this[0];
                }
            }
            const values: any = new Reverse('a', 'b');
            console.log([...values].join(','));
            function collect(...items: any[]): string { return items.join(':'); }
            console.log(collect(...values));
            const [first, second] = values;
            console.log(first, second);
            let direct = '';
            for (const value of values) direct += value;
            console.log('for', direct);
            function* delegate(): Generator<any, any, any> { yield* values; }
            let delegated = '';
            for (const value of delegate()) delegated += value;
            console.log('delegate', delegated);
            console.log('calls', state.calls);

            """;
        Assert.Equal("b,a\nb:a\nb a\nfor ba\ndelegate ba\ncalls 5\n", TestHarness.Run(source, mode));
    }
}