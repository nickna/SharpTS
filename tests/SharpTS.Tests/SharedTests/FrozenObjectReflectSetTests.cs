using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FrozenObjectReflectSetTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const O:any=Object;O.existing=1;Object.freeze(O);O.added=2;console.log('sloppy',Object.hasOwn(O,'added'),O.added);console.log(Reflect.set(O,'added',3));\n", "sloppy false undefined\nfalse\n"];
        yield return ["frozen-existing", "const O:any=Object;O.extra=1;Object.freeze(O);console.log(Reflect.set(O,'extra',2),O.extra,Reflect.set(O,'keys',()=>[]));", "false 1 false\n"];
        yield return ["sealed-writable", "const O:any=Object;O.extra=1;Object.seal(O);console.log(Reflect.set(O,'extra',2),O.extra,Reflect.set(O,'added',3),Object.getOwnPropertyDescriptor(O,'extra')!.configurable,Object.getOwnPropertyDescriptor(O,'extra')!.writable);", "true 2 false false true\n"];
        yield return ["nonextensible-existing-native", "const O:any=Object;Object.preventExtensions(O);const replacement=()=>7;console.log(Reflect.set(O,'keys',replacement),O.keys===replacement,Reflect.set(O,'added',3),Object.hasOwn(O,'added'));", "true true false false\n"];
        yield return ["setter-throw-propagates", "const O:any=Object,marker={};Object.defineProperty(O,'value',{set(value:any){throw marker;},configurable:true});Object.freeze(O);try{Reflect.set(O,'value',7);}catch(error){console.log(error===marker);}", "true\n"];
        yield return ["distinct-receiver", "const O:any=Object;O.extra=1;Object.freeze(O);const receiver:any={};console.log(Reflect.set(O,'extra',2,receiver),Reflect.set(O,'added',3,receiver),receiver.added,Object.hasOwn(O,'added'));", "false true 3 false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReportsOrdinaryDefinitionRefusalsWithoutSwallowingSetterThrows(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
