using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneClassInstanceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["class-original", "class Value{value=1;}const copy:any=structuredClone(new Value());console.log(copy.value,Object.getPrototypeOf(copy)===Object.prototype);", "1 true\n"];
        yield return ["constructor-not-replayed", "let calls=0;class Value{n=++calls;}const a=new Value();const c:any=structuredClone(a);console.log(c.n,calls,c!==a);", "1 1 true\n"];
        yield return ["prototype-method-omitted", "class Value{n=2;method(){return 3}}const c:any=structuredClone(new Value());console.log(c.n,Object.hasOwn(c,\"method\"),typeof c.method,Object.getPrototypeOf(c)===Object.prototype);", "2 false undefined true\n"];
        yield return ["inherited-fields", "class Base{a=1;}class Value extends Base{b=2;}const c:any=structuredClone(new Value());console.log(c.a,c.b,Object.getPrototypeOf(c)===Object.prototype);", "1 2 true\n"];
        yield return ["nonenumerable-omitted", "class Value{a=1;}const a:any=new Value();Object.defineProperty(a,\"hidden\",{value:9,enumerable:false});const c:any=structuredClone(a);console.log(c.a,Object.hasOwn(c,\"hidden\"));", "1 false\n"];
        yield return ["own-getter-receiver", "class Value{n=4;}const a:any=new Value();let calls=0;Object.defineProperty(a,\"x\",{enumerable:true,get:function(this:any){calls++;return this.n+1}});const c:any=structuredClone(a);console.log(c.x,calls,Object.getOwnPropertyDescriptor(c,\"x\")!.get===undefined);", "5 1 true\n"];
        yield return ["prototype-getter-not-read", "let calls=0;class Value{n=1;get x(){calls++;return 9}}const c:any=structuredClone(new Value());console.log(c.n,Object.hasOwn(c,\"x\"),calls);", "1 false 0\n"];
        yield return ["symbol-key-omitted", "const s=Symbol(\"x\");class Value{n=1;}const a:any=new Value();a[s]=9;const c:any=structuredClone(a);console.log(c.n,Object.getOwnPropertySymbols(c).length);", "1 0\n"];
        yield return ["normalized-descriptor", "class Value{}const a:any=new Value();Object.defineProperty(a,\"x\",{value:7,enumerable:true,writable:false,configurable:false});const c:any=structuredClone(a),d:any=Object.getOwnPropertyDescriptor(c,\"x\");console.log(c.x,d.enumerable,d.writable,d.configurable);", "7 true true true\n"];
        yield return ["self-cycle", "class Value{n=1;self:any=this;}const a=new Value(),c:any=structuredClone(a);console.log(c!==a,c.self===c,c.n);", "true true 1\n"];
        yield return ["shared-class-child", "class Value{n=3;}const a=new Value();const c:any=structuredClone({left:a,right:a});console.log(c.left===c.right,c.left!==a,c.left.n);", "true true 3\n"];
        yield return ["shared-field-child", "class Value{left:any;right:any;constructor(){const a={n:8};this.left=a;this.right=a;}}const c:any=structuredClone(new Value());console.log(c.left===c.right,c.left.n);", "true 8\n"];
        yield return ["class-array-map", "class Value{n=6;}const a=new Value();const c:any=structuredClone({a,list:[a],map:new Map<any,any>([[\"a\",a]])});console.log(c.a===c.list[0],c.a===c.map.get(\"a\"),c.a.n);", "true true 6\n"];
        yield return ["generic-class", "class Value<T>{constructor(public item:T){}}const c:any=structuredClone(new Value<number>(7));console.log(c.item,Object.getPrototypeOf(c)===Object.prototype);", "7 true\n"];
        yield return ["getter-abrupt", "class Value{}const a:any=new Value();let calls=0;Object.defineProperty(a,\"x\",{enumerable:true,get(){calls++;throw new RangeError(\"stop\")}});try{structuredClone(a)}catch(e:any){console.log(e.name,e.message,calls)}", "RangeError stop 1\n"];
        yield return ["getter-mutates-next-descriptor", "class Value{}const a:any=new Value();Object.defineProperty(a,\"first\",{enumerable:true,get(){Object.defineProperty(a,\"later\",{enumerable:false});return 1}});Object.defineProperty(a,\"later\",{value:2,enumerable:true,configurable:true});const c:any=structuredClone(a);console.log(c.first,Object.hasOwn(c,\"later\"));", "1 true\n"];
        yield return ["own-function-rejected", "class Value{fn=()=>1;}try{structuredClone(new Value())}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["constructor-rejected", "class Value{n=1;}try{structuredClone(Value)}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["promise-rejected", "try{structuredClone(Promise.resolve(1))}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["private-field-omitted", "class Value{#secret=9;visible=2;read(){return this.#secret}}const c:any=structuredClone(new Value());console.log(c.visible,Object.keys(c).join(\",\"),typeof c.read);", "2 visible undefined\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void EnumerableOwnDataClonesToPlainObjects(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
