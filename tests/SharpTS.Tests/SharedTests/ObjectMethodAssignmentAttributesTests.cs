using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectMethodAssignmentAttributesTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const O:any=Object;O.keys=()=>7;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.writable,d.enumerable,d.configurable,d.value());\n", "true false true 7\n"];
        yield return ["retained-identity", "const O:any=Object,original=Object.keys;O.keys=original;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value===original,O.keys===original,d.writable,d.enumerable,d.configurable);", "true true true false true\n"];
        yield return ["deleted-then-created", "const O:any=Object;console.log(delete O.keys);O.keys=()=>7;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value(),d.writable,d.enumerable,d.configurable);", "true\n7 true true true\n"];
        yield return ["explicit-descriptor", "const O:any=Object;Object.defineProperty(O,'keys',{value:()=>7,writable:true,enumerable:false,configurable:true});O.keys=()=>8;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value(),d.writable,d.enumerable,d.configurable);", "8 true false true\n"];
        yield return ["computed-key-once", "const O:any=Object;let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return 'keys';}};O[key]=()=>9;const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(d.value(),d.enumerable,calls);", "string\n9 false 1\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesExistingNativeMethodAttributesOnAssignment(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
