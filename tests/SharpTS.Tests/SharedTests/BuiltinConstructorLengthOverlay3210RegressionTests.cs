using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BuiltinConstructorLengthOverlay3210RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["preserve-metadata-value-and-flags", "const C:any=Map;Object.defineProperty(C,\"length\",{writable:true});C.length=9;const d:any=Object.getOwnPropertyDescriptor(C,\"length\");console.log(C.length,d.writable,d.enumerable,d.configurable);", "9 true false true\n"];
        yield return ["incompatible-refusal", "const C:any=Map;Object.defineProperty(C,\"length\",{value:1,writable:false,configurable:false});try{Object.defineProperty(C,\"length\",{value:2});}catch(error){console.log(error instanceof TypeError);}console.log(C.length);", "true\n1\n"];
        yield return ["exact-reported", "const C:any=Map;Object.defineProperty(C,\"length\",{writable:true});const d:any=Object.getOwnPropertyDescriptor(C,\"length\");console.log(d.writable,d.configurable,d.value===C.length);", "true true true\n"];
        yield return ["actual-accessor-without-read", "const C:any=Map;let calls=0;const get=function(){calls++;return 7;};Object.defineProperty(C,\"length\",{get,configurable:true});const d:any=Object.getOwnPropertyDescriptor(C,\"length\");console.log(calls,d.get===get,d.value===undefined);", "0 true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ActualLengthOverlayPrecedesCallableMetadata(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}