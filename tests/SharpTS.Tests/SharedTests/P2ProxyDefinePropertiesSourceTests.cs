using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2ProxyDefinePropertiesSourceTests
{
    [Theory, ModeData]
    public void MissingOwnKeysTrapForwardsMixedTargetKeysInSpecificationOrder(ExecutionMode mode)
    {
        const string source = """
            const target:any={};
            const symbol=Symbol('key');
            target[symbol]=1;target.foo=2;target[0]=3;
            const keys:any[]=[];
            const proxy:any=new Proxy(target,{getOwnPropertyDescriptor(object:any,key:any){
                keys.push(typeof key==='symbol'?'symbol':key);return undefined;
            }});
            Object.defineProperties({},proxy);
            console.log(JSON.stringify(keys));
            """;
        Assert.Equal("[\"0\",\"foo\",\"symbol\"]\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void TrapOrderAndLiveEnumerabilitySurviveDescriptorConversion(ExecutionMode mode)
    {
        const string source = """
            const symbol=Symbol('key');
            const trace:any[]=[];
            let includeSecond=true;
            const first:any={get value(){trace.push('convert');includeSecond=false;return 2}};
            const source:any=new Proxy({},{
                ownKeys(){trace.push('own');return [symbol,'first','second']},
                getOwnPropertyDescriptor(object:any,key:any){
                    trace.push('desc:'+(typeof key==='symbol'?'symbol':key));
                    return {enumerable:key!=='second'||includeSecond,configurable:true};
                },
                get(object:any,key:any){
                    trace.push('get:'+(typeof key==='symbol'?'symbol':key));
                    return key===symbol?{value:1}:key==='first'?first:{value:3};
                }
            });
            const target:any={};
            Object.defineProperties(target,source);
            console.log(trace.join(','),target[symbol],target.first,Object.hasOwn(target,'second'));
            """;
        Assert.Equal("own,desc:symbol,get:symbol,desc:first,get:first,convert,desc:second 1 2 false\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AbruptSourceGetPreventsApplyingEarlierDefinitions(ExecutionMode mode)
    {
        const string source = """
            const marker:any={};
            const target:any={};
            const source:any=new Proxy({first:1,second:2},{
                get(object:any,key:any){if(key==='second')throw marker;return {value:4}}
            });
            try{Object.defineProperties(target,source)}catch(error){console.log(error===marker)}
            console.log(Object.hasOwn(target,'first'),Object.hasOwn(target,'second'));
            """;
        Assert.Equal("true\nfalse false\n", TestHarness.Run(source, mode));
    }
}
