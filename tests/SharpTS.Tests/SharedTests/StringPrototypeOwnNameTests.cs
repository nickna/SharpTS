using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StringPrototypeOwnNameTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-length-own-keys", "const p:any=String.prototype;console.log(Object.keys(p).includes('length'),Object.getOwnPropertyNames(p).includes('length'));", "false true\n"];
        yield return ["string-length-physical-names-no-get", "const p:any=String.prototype;let calls=0;Object.defineProperty(p,'probe',{get(){calls++;return 7},enumerable:false,configurable:true});const names=Object.getOwnPropertyNames(p);console.log(names.includes('length'),names.includes('probe'),calls);", "true true 0\n"];
        yield return ["string-length-physical-name-deleted", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:7,configurable:true});delete p.probe;const names=Object.getOwnPropertyNames(p);console.log(names.includes('length'),names.includes('probe'));", "true false\n"];
        yield return ["string-length-physical-numeric-order", "const p:any=String.prototype;p[2]=7;p[1]=9;const names=Object.getOwnPropertyNames(p).filter(n=>n==='length'||n==='1'||n==='2');console.log(names.join(','));", "1,2,length\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OwnNamesExposePhysicalStringPrototypeSlots(
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
