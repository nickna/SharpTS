using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

// The interpreter source-class Reflect.set success boundary is tracked separately.
public sealed class SourceClassDataDefinitionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["inherited-closed-generic-fields", """
            class Base<T> { value:number=1; flag:boolean=true; payload:T; constructor(payload:T){this.payload=payload;} }
            class Middle<U> extends Base<U[]> {}
            class Leaf extends Middle<string> { own=5; }
            const first=new Leaf(['a']),other=new Leaf(['b']);
            console.log(Reflect.set(first,'value',null),Reflect.set(first,'flag',undefined),first.value,first.flag,other.value,other.flag);
            console.log(Reflect.set(first,'value',7),Reflect.set(first,'flag',false),first.value,first.flag);
            console.log(first.payload[0],other.payload[0]);
            """, "true true null undefined 1 true\ntrue true 7 false\na b\n"];
        yield return ["define-data-bypasses-prototype-setter", """
            let calls=0;
            class C { value:number=1; }
            Object.defineProperty(C.prototype,'extra',{set(value:any){calls++;},configurable:true});
            const value:any=new C();
            Object.defineProperty(value,'extra',{value:7,writable:true,enumerable:true,configurable:true});
            console.log(value.extra,calls,Object.getOwnPropertyDescriptor(value,'extra')!.value);
            const raw:any={};Object.defineProperty(value,'value',{value:raw});
            console.log(Object.is(value.value,raw),Object.is(Object.getOwnPropertyDescriptor(value,'value')!.value,raw),calls);
            """, "7 0 7\ntrue true 0\n"];
        yield return ["partial-descriptor-preserves-current-value", """
            class C { value:number=1; flag:boolean=true; }
            const value=new C();Reflect.set(value,'value',null);Reflect.set(value,'flag',undefined);
            Object.defineProperty(value,'value',{enumerable:false});Object.defineProperty(value,'flag',{configurable:false});
            const number:any=Object.getOwnPropertyDescriptor(value,'value'),flag:any=Object.getOwnPropertyDescriptor(value,'flag');
            console.log(value.value,value.flag,number.value,number.enumerable,flag.value,flag.configurable);
            console.log(Reflect.set(value,'value',7),Reflect.set(value,'flag',false),value.value,value.flag);
            """, "null undefined null false undefined false\ntrue true 7 false\n"];
        yield return ["rejected-and-frozen-data-writes", """
            class C { value:number=1; flag:boolean=true; }
            const value=new C();Object.defineProperty(value,'value',{writable:false});
            console.log(Reflect.set(value,'value',7),value.value,Object.getOwnPropertyDescriptor(value,'value')!.value);
            Object.freeze(value);console.log(Reflect.set(value,'flag',false),value.flag);
            try{Object.defineProperty(value,'value',{value:9});}catch(error){console.log(error instanceof TypeError);}
            console.log(value.value,value.flag);
            """, "false 1 1\nfalse true\ntrue\n1 true\n"];
        yield return ["class-prototype-data-does-not-touch-instance-slot", """
            class C { value:number=1; }
            Object.defineProperty(C.prototype,'value',{value:9,writable:true,enumerable:true,configurable:true});
            console.log((C.prototype as any).value,Object.getOwnPropertyDescriptor(C.prototype,'value')!.value,new C().value);
            Reflect.set(C.prototype,'value',10);console.log((C.prototype as any).value,new C().value);
            """, "9 9 1\n10 1\n"];
        yield return ["proxy-and-array-existing-definitions", """
            class C { value:number=1; }
            const target=new C();let calls=0;const proxy=new Proxy(target,{defineProperty(t,key,descriptor){calls++;return Reflect.defineProperty(t,key,descriptor);}});
            console.log(Reflect.set(proxy,'value',7),target.value,calls);
            const array:any[]=[1];console.log(Reflect.set(array,'0',9),array[0]);
            const key=Symbol('s');Object.defineProperty(target,key,{value:11});console.log((target as any)[key],Object.getOwnPropertyDescriptor(target,key)!.value);
            """, "true 7 1\ntrue 9\n11 11\n"];
        yield return ["same-name-own-shadow", """
            class Base<T>{value:any=1;constructor(public payload:T){}}
            class Child extends Base<number>{value:any='child';}
            const first=new Child(3),second=new Child(4);const raw:any={};
            console.log(Reflect.set(first,'value',raw),Object.is(first.value,raw),Object.is(Reflect.get(first,'value'),raw),second.value);
            console.log(Reflect.set(first,'payload',null),first.payload,second.payload);
            """, "true true true child\ntrue null 4\n"];
        yield return ["derived-accessor-base-own-data", """
            let calls=0;
            class Base { value:number=1; }
            const Parent:any=Base;
            class Child extends Parent { get value():number{calls++;return 99;} }
            const child:any=new Child();
            console.log(Reflect.defineProperty(child,'value',{value:7}),child.value,calls);
            console.log(Object.getOwnPropertyDescriptor(child,'value')!.value);
            """, "true 7 0\n7\n"];
        yield return ["prototype-data-replacement-keeps-creation-order", """
            let reads=0;
            class C { first(){} get middle(){reads++;return 1;} last(){} }
            const prototype:any=C.prototype;
            Object.defineProperty(prototype,'first',{value:7});
            Object.defineProperty(prototype,'last',{writable:false});
            console.log(Object.getOwnPropertyNames(prototype).join(','),prototype.first,reads);
            console.log(Reflect.set(prototype,'first',8),prototype.first,Object.getOwnPropertyNames(prototype).join(','),reads);
            """, "constructor,first,middle,last 7 0\ntrue 8 constructor,first,middle,last 0\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CompiledOwnDataDefinitionsWriteRegisteredSourceStorage(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
