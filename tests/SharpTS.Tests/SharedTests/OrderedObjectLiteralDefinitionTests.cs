using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class OrderedObjectLiteralDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const key:string='x';const value:any={get x(){return 1;},[key]:2};console.log(value.x);", "2\n"];
        yield return ["data-replaces-accessor", "const key:string='x';const value:any={get x(){return 1;},[key]:2};const d:any=Object.getOwnPropertyDescriptor(value,'x');console.log(value.x,d.value,'get' in d,d.writable,d.enumerable,d.configurable);", "2 2 false true true true\n"];
        yield return ["data-clears-getter-before-setter", "let calls=0;const key:string='x';const value:any={get x(){return 1;},[key]:2,set x(next:any){calls=next;}};const d:any=Object.getOwnPropertyDescriptor(value,'x');console.log(value.x,d.get===undefined,typeof d.set);value.x=9;console.log(calls);", "undefined true function\n9\n"];
        yield return ["data-clears-setter-before-getter", "let calls=0;const key:string='x';const value:any={set x(next:any){calls=next;},[key]:2,get x(){return 3;}};const d:any=Object.getOwnPropertyDescriptor(value,'x');console.log(value.x,typeof d.get,d.set===undefined,calls);", "3 function true 0\n"];
        yield return ["paired-halves", "let stored=2;const value:any={get x(){return stored;},set x(next:any){stored=next;}};value.x=7;const d:any=Object.getOwnPropertyDescriptor(value,'x');console.log(value.x,typeof d.get,typeof d.set,d.enumerable,d.configurable);", "7 function function true true\n"];
        yield return ["symbol-data-reset", "const key:symbol=Symbol('x');let calls=0;const value:any={get [key](){return 1;},[key]:2,set [key](next:any){calls=next;}};const d:any=Object.getOwnPropertyDescriptor(value,key);console.log(value[key],d.get===undefined,typeof d.set);value[key]=8;console.log(calls,Object.getOwnPropertySymbols(value).length);", "undefined true function\n8 1\n"];
        yield return ["spread-overwrites-getter", "let reads=0;const source:any={get x(){reads++;return 4;}};const value:any={get x(){return 1;},...source};const d:any=Object.getOwnPropertyDescriptor(value,'x');console.log(value.x,d.value,'get' in d,reads);", "4 4 false 1\n"];
        yield return ["spread-bypasses-target-setter", "let writes=0;const source:any={x:5};const value:any={set x(next:any){writes++;},...source};const d:any=Object.getOwnPropertyDescriptor(value,'x');console.log(value.x,d.value,'set' in d,writes);", "5 5 false 0\n"];
        yield return ["source-key-order", "const key:string='x';const value:any={get a(){return 0;},get x(){return 1;},b:3,[key]:2};console.log(Object.keys(value).join(','),value.x);", "a,x,b 2\n"];
        yield return ["computed-conversion-order", "const log:string[]=[];let reads=0;const key:any={[Symbol.toPrimitive](){log.push('key');return 'x';}};const value:any={get x(){reads++;return 1;},[key]:(log.push('value'),2)};console.log(log.join(','),value.x,reads);", "key,value 2 0\n"];
        yield return ["async-key-and-value", "async function run(){const key:string='x';const value:any={get x(){return 1;},[await Promise.resolve(key)]:await Promise.resolve(2)};const d:any=Object.getOwnPropertyDescriptor(value,'x');console.log(value.x,d.value,'get' in d);}run();", "2 2 false\n"];
        yield return ["null-prototype-and-shadow", "const key:string='x';const value:any={__proto__:null,get x(){return 1;},[key]:2};console.log(value.x,Object.getPrototypeOf(value)===null,Object.keys(value).join(','));", "2 true x\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void DefinitionsFollowSourceOrder(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
