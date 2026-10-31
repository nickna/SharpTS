using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GenericSourceFieldArgumentRepresentationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-with-explicit-initializer-and-without-reflect", """
            class Slot<T>{value:T=undefined as any;write(value:any){this.value=value;}}
            function forward(input:number):any{return input;}
            const value=new Slot<number>(),raw:any={};value.write(raw);
            console.log(Object.is(forward(value.value),raw));
            """, "true\n"];
        yield return ["numeric-local-and-forwarding", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}read():T{return this.value;}write(value:any):T{this.value=value;return this.value;}}
            const value=new Slot<number>(1),raw:any={};Reflect.set(value,'value',raw);const old=value.value;function forward(input:number):any{return input;}console.log(typeof old,Object.is(old,raw),Object.is(forward(value.value),raw));
            """, "object true true\n"];
        yield return ["constrained-number-bigint", """
            class Slot<T extends number>{value:T;constructor(value:T){this.value=value;}readLocal(){const old:T=this.value;return typeof old;}}const value=new Slot<number>(1);Reflect.set(value,'value',31n);console.log(typeof value.value,value.readLocal(),Object.is(value.value,31n));
            """, "bigint bigint true\n"];
        yield return ["source-method-return-then-numeric-argument", """
            class Slot<T>{value:T=undefined as any;write(value:any){this.value=value;}read():T{return this.value;}}function forward(value:number):any{return value;}const slot=new Slot<number>(),raw:any={};slot.write(raw);console.log(Object.is(forward(slot.read()),raw));
            """, "true\n"];
        yield return ["closure-and-async-source-arguments", """
            class Slot<T>{value:T=undefined as any;write(value:any){this.value=value;}}function run(){const slot=new Slot<number>(),raw:any={};slot.write(raw);const forward=(value:number):any=>value;async function later(value:number):Promise<any>{await Promise.resolve();return value;}console.log(Object.is(forward(slot.value),raw));later(slot.value).then(value=>console.log(Object.is(value,raw)));}run();
            """, "true\ntrue\n"];
        yield return ["overwritten-source-target", """
            class Slot<T>{value:T=undefined as any;write(value:any){this.value=value;}}function first(value:number):any{return value;}function second(value:number):any{return value;}let forward=first;forward=second;const slot=new Slot<number>(),raw:any={};slot.write(raw);console.log(Object.is(forward(slot.value),raw));
            """, "true\n"];
        yield return ["member-and-index-source-targets", """
            class Slot<T>{value:T=undefined as any;write(value:any){this.value=value;}}function forward(value:number):any{return value;}const source={forward},slot=new Slot<number>(),raw:any={};slot.write(raw);console.log(Object.is(source.forward(slot.value),raw),Object.is(source['forward'](slot.value),raw));
            """, "true true\n"];
        yield return ["inherited-closed-owner-argument", """
            class Base<T>{value:T=undefined as any;write(value:any){this.value=value;}}class Middle<U>extends Base<U>{}class Leaf extends Middle<number>{}function forward(value:number):any{return value;}const slot=new Leaf(),raw:any={};slot.write(raw);console.log(Object.is(forward(slot.value),raw));
            """, "true\n"];
        yield return ["normally-numeric-callers", """
            class Slot<T>{value:T;constructor(value:T){this.value=value;}}function forward(value:number):number{return value*2;}const slot=new Slot(3);console.log(forward(slot.value),forward(4));
            """, "6 8\n"];
        yield return ["literal-index-field-producer", """
            class Slot<T>{value:T=undefined as any;write(value:any){this.value=value;}}function forward(value:number):any{return value;}const slot=new Slot<number>(),raw:any={};slot.write(raw);console.log(Object.is(forward(slot['value']),raw));
            """, "true\n"];
        yield return ["asserted-field-producer", """
            class Slot<T>{value:T=undefined as any;write(value:any){this.value=value;}}function forward(value:number):any{return value;}const slot=new Slot<number>(),raw:any={};slot.write(raw);console.log(Object.is(forward((slot.value as number)),raw));
            """, "true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void SourceGenericFieldResultsPreserveActualArgumentRepresentation(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
