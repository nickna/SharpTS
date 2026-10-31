using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ProtectedConstructorDelete3204RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-reported", "const C:any=Date;console.log(delete C.prototype,Object.hasOwn(C,\"prototype\"));", "false true\n"];
        yield return ["reflect-prototype", "const C:any=Date;console.log(Reflect.deleteProperty(C,\"prototype\"),Object.hasOwn(C,\"prototype\"));", "false true\n"];
        yield return ["configurable-native-method", "const C:any=Date,held=C.now;console.log(delete C.now,Object.hasOwn(C,\"now\"),typeof held);", "true false function\n"];
        yield return ["compatible-prototype-definition", "const C:any=Date,prototype=C.prototype;Object.defineProperty(C,\"prototype\",{value:prototype});console.log(delete C.prototype,C.prototype===prototype);", "false true\n"];
        yield return ["protected-promise-regexp-control", "for(const C of [Promise,RegExp] as any[])console.log(delete C.prototype,Object.hasOwn(C,\"prototype\"));", "false true\nfalse true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ActualNativeOwnDescriptorControlsDeletion(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}