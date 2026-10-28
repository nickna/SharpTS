using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2PrimitiveToStringTagTests
{
    [Theory, ModeData]
    public void PrimitiveReceiversUseTheirLivePrototypeTags(ExecutionMode mode)
    {
        const string source = """
            Boolean.prototype[Symbol.toStringTag]='BooleanTag';
            Number.prototype[Symbol.toStringTag]='NumberTag';
            String.prototype[Symbol.toStringTag]='StringTag';
            console.log(Object.prototype.toString.call(true),Object.prototype.toString.call(0),Object.prototype.toString.call(''));
            console.log(Object.prototype.toString.call(Boolean.prototype),Object.prototype.toString.call(Number.prototype),Object.prototype.toString.call(String.prototype));
            console.log(Object.prototype.toString.call(null),Object.prototype.toString.call(undefined));
            """;
        Assert.Equal("[object BooleanTag] [object NumberTag] [object StringTag]\n[object BooleanTag] [object NumberTag] [object StringTag]\n[object Null] [object Undefined]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NonStringTagFallsBackAndGetterIsReadOnce(ExecutionMode mode)
    {
        const string source = """
            let reads=0;
            Object.defineProperty(String.prototype,Symbol.toStringTag,{get(){reads++;return 7},configurable:true});
            console.log(Object.prototype.toString.call(''),reads);
            Object.defineProperty(BigInt.prototype,Symbol.toStringTag,{get(){reads++;return 7},configurable:true});
            console.log(Object.prototype.toString.call(1n),reads);
            """;
        Assert.Equal("[object String] 1\n[object Object] 2\n", TestHarness.Run(source, mode));
    }
}
