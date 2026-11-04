using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class TypedArrayNamedIndexReadTests
{
    [Theory, ModeData]
    public void NonnumericComputedKeysUseOrdinaryPropertyLookup(ExecutionMode mode)
    {
        Assert.Equal("true\n7 9 2\n1 true true\n", TestHarness.Run("""
            const value: any = new Uint8Array([1, 2]);
            console.log(value['missing'] === undefined);
            value.extra = 7;
            Object.defineProperty(value, 'accessor', { get() { return 9; }, configurable: true });
            console.log(value['extra'], value['accessor'], value['length']);
            console.log(value['0'], value['-0'] === undefined, value['1.5'] === undefined);
            """, mode));
    }
}
