using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue3148RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["delete-and-restore", "const object:any=globalThis;const original=ArrayBuffer;console.log(delete object.ArrayBuffer,Reflect.get(object,\"ArrayBuffer\")===undefined,Object.getOwnPropertyDescriptor(object,\"ArrayBuffer\")===undefined);object.ArrayBuffer=original;console.log(Reflect.get(object,\"ArrayBuffer\")===original,Object.getOwnPropertyDescriptor(object,\"ArrayBuffer\")!.value===original,new object.ArrayBuffer(3).byteLength);", "true true true\ntrue true 3\n"];
        yield return ["original", "const constructor:any=ArrayBuffer;\n            const original:any=Object.getOwnPropertyDescriptor(globalThis,'ArrayBuffer');\n            console.log(original.writable,original.enumerable,original.configurable);\n            delete (globalThis as any).ArrayBuffer;\n            console.log(Reflect.get(globalThis,'ArrayBuffer')===undefined,\n                Object.getOwnPropertyDescriptor(globalThis,'ArrayBuffer')===undefined);\n            Object.defineProperty(globalThis,'ArrayBuffer',{value:constructor,writable:false,enumerable:true,configurable:true});\n            const replacement:any=Object.getOwnPropertyDescriptor(globalThis,'ArrayBuffer');\n            console.log(replacement.value===constructor,replacement.writable,replacement.enumerable,\n                replacement.configurable,Reflect.get(globalThis,'ArrayBuffer')===constructor);\n", "true false true\ntrue true\ntrue false true true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OrderedProtocolControls(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
