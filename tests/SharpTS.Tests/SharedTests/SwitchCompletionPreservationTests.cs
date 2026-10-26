using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SwitchCompletionPreservationTests
{
    public static IEnumerable<object[]> Programs()
    {
        yield return ["exhaustive-fallthrough-default", """
            function read(value:0|1){
                let log='';
                switch(value){case 0:log+='a';default:log+='d';case 1:log+='b';break;}
                return log;
            }
            console.log(read(0),read(1));
            """, "adb b\n"];
        yield return ["strict-literal-exhaustiveness", """
            function readNumber(value:1|'1'){
                switch(value){case 1:return 'number';default:return 'string';}
            }
            function readBoolean(value:true|'True'){
                switch(value){case true:return 'boolean';default:return 'string';}
            }
            function readBigint(value:1n|'1n'){
                switch(value){case 1n:return 'bigint';default:return 'string';}
            }
            console.log(readNumber(1),readNumber('1'));
            console.log(readBoolean(true),readBoolean('True'));
            console.log(readBigint(1n),readBigint('1n'));
            """, "number string\nboolean string\nbigint string\n"];
        yield return ["conditional-break-and-finally", """
            function read(value:0|1,choose:boolean):string{
                switch(value){case 0:if(choose)break;return 'zero';default:return 'one';}
                return 'tail';
            }
            function finalized(value:0|1,choose:boolean):number{
                switch(value){case 0:try{return 1;}finally{if(choose)break;}default:return 2;}
                return 3;
            }
            console.log(read(0,true),read(0,false),read(1,true));
            console.log(finalized(0,true),finalized(0,false),finalized(1,true));
            """, "tail zero one\n3 1 2\n"];
    }

    public static IEnumerable<object[]> RuntimeCases() => Programs().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode => new object[] { row[1], row[2], mode }));

    [Theory, MemberData(nameof(RuntimeCases))]
    public void OptimizationRetainsReachableCompletionPaths(string source, string expected, ExecutionMode mode)
        => Assert.Equal(expected, TestHarness.Run(source, mode));
}
