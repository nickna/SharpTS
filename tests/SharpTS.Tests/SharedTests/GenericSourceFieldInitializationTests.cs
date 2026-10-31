using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericSourceFieldInitializationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", """
            class C<T>{payload:T;constructor(payload:T){this.payload=payload;}}
            const value=new C<string[]>([]),raw:any={};
            console.log(Reflect.set(value,'payload',raw),Object.is(value.payload,raw));
            """, "true true\n"];
        yield return ["array-identity", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}read():T{return this.value;}write(value:any):T{this.value=value;return this.value;}}
            const value=new Slot<string[]>([]),next:any=['c'];console.log(Reflect.set(value,'value',next),Object.is(value.value,next),value.value[0]);
            """, "true true c\n"];
        yield return ["numeric-transitions", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}read():T{return this.value;}write(value:any):T{this.value=value;return this.value;}}
            const value=new Slot<number>(1),raw:any={};console.log(Reflect.set(value,'value',raw),Object.is(value.value,raw),Object.is(value.read(),raw));console.log(Reflect.set(value,'value',7),value.value,value.read());
            """, "true true true\ntrue 7 7\n"];
        yield return ["method-direct-write", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}read():T{return this.value;}write(value:any):T{this.value=value;return this.value;}}
            const value=new Slot<number>(1),raw:any={};console.log(Object.is(value.write(raw),raw),Object.is(value.value,raw),Object.is(value.read(),raw));
            """, "true true true\n"];
        yield return ["generic-local-and-closure", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}capture(){const old:T=this.value;return ()=>old;}}const value=new Slot<number>(1),raw:any={};Reflect.set(value,'value',raw);console.log(Object.is(value.capture()(),raw));
            """, "true\n"];
        yield return ["inherited-closed-owner", """
            class Base<T>{value:T;constructor(value:T){this.value=value;}}class Middle<U>extends Base<U[]>{}class Leaf extends Middle<string>{}const value=new Leaf(['a']),other=new Leaf(['z']),raw:any={};console.log(Reflect.set(value,'value',raw),Object.is(value.value,raw),other.value[0]);
            """, "true true z\n"];
        yield return ["readonly-parameter-property", """
            class Slot<T>{constructor(public readonly value:T){}}const value=new Slot<number>(1),raw:any={};console.log(Reflect.set(value,'value',raw),Object.is(value.value,raw));
            """, "true true\n"];
        yield return ["partial-and-frozen", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}read():T{return this.value;}write(value:any):T{this.value=value;return this.value;}}
            const value=new Slot<number>(1),raw:any={};Reflect.set(value,'value',raw);Object.defineProperty(value,'value',{enumerable:false});console.log(Object.is(value.value,raw),Object.getOwnPropertyDescriptor(value,'value')!.enumerable);Object.freeze(value);console.log(Reflect.set(value,'value',7),Object.is(value.value,raw));
            """, "true false\nfalse true\n"];
        yield return ["async-closure", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}async capture(){await Promise.resolve();const old:T=this.value;return ()=>old;}}async function run(){const value=new Slot<number>(1),raw:any={};Reflect.set(value,'value',raw);const read=await value.capture();console.log(Object.is(read(),raw));}run();
            """, "true\n"];
        yield return ["string-nullish", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}read():T{return this.value;}write(value:any):T{this.value=value;return this.value;}}
            const value=new Slot<string>('a');console.log(Reflect.set(value,'value',undefined),value.value===undefined,typeof value.read());console.log(Reflect.set(value,'value',null),Object.is(value.value,null));
            """, "true true undefined\ntrue true\n"];
        yield return ["omitted-field-own-descriptor-and-order", """
            class Slot<T>{before=1;value:T;after=2;constructor(value:T){const d:any=Object.getOwnPropertyDescriptor(this,'value');console.log(Object.keys(this).join(','),d.value===undefined,d.writable,d.enumerable,d.configurable);this.value=value;}}console.log(new Slot(7).value);
            """, "before,value,after true true true true\n7\n"];
        yield return ["generic-class-expression", """
            const Slot=class<T>{value:T;constructor(value:T){this.value=value;}};const value=new Slot<number>(1),raw:any={};console.log(Reflect.set(value,'value',raw),Object.is(value.value,raw));
            """, "true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OmittedSourceFieldsExistBeforeConstructorAssignments(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
