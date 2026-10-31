using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2DateSymbolPropertyTests
{
    [Theory, ModeData]
    public void DateSymbolWritesKeepIdentitySeparateFromStringKeysAndOverrideTheTag(ExecutionMode mode)
    {
        const string source = """
            const date:any=new Date(0);
            const symbol=Symbol('key');
            date[symbol]=1;date['Symbol(key)']=2;
            console.log(date[symbol],date['Symbol(key)'],Object.getOwnPropertySymbols(date)[0]===symbol,Object.getOwnPropertyNames(date).join(','));
            date[Symbol.toStringTag]='CustomDate';
            console.log(Object.prototype.toString.call(date));
            """;
        Assert.Equal("1 2 true Symbol(key)\n[object CustomDate]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SymbolDescriptorsReflectionDeletionAndInheritedGetterUseCanonicalStorage(ExecutionMode mode)
    {
        const string source = """
            const date:any=new Date(0);
            const symbol=Symbol('value');
            Object.defineProperty(date,symbol,{get(){return this===date?9:0},enumerable:true,configurable:true});
            console.log(date[symbol],typeof Object.getOwnPropertyDescriptor(date,symbol).get,Reflect.ownKeys(date)[0]===symbol);
            delete date[symbol];
            console.log(Object.getOwnPropertySymbols(date).length);
            let reads=0;
            Object.defineProperty(Date.prototype,Symbol.toStringTag,{get(){reads++;return this===date?'InheritedDate':'other'},configurable:true});
            console.log(Object.prototype.toString.call(date),reads);
            """;
        Assert.Equal("9 function true\n0\n[object InheritedDate] 1\n", TestHarness.Run(source, mode));
    }
}
