using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveStringDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original","const descriptor:any=Object.getOwnPropertyDescriptor('abc','0');console.log(descriptor.value,descriptor.writable,descriptor.enumerable,descriptor.configurable);","a false true false\n"];
        yield return ["canonical-indices","""
            const own=Object.getOwnPropertyDescriptor;for(const key of ['0','1','2','01','-0','+1',' 1','3','2147483648','charAt']){const descriptor:any=own('abc',key);console.log(key,descriptor===undefined?'missing':descriptor.value);}
            ""","0 a\n1 b\n2 c\n01 missing\n-0 missing\n+1 missing\n 1 missing\n3 missing\n2147483648 missing\ncharAt missing\n"];
        yield return ["length-symbol-and-key-conversion","""
            let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return '1';}};
            const descriptor:any=Object.getOwnPropertyDescriptor('abc',key),lengthDescriptor:any=Object.getOwnPropertyDescriptor('abc','length');
            console.log(descriptor.value,calls,lengthDescriptor.value,lengthDescriptor.writable,lengthDescriptor.enumerable,lengthDescriptor.configurable,Object.getOwnPropertyDescriptor('abc',Symbol('key'))===undefined);
            ""","string\nb 1 3 false false false true\n"];
        yield return ["utf16-and-empty","""
            const first:any=Object.getOwnPropertyDescriptor('😀','0'),second:any=Object.getOwnPropertyDescriptor('😀','1');
            console.log(first.value.charCodeAt(0),second.value.charCodeAt(0),first.enumerable,second.enumerable,Object.getOwnPropertyDescriptor('','0')===undefined);
            ""","55357 56832 true true true\n"];
    }

    public static IEnumerable<object[]> Modes()=>Cases().SelectMany(row=>new[]{ExecutionMode.Interpreted,ExecutionMode.Compiled}
        .Select(mode=>new object[]{row[0],row[1],row[2],mode}));

    [Theory,MemberData(nameof(Modes))]
    public void StringOwnPropertiesHaveExactVirtualDescriptors(string name,string source,string expected,ExecutionMode mode)
    {
        Assert.NotEmpty(name);Assert.Equal(expected,TestHarness.Run(source,mode));
    }
}
