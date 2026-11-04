using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3PrimitivePrototypeSymbolDescriptorTests
{
    [Theory, ModeData]
    public void BooleanAndNumberPrototypeDefinitionsRetainSymbolDescriptorIdentity(ExecutionMode mode)
    {
        const string source = """
            for(const prototype of [Boolean.prototype, Number.prototype] as any[]) {
                const key = Symbol('key');
                Object.defineProperty(prototype,key,{value:7,configurable:true});
                const descriptor:any=Object.getOwnPropertyDescriptor(prototype,key);
                console.log(prototype[key],descriptor.value,descriptor.enumerable,
                    Object.getOwnPropertySymbols(prototype).includes(key));
                let reads=0;
                const getter=function(){reads++;return 'Custom'};
                Object.defineProperty(prototype,Symbol.toStringTag,{get:getter,configurable:true});
                console.log(Object.getOwnPropertyDescriptor(prototype,Symbol.toStringTag)!.get===getter,reads);
                console.log(Object.prototype.toString.call(prototype),reads);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("7 7 false true\ntrue 0\n[object Custom] 1\n", 2)),
            TestHarness.Run(source, mode));
    }
}
