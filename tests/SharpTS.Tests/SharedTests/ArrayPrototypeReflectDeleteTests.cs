using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayPrototypeReflectDeleteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["configurable-custom", "const p:any=Array.prototype;Object.defineProperty(p,'probe',{value:3,configurable:true});console.log(Reflect.deleteProperty(p,'probe'),Object.hasOwn(p,'probe'));", "true false\n"];
        yield return ["nonconfigurable-custom", "const p:any=Array.prototype;Object.defineProperty(p,'probe',{value:3,configurable:false});console.log(Reflect.deleteProperty(p,'probe'),Object.hasOwn(p,'probe'),p.probe);", "false true 3\n"];
        yield return ["missing-control", "console.log(Reflect.deleteProperty(Array.prototype,'absent'));", "true\n"];
        yield return ["coercion-once", "const p:any=Array.prototype;Object.defineProperty(p,'probe',{value:3,configurable:true});let calls=0;const key:any={toString(){calls++;return 'probe'}};console.log(Reflect.deleteProperty(p,key),Object.hasOwn(p,'probe'),calls);", "true false 1\n"];
        yield return ["getter-not-invoked", "const p:any=Array.prototype;let calls=0;Object.defineProperty(p,'probe',{get(){calls++;return 3},configurable:true});console.log(Reflect.deleteProperty(p,'probe'),Object.hasOwn(p,'probe'),calls);", "true false 0\n"];
        yield return ["nonconfigurable-length", "const p:any=Array.prototype;console.log(Reflect.deleteProperty(p,'length'),Object.hasOwn(p,'length'),p.length);", "false true 0\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReflectDeleteUsesOwnedDescriptor(string name, string source, string expected, ExecutionMode mode)
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

