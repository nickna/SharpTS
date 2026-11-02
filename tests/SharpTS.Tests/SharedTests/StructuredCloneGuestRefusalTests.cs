using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class StructuredCloneGuestRefusalTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["constructor-original", "class Value{n=1;}try{structuredClone(Value)}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["promise-original", "try{structuredClone(Promise.resolve(1))}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["constructor-return-marker", "class Value{n=1;}try{structuredClone(Value);console.log(\"returned\")}catch(e:any){console.log(\"caught\",String(e).includes(\"DataCloneError\"))}", "caught true\n"];
        yield return ["promise-return-marker", "try{structuredClone(Promise.resolve(1));console.log(\"returned\")}catch(e:any){console.log(\"caught\",String(e).includes(\"DataCloneError\"))}", "caught true\n"];
        yield return ["promise-any-alias", "const p:any=Promise.resolve(1);try{structuredClone(p)}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["nested-object-promise", "try{structuredClone({p:Promise.resolve(1)})}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["array-promise", "try{structuredClone([Promise.resolve(1)])}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["map-promise-value", "const map=new Map<any,any>([[\"p\",Promise.resolve(1)]]);try{structuredClone(map)}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["set-promise", "try{structuredClone(new Set<any>([Promise.resolve(1)]))}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["async-promise", "async function value(){return 1}try{structuredClone(value())}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["callable-alias-promise", "const clone=structuredClone;try{clone(Promise.resolve(1))}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["global-value-promise", "try{globalThis.structuredClone(Promise.resolve(1))}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["promise-failure-before-transfer", "const b=new ArrayBuffer(4);try{structuredClone({p:Promise.resolve(1)},{transfer:[b]})}catch(e:any){console.log(e.name,b.byteLength)}", "DataCloneError 4\n"];
        yield return ["memo-prefix-before-promise", "const child:any={n:1};try{structuredClone({left:child,right:child,p:Promise.resolve(1)})}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["constructor-in-object", "class Value{n=1;}try{structuredClone({value:Value})}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["generic-constructor", "class Value<T>{item:T|undefined;}try{structuredClone(Value)}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["class-own-promise", "class Value{p=Promise.resolve(1);}try{structuredClone(new Value())}catch(e:any){console.log(e.name)}", "DataCloneError\n"];
        yield return ["cloneable-promise-shaped-data", "const c:any=structuredClone({name:\"Promise\",constructor:\"data\",then:7});console.log(c.name,c.constructor,c.then,Object.getPrototypeOf(c)===Object.prototype);", "Promise data 7 true\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void GuestOperationRejectsUncloneableValues(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
