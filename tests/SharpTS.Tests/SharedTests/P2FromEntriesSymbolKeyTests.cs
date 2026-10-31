using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2FromEntriesSymbolKeyTests
{
    [Theory, ModeData]
    public void FromEntriesPreservesSymbolsAndConvertsKeysAfterEntryReads(ExecutionMode mode)
    {
        const string source = """
            const key = Symbol("entry");
            const result: any = Object.fromEntries([[key, 42]]);
            console.log(result[key], Object.getOwnPropertySymbols(result).length, Object.keys(result).length);
            const events: string[] = [];
            const coercible: any = { [Symbol.toPrimitive](hint: string) { events.push(hint); return key; } };
            const entry: any = {
                get 0() { events.push("key"); return coercible; },
                get 1() { events.push("value"); return 7; }
            };
            const converted: any = Object.fromEntries([entry, [undefined, 1], [null, 2]]);
            console.log(converted[key], converted.undefined, converted.null, events.join(","));
            console.log(Object.getOwnPropertyDescriptor(converted, key)!.enumerable);
            """;
        Assert.Equal("42 1 0\n7 1 2 key,value,string\ntrue\n", TestHarness.Run(source, mode));
    }
}
