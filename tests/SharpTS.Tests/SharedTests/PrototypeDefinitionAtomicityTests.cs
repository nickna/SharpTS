using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PrototypeDefinitionAtomicityTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["root-prevent-delete-object", "const p:any=Object.prototype;Object.preventExtensions(p);delete p.constructor;let caught=false;let result:any;try{result=Object.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,result,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "true undefined false true true false\n"];
        yield return ["root-delete-prevent-object", "const p:any=Object.prototype;delete p.constructor;Object.preventExtensions(p);let caught=false;let result:any;try{result=Object.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,result,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "true undefined false true true false\n"];
        yield return ["root-prevent-delete-reflect", "const p:any=Object.prototype;Object.preventExtensions(p);delete p.constructor;let caught=false;let result:any;try{result=Reflect.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,result,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "false false false true true false\n"];
        yield return ["root-delete-prevent-reflect", "const p:any=Object.prototype;delete p.constructor;Object.preventExtensions(p);let caught=false;let result:any;try{result=Reflect.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,result,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "false false false true true false\n"];
        yield return ["root-successful-recreation", "const p:any=Object.prototype;delete p.constructor;const value={};Object.defineProperty(p,'constructor',{value,writable:true,configurable:true});const d=Object.getOwnPropertyDescriptor(p,'constructor')!;console.log(Object.prototype.hasOwnProperty.call(p,'constructor'),p.constructor===value,d.value===value,d.writable,d.enumerable,d.configurable);", "true true true true false true\n"];
        yield return ["function-prevent-delete-object", "const p:any=Function.prototype;Object.preventExtensions(p);delete p.constructor;let caught=false;try{Object.defineProperty(p,'constructor',{value:9});}catch(e){caught=e instanceof TypeError;}console.log(caught,Object.prototype.hasOwnProperty.call(p,'constructor'),Object.getOwnPropertyDescriptor(p,'constructor')===undefined,p.constructor===undefined,Object.isExtensible(p));", "true false true false false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RejectedDefinitionPreservesDeletedIntrinsic(string name, string source, string expected, ExecutionMode mode)
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
