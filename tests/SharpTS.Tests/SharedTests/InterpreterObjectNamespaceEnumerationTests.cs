using SharpTS.Tests.Infrastructure;
using Xunit;
namespace SharpTS.Tests.SharedTests;
public sealed class InterpreterObjectNamespaceEnumerationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const O:any=Object;O.extra=1;console.log(Object.keys(O).includes('extra'),Object.getOwnPropertyDescriptor(O,'extra')!.enumerable);", "true true\n"];
        yield return ["all-enumerable-views", "const O:any=Object;O.extra=1;console.log(Object.keys(O).includes('extra'),Object.values(O).includes(1),Object.entries(O).some(entry=>entry[0]==='extra'&&entry[1]===1));", "true true true\n"];
        yield return ["live-enumerability", "const O:any=Object,events:string[]=[];Object.defineProperty(O,'a',{enumerable:true,get(){events.push('a');Object.defineProperty(O,'b',{enumerable:false});return 1;}});O.b=2;console.log(Object.keys(O).join(','),events.length);console.log(Object.values(O).join(','),events.join(','));console.log(Object.entries(O).map(entry=>entry[0]+':'+entry[1]).join(','),events.join(','));", "a,b 0\n1 a\na:1 a,a\n"];
        yield return ["key-snapshot", "const O:any=Object;Object.defineProperty(O,'first',{enumerable:true,get(){O.late=9;return 1;}});O.last=2;console.log(Object.values(O).join(','),Object.keys(O).join(','));", "1,2 first,last,late\n"];
        yield return ["native-hidden-and-deleted", "const O:any=Object;O.extra=1;Object.defineProperty(O,'hidden',{value:2,enumerable:false});delete O.extra;console.log(Object.keys(O).length,Object.values(O).length,Object.entries(O).length,Object.hasOwn(O,'hidden'),Object.getOwnPropertyNames(O).includes('hidden'));", "0 0 0 true true\n"];
        yield return ["inherited-strings-excluded", "const O:any=Object,F:any=Function.prototype;F.inherited=1;O.own=2;console.log(Object.keys(O).join(','),Object.values(O).join(','));", "own 2\n"];
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
