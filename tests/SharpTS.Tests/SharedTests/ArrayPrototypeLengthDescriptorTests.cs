using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayPrototypeLengthDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["prevented-partial-update", "const p:any=Array.prototype;Object.preventExtensions(p);Object.defineProperty(p,'length',{value:3});const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(p.length,d.value,d.writable,d.enumerable,d.configurable,Object.isExtensible(p));", "3 3 true false false false\n"];
        yield return ["initial-descriptor", "const d=Object.getOwnPropertyDescriptor(Array.prototype,'length')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "0 true false false\n"];
        yield return ["empty-partial-definition", "const p:any=Array.prototype;Object.defineProperty(p,'length',{});const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "0 true false false\n"];
        yield return ["reflect-enumerable-refused", "const p:any=Array.prototype;console.log(Reflect.defineProperty(p,'length',{enumerable:true}));const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "false\n0 true false false\n"];
        yield return ["reflect-configurable-refused", "const p:any=Array.prototype;console.log(Reflect.defineProperty(p,'length',{configurable:true}));const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "false\n0 true false false\n"];
        yield return ["nonenumerable-keys", "console.log(Object.keys(Array.prototype).includes('length'));", "false\n"];
        yield return ["ordinary-length-control", "const p:any={length:0};Object.preventExtensions(p);Object.defineProperty(p,'length',{value:3});const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "3 true true true\n"];
        yield return ["explicit-readonly-update", "const p:any=Array.prototype;Object.defineProperty(p,'length',{writable:false});console.log(Reflect.defineProperty(p,'length',{value:3}));const d=Object.getOwnPropertyDescriptor(p,'length')!;console.log(d.value,d.writable,d.enumerable,d.configurable);", "false\n0 false false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void LengthDescriptorRetainsIntrinsicAttributes(string name, string source, string expected, ExecutionMode mode)
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
