using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ProxyIterableAcquisitionTests
{
    [Theory, ModeData]
    public void FromAndSpreadAcquireTheProxyIteratorOnce(ExecutionMode mode)
    {
        const string source = """
            let reads=0;
            const source:any=new Proxy({length:0},{get(target:any,key:any){
                if(typeof key==='symbol'){
                    reads++;return function(this:any){console.log(this===source);
                        let done=false;return {next(){if(done)return {done:true};done=true;return {done:false,value:7};}};
                    };
                }return target[key];
            }});
            console.log(Array.from(source).join(','),reads);
            console.log([...source].join(','),reads);
            """;
        Assert.Equal("true\n7 1\ntrue\n7 2\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AbsentAndInvalidMethodsPreserveGetMethodSemantics(ExecutionMode mode)
    {
        const string source = """
            let reads=0;
            const source:any=new Proxy({0:3,length:1},{get(target:any,key:any){
                if(typeof key==='symbol'){reads++;return null;}return target[key];
            }});
            console.log(Array.from(source).join(','),reads);
            const bad:any=new Proxy({}, {get(target:any,key:any){return 7;}});
            try{Array.from(bad);}catch(e){console.log(e instanceof TypeError);}
            const revoked=Proxy.revocable({},{});revoked.revoke();
            try{Array.from(revoked.proxy as any);}catch(e){console.log(e instanceof TypeError);}
            """;
        Assert.Equal("3 1\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
