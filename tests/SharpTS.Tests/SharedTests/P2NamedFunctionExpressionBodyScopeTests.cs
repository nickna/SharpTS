using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P2NamedFunctionExpressionBodyScopeTests
{
    [Theory, ModeData]
    public void BodyVarCreatesADistinctBindingFromThePrivateSelfName(ExecutionMode mode)
    {
        const string source = """
            const outside:any='outer';
            let readBody:any;
            const value:any=function named(){var named;readBody=function(){return named};};
            value();
            console.log(readBody()===undefined,outside);
            const parameter:any=function named(named:any){return ()=>named};
            console.log(parameter(7)());
            """;
        Assert.Equal("true outer\n7\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BodyLexicalShadowRetainsItsTdzAndClosureOwner(ExecutionMode mode)
    {
        const string source = """
            const value:any=function named(){
                const read=()=>named;
                try{console.log(read())}catch(error){console.log(error instanceof ReferenceError)}
                let named:any=1;
                console.log(read());
            };
            value();
            """;
        Assert.Equal("true\n1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DefaultsAndRecursionKeepThePrivateSelfOwnerAndOuterCaptures(ExecutionMode mode)
    {
        const string source = """
            const outer=5;
            const value:any=function named(n:number=2,read:any=()=>named){
                var local:any='body';
                console.log(read()===value,local,outer);
                if(n>0)read()(n-1);
            };
            value();
            const recursive:any=function self(n:number):number{return n===0?outer:self(n-1)+1};
            console.log(recursive(2));
            """;
        Assert.Equal("true body 5\ntrue body 5\ntrue body 5\n7\n", TestHarness.Run(source, mode));
    }
}
