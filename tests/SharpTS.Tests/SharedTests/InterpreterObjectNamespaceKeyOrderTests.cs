using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterObjectNamespaceKeyOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const O:any=Object;O[7]=1;console.log(Object.getOwnPropertyNames(O).slice(0,3).join(','));console.log(Reflect.ownKeys(O).slice(0,3).join(','));", "7,length,name\n7,length,name\n"];
        yield return ["ascending", "const O:any=Object;O[11]=11;O[2]=2;O[0]=0;console.log(Object.getOwnPropertyNames(O).slice(0,4).join(','),Object.keys(O).join(','));", "0,2,11,length 0,2,11\n"];
        yield return ["canonical-index-boundary", "const O:any=Object;O['4294967295']=1;O['4294967294']=1;O['01']=1;O['-0']=1;O['1.0']=1;O['0']=1;const selected=['0','4294967294','4294967295','01','-0','1.0'];console.log(Object.getOwnPropertyNames(O).filter(key=>selected.includes(key)).join(','));", "0,4294967294,4294967295,01,-0,1.0\n"];
        yield return ["deleted-and-recreated-index", "const O:any=Object;O[7]=7;O[2]=2;delete O[7];O[7]=8;console.log(Object.getOwnPropertyNames(O).slice(0,3).join(','),O[7]);", "2,7,length 8\n"];
        yield return ["hidden-and-getter-inventory", "const O:any=Object;let calls=0;Object.defineProperty(O,'7',{get(){calls++;return 1;},enumerable:true,configurable:true});Object.defineProperty(O,'2',{value:2,enumerable:false});console.log(Object.getOwnPropertyNames(O).slice(0,3).join(','),Reflect.ownKeys(O).slice(0,3).join(','),calls);console.log(Object.keys(O).join(','),calls);", "2,7,length 2,7,length 0\n7 0\n"];
        yield return ["transparent-proxy", "const O:any=Object;O[7]=1;const P:any=new Proxy(O,{});console.log(Object.getOwnPropertyNames(P).slice(0,3).join(','),Reflect.ownKeys(P).slice(0,3).join(','));", "7,length,name 7,length,name\n"];
        yield return ["symbol-position", "const O:any=Object,key=Symbol('k');O[key]=1;O[7]=1;const keys=Reflect.ownKeys(O);console.log(keys[0]==='7',keys[keys.length-1]===key,keys.indexOf(key)>keys.indexOf('name'));", "true true true\n"];
        yield return ["copy-key-snapshot", "const O:any=Object;Object.defineProperty(O,'2',{get(){O[1]=1;return 2;},enumerable:true});O[7]=7;O.tail=9;const out:any={...O};console.log(Object.keys(out).join(','),Object.keys(O).join(','));", "2,7,tail 1,2,7,tail\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OrdersOnlyCanonicalIndicesBeforeOrdinaryNamesAndSymbols(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Interpreted)
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        else
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
    }
}
