using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassIntegerKeyOrderTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void ClassInstancesSortArrayIndicesBeforeOrdinaryStringsAndSymbols(ExecutionMode mode)
    {
        const string source = """
            const symbol = Symbol('s');
            class C {
              first = (this.extra = 1);
              later = (this[10] = 7);
              constructor() {
                this[symbol] = 9;
                this['01'] = 8;
                this[2] = 6;
              }
            }
            const value = new C();
            console.log(Object.keys(value).join(','));
            console.log(Object.getOwnPropertyNames(value).join(','));
            console.log(Reflect.ownKeys(value).map(String).join(','));
            console.log(Object.values(value).join(','));
            console.log(Object.entries(value).map(pair => pair.join(':')).join(','));
            console.log(Object.keys({ ...value }).join(','));
            """;
        Assert.Equal("2,10,extra,first,later,01\n2,10,extra,first,later,01\n"
            + "2,10,extra,first,later,01,Symbol(s)\n6,7,1,1,7,8\n"
            + "2:6,10:7,extra:1,first:1,later:7,01:8\n2,10,extra,first,later,01\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
