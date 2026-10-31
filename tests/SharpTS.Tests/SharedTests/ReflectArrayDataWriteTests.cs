using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectArrayDataWriteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const value:any=[1,2];console.log(Reflect.set(value,'0',7),value[0],Object.getOwnPropertyDescriptor(value,'0')!.value);", "true 7 7\n"];
        yield return ["object-value", "const value:any=[1],marker:any={};console.log(Reflect.set(value,'0',marker),value[0]===marker,Object.getOwnPropertyDescriptor(value,'0')!.value===marker);", "true true true\n"];
        yield return ["distinct-receiver", "const target:any={'0':1},value:any=[1,2];console.log(Reflect.set(target,'0',7,value),value[0],Object.getOwnPropertyDescriptor(value,'0')!.value,target[0]);", "true 7 7 1\n"];
        yield return ["hole-and-extension", "const value:any=new Array(2);console.log(Reflect.set(value,'0',7),value[0],value.length);console.log(Reflect.set(value,'2',9),value[2],value.length);", "true 7 2\ntrue 9 3\n"];
        yield return ["readonly", "const value:any=[1];Object.defineProperty(value,'0',{writable:false});console.log(Reflect.set(value,'0',7),value[0],Object.getOwnPropertyDescriptor(value,'0')!.value);", "false 1 1\n"];
        yield return ["accessor", "const value:any=[1];let seen=0,correct=false;Object.defineProperty(value,'0',{get(){return 9},set(v:any){seen=v;correct=this===value},configurable:true});console.log(Reflect.set(value,'0',7),value[0],seen,correct);", "true 9 7 true\n"];
        yield return ["frozen", "const value:any=[1];Object.freeze(value);console.log(Reflect.set(value,'0',7),value[0]);", "false 1\n"];
        yield return ["nonwritable-length", "const value:any=[1];Object.defineProperty(value,'length',{writable:false});console.log(Reflect.set(value,'0',7),value[0],Reflect.set(value,'1',9),value.length);", "true 7 false 1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OrdinarySetUpdatesTheLiveArraySlot(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
