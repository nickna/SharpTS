using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2NativePromiseExecutorValidationTests
{
    [Theory, ModeData]
    public void DirectAndAliasedInvalidExecutorsThrowGuestTypeError(ExecutionMode mode)
    {
        const string source = """
            const Constructor:any=Promise;
            for(const value of [undefined,null,'bad',1,{},Symbol('value')]){
                try{new Promise(value)}catch(error){console.log(error instanceof TypeError,error.name)}
                try{new Constructor(value)}catch(error){console.log(error instanceof TypeError,error.name)}
            }
            try{new Promise()}catch(error){console.log(error instanceof TypeError,error.name)}
            try{new Constructor()}catch(error){console.log(error instanceof TypeError,error.name)}
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true TypeError\n", 14)), TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ExtraArgumentsAreEvaluatedBeforeExecutorAndThenIgnored(ExecutionMode mode)
    {
        const string source = """
            const trace:any[]=[];
            const promise:any=new Promise((resolve:any)=>{trace.push('executor');resolve(7)},trace.push('extra'));
            trace.push('after');
            promise.then((value:any)=>console.log(trace.join(','),value));
            """;
        Assert.Equal("extra,executor,after 7\n", TestHarness.RunUnchecked(source, mode));
    }
}
