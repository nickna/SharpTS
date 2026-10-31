using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ConditionalCallContextRuntimeTests
{
    [Theory, ModeData]
    public void ConditionalActionsKeepTheirPayloadAndEvaluateOnlyTheSelectedBranch(ExecutionMode mode)
    {
        const string source = """
            type Action={type:'size',size:number}|{type:'textSize',value:number}|{type:'fillTolerance',value:number};
            function dispatch(action:Action):string{
                if(action.type==='size')return action.type+':'+action.size;
                return action.type+':'+action.value;
            }
            function create(text:boolean,fill:boolean,value:number):string{
                return dispatch(text?{type:'textSize',value}:fill?{type:'fillTolerance',value:value/100}:{type:'size',size:value});
            }
            console.log(create(true,false,12),create(false,true,50),create(false,false,3));
            let conditions=0; let branches=0;
            function choose():boolean{conditions++;return true;}
            function payload():number{branches++;return 7;}
            dispatch(choose()?{type:'size',size:payload()}:{type:'textSize',value:payload()});
            console.log(conditions,branches);
            """;
        Assert.Equal("textSize:12 fillTolerance:0.5 size:3\n1 1\n", TestHarness.Run(source, mode));
    }
}
