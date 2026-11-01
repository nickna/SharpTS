using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayPrototypeCycleTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["typedarray-reverse-cycle", "const view:any=new Uint8Array(1),obj:any={};Object.setPrototypeOf(view,obj);console.log(Reflect.setPrototypeOf(obj,view),Object.getPrototypeOf(obj)===Object.prototype,Object.getPrototypeOf(view)===obj);\n", "false true true\n"];
        yield return ["object-throws", "const view:any=new Uint8Array(1),obj:any={};Object.setPrototypeOf(view,obj);let caught=false;try{Object.setPrototypeOf(obj,view)}catch(e){caught=e instanceof TypeError}console.log(caught,Object.getPrototypeOf(obj)===Object.prototype);\n", "true true\n"];
        yield return ["forward-order", "const view:any=new Uint8Array(1),obj:any={};const before=Object.getPrototypeOf(view);Object.setPrototypeOf(obj,view);console.log(Reflect.setPrototypeOf(view,obj),Object.getPrototypeOf(view)===before,Object.getPrototypeOf(obj)===view);\n", "false true true\n"];
        yield return ["multiple-typed-views", "const first:any=new Uint8Array(1),second:any=new Float64Array(1),obj:any={};Object.setPrototypeOf(first,obj);Object.setPrototypeOf(second,first);console.log(Reflect.setPrototypeOf(obj,second),Object.getPrototypeOf(obj)===Object.prototype);\n", "false true\n"];
        yield return ["ordinary-bridge", "const view:any=new Uint8Array(1),middle:any={},obj:any={};Object.setPrototypeOf(view,middle);Object.setPrototypeOf(middle,obj);console.log(Reflect.setPrototypeOf(obj,view),Object.getPrototypeOf(obj)===Object.prototype);\n", "false true\n"];
        yield return ["named-proto-shadow", "const view:any=new Uint8Array(1),obj:any={};Object.defineProperty(view,'__proto__',{value:null,configurable:true});Object.setPrototypeOf(view,obj);console.log(Reflect.setPrototypeOf(obj,view),Object.getPrototypeOf(view)===obj);\n", "false true\n"];
        yield return ["function-target", "const view:any=new Uint8Array(1);function target(){}const before=Object.getPrototypeOf(target);Object.setPrototypeOf(view,target);console.log(Reflect.setPrototypeOf(target,view),Object.getPrototypeOf(target)===before);\n", "false true\n"];
        yield return ["instance-target", "class C{}const obj:any=new C(),view:any=new Uint8Array(1),before=Object.getPrototypeOf(obj);Object.setPrototypeOf(view,obj);console.log(Reflect.setPrototypeOf(obj,view),Object.getPrototypeOf(obj)===before);\n", "false true\n"];
        yield return ["acyclic-accepted", "const view:any=new Uint8Array(1),obj:any={},tail:any={};Object.setPrototypeOf(view,tail);console.log(Reflect.setPrototypeOf(obj,view),Object.getPrototypeOf(obj)===view,Object.getPrototypeOf(view)===tail);\n", "true true true\n"];
        yield return ["proxy-parent-no-trap", "const view:any=new Uint8Array(1);let calls=0;const proto:any=new Proxy({},{getPrototypeOf(){calls++;return view}});console.log(Reflect.setPrototypeOf(view,proto),Object.getPrototypeOf(view)===proto,calls);\n", "true true 0\n"];
        yield return ["nonextensible-explicit-same-link", "const view:any=new Uint8Array(1),proto:any={};Object.setPrototypeOf(view,proto);const before=Object.getPrototypeOf(view);Object.preventExtensions(view);console.log(Reflect.setPrototypeOf(view,before),Reflect.setPrototypeOf(view,{}),Object.getPrototypeOf(view)===before);\n", "true false true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void OrdinaryPrototypeCyclesStopAtActualTypedArrayLinks(string name, string source, string expected, ExecutionMode mode)
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
