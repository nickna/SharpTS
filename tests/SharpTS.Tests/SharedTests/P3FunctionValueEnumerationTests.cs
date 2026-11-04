using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3FunctionValueEnumerationTests
{
    [Theory, ModeData]
    public void FunctionValueEnumerationInvokesOwnGettersAndKeysDoesNot(ExecutionMode mode)
    {
        const string source = """
            function ordinary() {}
            for (const fn of [ordinary, () => {}] as any[]) {
                for (const key of ['visible', 'name']) {
                    let reads = 0;
                    Object.defineProperty(fn, key, {
                        get() { reads++; return 9; }, enumerable: true, configurable: true
                    });
                    console.log(Object.keys(fn).join(','), reads);
                    console.log(JSON.stringify(Object.values(fn)), reads);
                    console.log(JSON.stringify(Object.entries(fn)), reads);
                    console.log(JSON.stringify(Object.assign({}, fn)), reads);
                    delete fn[key];
                }
            }
            """;
        const string expected = "visible 0\n[9] 1\n[[\"visible\",9]] 2\n{\"visible\":9} 3\n"
            + "name 0\n[9] 1\n[[\"name\",9]] 2\n{\"name\":9} 3\n";
        Assert.Equal(expected + expected, TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FunctionValueEnumerationSnapshotsKeysAndChecksLaterDescriptors(ExecutionMode mode)
    {
        const string source = """
            for(const operation of [Object.values, Object.entries, (value:any)=>Object.assign({},value)]) {
                const fn:any=function(){};
                Object.defineProperty(fn,'first',{get(){delete fn.second;fn.later=7;return 9},enumerable:true});
                fn.second=2;
                console.log(JSON.stringify(operation(fn)));
            }
            """;
        Assert.Equal("[9]\n[[\"first\",9]]\n{\"first\":9}\n", TestHarness.Run(source, mode));
    }
}
