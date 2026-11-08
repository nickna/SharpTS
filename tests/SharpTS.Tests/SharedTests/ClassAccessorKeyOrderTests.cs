using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassAccessorKeyOrderTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void EnumerableAccessorsKeepTheirPositionAcrossRedefinitionsAndCopies(ExecutionMode mode)
    {
        const string source = """
            let reads = 0;
            const symbol = Symbol('last');
            const hidden = Symbol('hidden');
            class C {
              first = (Object.defineProperty(this, 'extra', {
                get() { reads++; return 9; }, enumerable: true, configurable: true
              }), 1);
              later = 7;
            }
            const item = new C();
            item[symbol] = 8;
            Object.defineProperty(item, hidden, { value: 99 });
            console.log(Object.keys(item).join(','), Object.getOwnPropertyNames(item).join(','));
            const keys = Reflect.ownKeys(item);
            console.log(keys.slice(0, 3).join(','), keys[3] === symbol, keys[4] === hidden, reads);
            console.log(Object.values(item).join(','));
            console.log(Object.entries(item).map(pair => pair.join(':')).join(','));
            const { later, ...rest } = item;
            for (const copy of [rest, { ...item }, Object.assign({}, item)]) {
              console.log(Object.keys(copy).join(','), copy.extra, copy[symbol], Object.hasOwn(copy, hidden));
            }
            console.log(reads);
            Object.defineProperty(item, 'extra', { value: 3 });
            console.log(Object.keys(item).join(','), item.extra);
            delete item.extra;
            item.extra = 4;
            console.log(Object.keys(item).join(','), item.extra);
            """;
        Assert.Equal("extra,first,later extra,first,later\nextra,first,later true true 0\n"
            + "9,1,7\nextra:9,first:1,later:7\nextra,first 9 8 false\nextra,first,later 9 8 false\n"
            + "extra,first,later 9 8 false\n5\nextra,first,later 3\nfirst,later,extra 4\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void ValueEnumerationRechecksDescriptorsAfterInvokingAnEarlierGetter(ExecutionMode mode)
    {
        const string source = """
            class C {
              first = (Object.defineProperty(this, 'early', {
                get() { delete this.later; this.added = 4; return 2; }, enumerable: true
              }), 1);
              later = 3;
            }
            const item = new C();
            console.log(Object.values(item).join(','), Object.keys(item).join(','));
            """;
        Assert.Equal("2,1 early,first,added\n", TestHarness.RunUnchecked(source, mode));
    }
}
