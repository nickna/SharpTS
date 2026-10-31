using SharpTS.Tests.Infrastructure;
using Xunit;
namespace SharpTS.Tests.SharedTests;
public sealed class InterpreterObjectNamespaceUpdateTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const O:any=Object;O.count=1;console.log(O.count++,O.count);", "1 2\n"];
        yield return ["named-and-indexed", "const O:any=Object;O.count='4';console.log(O.count++,++O['count'],O.count,typeof O.count);console.log(O['count']--,--O.count,O.count);", "4 6 6 number\n6 4 4\n"];
        yield return ["bigint", "const O:any=Object;O.count=1n;console.log(String(O.count++),String(++O['count']),String(O.count),typeof O.count);", "1 3 3 bigint\n"];
        yield return ["own-accessor", "const O:any=Object,events:string[]=[];let value=1;Object.defineProperty(O,'count',{get:function(this:any){events.push('get:'+String(this===O));return value;},set:function(this:any,next:any){events.push('set:'+String(this===O));value=next;}});console.log(O.count++,value,events.join(','));", "1 2 get:true,set:true\n"];
        yield return ["inherited-accessor", "const O:any=Object,F:any=Function.prototype,events:string[]=[];let value=1;Object.defineProperty(F,'nscount',{get:function(this:any){events.push('get:'+String(this===O));return value;},set:function(this:any,next:any){events.push('set:'+String(this===O));value=next;},configurable:true});console.log(O.nscount++,value,events.join(','),Object.hasOwn(O,'nscount'));", "1 2 get:true,set:true false\n"];
        yield return ["retained-key-and-receiver", "const O:any=Object,events:string[]=[];O.count=1;function target(){events.push('target');return O;}const key:any={[Symbol.toPrimitive](){events.push('key');return 'count';}};console.log(target()[key]++,O.count,events.join(','));", "1 2 target,key,key\n"];
        yield return ["abrupt-coercion", "const O:any=Object,events:string[]=[],abortMarker=new Error('stop');Object.defineProperty(O,'count',{get(){events.push('get');return {valueOf(){events.push('coerce');throw abortMarker;}};},set(value:any){events.push('set');}});try{O.count++;}catch(error){console.log(error===abortMarker,events.join(','));}", "true get,coerce\n"];
        yield return ["readonly", "const O:any=Object;Object.defineProperty(O,'count',{value:7,writable:false});console.log(O.count++,++O.count,O.count);function strict(){'use strict';O.count++;}try{strict();}catch(error){console.log(error instanceof TypeError);}console.log(O.count);", "7 8 7\ntrue\n7\n"];
    }
    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row => new[] {ExecutionMode.Interpreted,ExecutionMode.Compiled}.Select(mode => new object[]{row[0],row[1],row[2],mode}));
    [Theory,MemberData(nameof(Modes))]
    public void ActualNamespacePropertiesFollowTheirOrdinaryProtocol(string name,string source,string expected,ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if(mode==ExecutionMode.Interpreted)Assert.Equal(expected,TestHarness.RunInterpreted(source));
        else {var(errors,output)=TestHarness.CompileVerifyAndRun(source);Assert.Empty(errors);Assert.Equal(expected,output);}
    }
}
