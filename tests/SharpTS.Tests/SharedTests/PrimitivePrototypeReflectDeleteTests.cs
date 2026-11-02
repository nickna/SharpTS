using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitivePrototypeReflectDeleteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["string-reflect-delete-existing", "const p:any=String.prototype;console.log(Reflect.deleteProperty(p,'toString'),Object.hasOwn(p,'toString'));", "true false\n"];
        yield return ["number-reflect-delete-existing", "const p:any=Number.prototype;console.log(Reflect.deleteProperty(p,'toString'),Object.hasOwn(p,'toString'));", "true false\n"];
        yield return ["boolean-reflect-delete-existing", "const p:any=Boolean.prototype;console.log(Reflect.deleteProperty(p,'toString'),Object.hasOwn(p,'toString'));", "true false\n"];
        yield return ["boolean-reflect-delete-missing", "const p:any=Boolean.prototype;console.log(Reflect.deleteProperty(p,'missing'),Object.hasOwn(p,'missing'));", "true false\n"];
        yield return ["string-reflect-delete-nonconfigurable", "const p:any=String.prototype;Object.defineProperty(p,'probe',{value:7,configurable:false});console.log(Reflect.deleteProperty(p,'probe'),Object.hasOwn(p,'probe'),p.probe);", "false true 7\n"];
        yield return ["string-reflect-delete-sealed", "const p:any=String.prototype;const old=p.toString;Object.seal(p);console.log(Reflect.deleteProperty(p,'toString'),Object.hasOwn(p,'toString'),p.toString===old);", "false true true\n"];
        yield return ["number-reflect-delete-nonconfigurable", "const p:any=Number.prototype;Object.defineProperty(p,'probe',{value:7,configurable:false});console.log(Reflect.deleteProperty(p,'probe'),Object.hasOwn(p,'probe'),p.probe);", "false true 7\n"];
        yield return ["number-reflect-delete-sealed", "const p:any=Number.prototype;const old=p.toString;Object.seal(p);console.log(Reflect.deleteProperty(p,'toString'),Object.hasOwn(p,'toString'),p.toString===old);", "false true true\n"];
        yield return ["boolean-reflect-delete-nonconfigurable", "const p:any=Boolean.prototype;Object.defineProperty(p,'probe',{value:7,configurable:false});console.log(Reflect.deleteProperty(p,'probe'),Object.hasOwn(p,'probe'),p.probe);", "false true 7\n"];
        yield return ["boolean-reflect-delete-sealed", "const p:any=Boolean.prototype;const old=p.toString;Object.seal(p);console.log(Reflect.deleteProperty(p,'toString'),Object.hasOwn(p,'toString'),p.toString===old);", "false true true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReflectDeletionUsesTheOwnedPrototypeStore(
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
