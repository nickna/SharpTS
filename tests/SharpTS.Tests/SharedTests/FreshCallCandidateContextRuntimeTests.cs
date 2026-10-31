using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FreshCallCandidateContextRuntimeTests
{
    [Theory, ModeData]
    public void CandidateContextsPreserveValuesAndEvaluateArgumentsOnce(ExecutionMode mode)
    {
        const string source = """
            type Action={type:'size',size:number}|{type:'textSize',value:number};
            function generic<T extends Action>(value:T):T{return value;}
            function nested<T extends Action>(...values:[[T]]):T{return values[0][0];}
            function over(value:Action):string;
            function over(value:string):string;
            function over(value:any):string{return typeof value==='string'?value:value.type;}
            let conditions=0; let payloads=0;
            function choose():boolean{conditions++;return true;}
            function payload():number{payloads++;return 7;}
            const action=generic(choose()?{type:'size',size:payload()}:{type:'textSize',value:payload()});
            console.log(action.type,over({type:'size',size:payload()}),conditions,payloads);
            console.log(nested([{type:'size',size:payload()}]).type,payloads);
            """;
        Assert.Equal("size size 1 2\nsize 3\n", TestHarness.Run(source, mode));
    }
}
