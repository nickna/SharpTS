using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeOwnPresenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["boolean-setter-only-own-presence", "const p:any=Boolean.prototype;const key='held';Object.defineProperty(p,key,{set(v:number){},configurable:true});console.log(Object.prototype.hasOwnProperty.call(p,key),Object.hasOwn(p,key),Object.getOwnPropertyDescriptor(p,key)!==undefined);", "true true true\n"];
        yield return ["number-setter-only-own-presence", "const p:any=Number.prototype;const key='held';Object.defineProperty(p,key,{set(v:number){},configurable:true});console.log(Object.prototype.hasOwnProperty.call(p,key),Object.hasOwn(p,key),Object.getOwnPropertyDescriptor(p,key)!==undefined);", "true true true\n"];
        yield return ["number-null-data-own-presence", "const p:any=Number.prototype;const key='held';Object.defineProperty(p,key,{value:null,configurable:true});console.log(Object.prototype.hasOwnProperty.call(p,key),Object.hasOwn(p,key),Object.getOwnPropertyDescriptor(p,key)!==undefined);", "true true true\n"];
        yield return ["number-getter-not-invoked", "const p:any=Number.prototype;let calls=0;Object.defineProperty(p,'held',{get(){calls++;throw new Error('unexpected')},configurable:true});console.log(Object.prototype.hasOwnProperty.call(p,'held'),Object.hasOwn(p,'held'),calls);", "true true 0\n"];
        yield return ["boolean-getter-not-invoked", "const p:any=Boolean.prototype;let calls=0;Object.defineProperty(p,'held',{get(){calls++;throw new Error('unexpected')},configurable:true});console.log(Object.prototype.hasOwnProperty.call(p,'held'),Object.hasOwn(p,'held'),calls);", "true true 0\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OwnPresenceDoesNotReadThePropertyValue(string name, string source, string expected, ExecutionMode mode)
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


