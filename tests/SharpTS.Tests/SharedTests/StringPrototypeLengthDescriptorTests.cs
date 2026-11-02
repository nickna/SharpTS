using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StringPrototypeLengthDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-reflect-delete-length", "const p:any=String.prototype;console.log(Reflect.deleteProperty(p,'length'),Object.hasOwn(p,'length'));", "false true\n"];
        yield return ["string-length-initial-descriptor", "const p:any=String.prototype;const d:any=Object.getOwnPropertyDescriptor(p,'length');console.log(p.length,Object.hasOwn(p,'length'),d?.value,d?.writable,d?.enumerable,d?.configurable);", "0 true 0 false false false\n"];
        yield return ["string-length-value-refusal", "const p:any=String.prototype;console.log(Reflect.defineProperty(p,'length',{value:7}),p.length);", "false 0\n"];
        yield return ["string-length-empty-definition", "const p:any=String.prototype;console.log(Object.defineProperty(p,'length',{})===p,p.length);", "true 0\n"];
        yield return ["string-length-config-refusal", "const p:any=String.prototype;console.log(Reflect.defineProperty(p,'length',{configurable:true}),Object.getOwnPropertyDescriptor(p,'length')?.configurable);", "false false\n"];
        yield return ["string-length-same-value", "const p:any=String.prototype;console.log(Reflect.defineProperty(p,'length',{value:0}),Reflect.defineProperty(p,'length',{writable:false}),p.length);", "true true 0\n"];
        yield return ["array-length-writable-control", "const p:any=Array.prototype;const d:any=Object.getOwnPropertyDescriptor(p,'length');console.log(d.value,d.writable,d.enumerable,d.configurable);", "0 true false false\n"];
        yield return ["string-length-direct-readonly", "console.log(String.prototype.length,typeof String.prototype.length);", "0 number\n"];
        yield return ["string-length-aliased-readonly", "const ctor:any=String;const p:any=ctor.prototype;console.log(p.length,typeof p.length);", "0 number\n"];
        yield return ["string-length-returned-readonly", "function pass(value:any):any{return value}const p:any=pass(String).prototype;console.log(p.length,typeof p.length);", "0 number\n"];
        yield return ["string-length-boxed-readonly", "const p:any=Object.getPrototypeOf(new String('x'));console.log(p.length,typeof p.length);", "0 number\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void InitialStringLengthRetainsItsIntrinsicDescriptor(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
