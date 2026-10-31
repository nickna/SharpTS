using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveStringDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "'use strict';function remove(value:any){try{console.log('result',delete value.length);}catch(error){console.log('caught',error instanceof TypeError);}}remove('x');", "caught true\n"];
        yield return ["sloppy-length", "const value:any='ab';console.log(delete value.length,value.length);", "false 2\n"];
        yield return ["canonical-indices", "const value:any='ab';console.log(delete value[0],value[0],delete value[1],value[1],delete value[2],delete value['01'],delete value['-0']);", "false a false b true true true\n"];
        yield return ["strict-indices", "'use strict';const value:any='ab';for(const key of ['0','1','length']){try{delete value[key];}catch(error){console.log(error instanceof TypeError);}}", "true\ntrue\ntrue\n"];
        yield return ["utf16-indices", "const value:any='\uD834\uDF06';console.log(delete value[0],delete value[1],delete value[2],value.length);", "false false true 2\n"];
        yield return ["object-key-once", "'use strict';const value:any='ab';let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return '0';}};try{delete value[key];}catch(error){console.log(error instanceof TypeError,calls);}", "string\ntrue 1\n"];
        yield return ["other-primitives", "const number:any=7,boolean:any=true,symbol:any=Symbol(),bigint:any=1n;console.log(delete number.length,delete boolean[0],delete symbol.x,delete bigint[0]);", "true true true true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void HonorsOnlyNonconfigurablePrimitiveStringOwnProperties(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
