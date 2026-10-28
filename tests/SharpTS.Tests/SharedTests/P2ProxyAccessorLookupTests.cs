using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2ProxyAccessorLookupTests
{
    [Theory, ModeData]
    public void LookupPropagatesOwnAndInheritedDescriptorTrapExceptions(ExecutionMode mode)
    {
        const string source = """
            const marker:any={};
            const proxy:any=new Proxy({},{getOwnPropertyDescriptor(){throw marker}});
            for(const lookup of [Object.prototype.__lookupGetter__,Object.prototype.__lookupSetter__]){
                try{lookup.call(proxy,'key');console.log('missed')}catch(error){console.log(error===marker)}
                try{lookup.call(Object.create(proxy),'key');console.log('missed')}catch(error){console.log(error===marker)}
            }
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void InterpretedLookupUsesLiveTrapDescriptorAndDataShadowingWithoutCallingAccessors()
    {
        const string source = """
            let calls=0;
            const getter:any=function(){calls++;return 3};
            const setter:any=function(value:any){calls++};
            let accessor=true;
            const target:any={};
            const proxy:any=new Proxy(target,{
                getOwnPropertyDescriptor(object:any,key:any){
                    if(key!=='key')return undefined;
                    return accessor?{get:getter,set:setter,configurable:true}:{value:4,configurable:true};
                }
            });
            const receiver:any=Object.create(proxy);
            console.log(typeof Object.prototype.__lookupGetter__.call(receiver,'key'));
            console.log(typeof Object.prototype.__lookupSetter__.call(receiver,'key'));
            accessor=false;
            console.log(Object.prototype.__lookupGetter__.call(receiver,'key')===undefined);
            console.log(Object.prototype.__lookupSetter__.call(receiver,'key')===undefined,calls);
            """;
        Assert.Equal("function\nfunction\ntrue\ntrue 0\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }
}
