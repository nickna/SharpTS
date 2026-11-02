using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectPrototypeReflectDeleteTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["root-delete-data", "const p:any=Object.prototype;Object.defineProperty(p,'probe',{value:7,configurable:true});console.log(Reflect.deleteProperty(p,'probe'),Object.prototype.hasOwnProperty.call(p,'probe'));\n", "true false\n"];
        yield return ["root-refuse-nonconfigurable", "const p:any=Object.prototype;Object.defineProperty(p,'probe',{value:7,configurable:false});console.log(Reflect.deleteProperty(p,'probe'),p.probe);\n", "false 7\n"];
        yield return ["root-delete-constructor", "const p:any=Object.prototype;console.log(Reflect.deleteProperty(p,'constructor'),Object.prototype.hasOwnProperty.call(p,'constructor'));\n", "true false\n"];
        yield return ["root-delete-toString", "const p:any=Object.prototype;console.log(Reflect.deleteProperty(p,'toString'),Object.prototype.hasOwnProperty.call(p,'toString'),typeof p.toString);\n", "true false undefined\n"];
        yield return ["root-accessor-no-getter", "const p:any=Object.prototype;let gets=0;Object.defineProperty(p,'probe',{get(){gets++;return 7},configurable:true});console.log(Reflect.deleteProperty(p,'probe'),Object.prototype.hasOwnProperty.call(p,'probe'),gets);\n", "true false 0\n"];
        yield return ["root-sealed-refusal", "const p:any=Object.prototype;Object.defineProperty(p,'probe',{value:7,configurable:true});Object.seal(p);console.log(Reflect.deleteProperty(p,'probe'),Reflect.deleteProperty(p,'constructor'),Object.prototype.hasOwnProperty.call(p,'probe'),Object.prototype.hasOwnProperty.call(p,'constructor'));\n", "false false true true\n"];
        yield return ["root-frozen-refusal", "const p:any=Object.prototype;Object.defineProperty(p,'probe',{value:7,configurable:true});Object.freeze(p);console.log(Reflect.deleteProperty(p,'probe'),p.probe);\n", "false 7\n"];
        yield return ["root-missing-key", "const p:any=Object.prototype;console.log(Reflect.deleteProperty(p,'missingProbe'),Object.prototype.hasOwnProperty.call(p,'missingProbe'));\n", "true false\n"];
        yield return ["root-key-once", "const p:any=Object.prototype,log:string[]=[];Object.defineProperty(p,'probe',{value:7,configurable:true});const key:any={[Symbol.toPrimitive](hint:string){log.push('key:'+hint);return 'probe'}};console.log(Reflect.deleteProperty(p,key),Object.prototype.hasOwnProperty.call(p,'probe'),log.join(','));\n", "true false key:string\n"];
        yield return ["root-prevented-tombstone", "const p:any=Object.prototype;Object.preventExtensions(p);console.log(Reflect.deleteProperty(p,'constructor'),Reflect.defineProperty(p,'constructor',{value:7}),Object.prototype.hasOwnProperty.call(p,'constructor'));\n", "true false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RootDeletionUsesItsActualOwnedDescriptorAndTombstone(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
