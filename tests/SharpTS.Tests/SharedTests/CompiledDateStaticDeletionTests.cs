using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CompiledDateStaticDeletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-date-statics", """
            const D:any=Date;for(const key of ['now','UTC','parse']){console.log(key,typeof D[key],Object.hasOwn(D,key));console.log(delete D[key],typeof D[key],Object.hasOwn(D,key),Object.getOwnPropertyDescriptor(D,key)===undefined);}
            """, "now function true\ntrue undefined false true\nUTC function true\ntrue undefined false true\nparse function true\ntrue undefined false true\n"];
        yield return ["strict-date-statics", """
            function remove(target:any,key:string){'use strict';return delete target[key];}const D:any=Date;for(const key of ['now','UTC','parse'])console.log(remove(D,key),typeof D[key],Object.hasOwn(D,key));
            """, "true undefined false\ntrue undefined false\ntrue undefined false\n"];
        yield return ["reflect-date-statics", """
            const D:any=Date;for(const key of ['now','UTC','parse'])console.log(Reflect.deleteProperty(D,key),typeof D[key],Object.getOwnPropertyDescriptor(D,key)===undefined,Reflect.deleteProperty(D,key));
            """, "true undefined true true\ntrue undefined true true\ntrue undefined true true\n"];
        yield return ["retained-static-callable", """
            const D:any=Date,parse=D.parse;console.log(delete D.parse,typeof D.parse,typeof parse,parse('2000-01-01T00:00:00.000Z'));
            """, "true undefined function 946684800000\n"];
        yield return ["nonconfigurable-override", """
            const D:any=Date;Object.defineProperty(D,'now',{value:()=>7,configurable:false});console.log(delete D.now,Reflect.deleteProperty(D,'now'),D.now());function strict(){'use strict';try{return delete D.now;}catch(error){return error instanceof TypeError;}}console.log(strict(),D.now());
            """, "false false 7\ntrue 7\n"];
        yield return ["same-name-source-class", """
            const C=class Date{static now(){return 7;}};const owner:any=C;console.log(delete owner.now,typeof owner.now,Object.hasOwn(owner,'now'),Object.getOwnPropertyDescriptor(owner,'now')===undefined);
            """, "true undefined false true\n"];
        yield return ["no-date-control", """
            class Owner{static now(){return 7;}}const owner:any=Owner;console.log(delete owner.now,typeof owner.now,Object.hasOwn(owner,'now'));console.log(42);
            """, "true undefined false\n42\n"];
        yield return ["direct-static-reads-after-alias-delete", """
            const D:any=Date;delete D.now;delete D.UTC;delete D.parse;console.log(Date.now===undefined,Date.UTC===undefined,Date.parse===undefined);
            """, "true true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void DeletionHidesOnlyTheActualDateStaticProperty(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
