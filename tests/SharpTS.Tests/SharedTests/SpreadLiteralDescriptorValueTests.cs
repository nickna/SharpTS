using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SpreadLiteralDescriptorValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["call-original", "const source:any={b:2};Object.defineProperty(source,'a',{value:1,enumerable:true,writable:false,configurable:true});function show(value:any){const d:any=Object.getOwnPropertyDescriptor(value,'a');console.log(value.a,value.b,d.value,d.writable,d.enumerable,d.configurable);}show({...source,a:3});", "3 2 3 true true true\n"];
        yield return ["constructor-original", "const source:any={b:2};Object.defineProperty(source,'a',{value:1,enumerable:true,writable:false,configurable:true});const Constructor:any=function(value:any){const d:any=Object.getOwnPropertyDescriptor(value,'a');console.log(value.a,value.b,d.value,d.writable,d.enumerable,d.configurable);};new Constructor({...source,a:3});", "3 2 3 true true true\n"];
        yield return ["source-unchanged", "const source:any={};Object.defineProperty(source,'a',{value:1,enumerable:true,writable:false,configurable:false});const value:any={...source,a:3};const before:any=Object.getOwnPropertyDescriptor(source,'a'),after:any=Object.getOwnPropertyDescriptor(value,'a');console.log(source.a,before.writable,before.configurable,value.a,after.value,after.writable,after.configurable);", "1 false false 3 3 true true\n"];
        yield return ["multiple-overwrites", "const source:any={};Object.defineProperty(source,'a',{value:1,enumerable:true});const second:any={a:3};const value:any={...source,a:2,...second};const d:any=Object.getOwnPropertyDescriptor(value,'a');console.log(value.a,d.value,d.writable,d.enumerable,d.configurable);", "3 3 true true true\n"];
        yield return ["static-before-spread", "const source:any={};Object.defineProperty(source,'a',{value:1,enumerable:true,writable:false});const value:any={a:3,...source};const d:any=Object.getOwnPropertyDescriptor(value,'a');console.log(value.a,d.value,d.writable,d.enumerable,d.configurable);", "1 1 true true true\n"];
        yield return ["promoted-source-replacement", "const source:any={};Object.defineProperty(source,'a',{value:1,enumerable:true});function copy(n:number):any{const promoted={a:n,b:n+1};return {...source,...promoted};}const value=copy(3),d=Object.getOwnPropertyDescriptor(value,'a')!;console.log(value.a,value.b,d.value,d.writable,Object.keys(value).join(','));", "3 4 3 true a,b\n"];
        yield return ["null-and-undefined-values", "const source:any={};Object.defineProperty(source,'a',{value:1,enumerable:true});const first:any={...source,a:null},second:any={...source,a:undefined};console.log(first.a,Object.getOwnPropertyDescriptor(first,'a')!.value,second.a,Object.getOwnPropertyDescriptor(second,'a')!.value,Object.keys(second).join(','));", "null null undefined undefined a\n"];
        yield return ["accessor-source-once", "let reads=0;const source:any={get a(){reads++;return 1;}};const value:any={...source,a:3},d:any=Object.getOwnPropertyDescriptor(value,'a');console.log(value.a,d.value,'get' in d,d.writable,reads);", "3 3 false true 1\n"];
        yield return ["key-order", "const source:any={b:2};Object.defineProperty(source,'a',{value:1,enumerable:true});const value:any={...source,a:3,c:4};console.log(Object.keys(value).join(','),Object.getOwnPropertyDescriptor(value,'a')!.value);", "b,a,c 3\n"];
        yield return ["computed-control", "const source:any={};Object.defineProperty(source,'a',{value:1,enumerable:true});const key:string='a',value:any={...source,[key]:3},d:any=Object.getOwnPropertyDescriptor(value,key);console.log(value.a,d.value,d.writable,d.enumerable,d.configurable);", "3 3 true true true\n"];
        yield return ["async-control", "const source:any={};Object.defineProperty(source,'a',{value:1,enumerable:true});async function run(){const value:any={...source,a:await Promise.resolve(3)},d:any=Object.getOwnPropertyDescriptor(value,'a');console.log(value.a,d.value,d.writable);}run();", "3 3 true\n"];
        yield return ["generator-control", "const source:any={};Object.defineProperty(source,'a',{value:1,enumerable:true});function* run(){const value:any={...source,a:3},d:any=Object.getOwnPropertyDescriptor(value,'a');yield value.a;yield d.value;}const iterator=run();console.log(iterator.next().value,iterator.next().value);", "3 3\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void LaterDataKeepsItsOwnDescriptorCurrent(string name, string source, string expected, ExecutionMode mode)
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
