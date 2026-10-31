using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedClassPrototypeReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-typed-prototype", """
            class C { value = "instance"; }
            const typed: C = C.prototype;
            const dynamic: any = typed;
            console.log(typed.value === undefined, dynamic.value === undefined);
            dynamic.value = "prototype";
            console.log(typed.value, dynamic.value);
            """, "true true\nprototype prototype\n"];
        yield return ["primitive-field-prototypes", """
            class C{n=2;b=true;s='instance';}const p:C=C.prototype,d:any=p;console.log(p.n===undefined,p.b===undefined,p.s===undefined);d.n=7;d.b=false;d.s='prototype';console.log(p.n,p.b,p.s,new C().n,new C().b,new C().s);
            """, "true true true\n7 false prototype 2 true instance\n"];
        yield return ["typed-parameter", """
            class C{value='instance';}function read(value:C){return value.value;}const p:any=C.prototype;console.log(read(p)===undefined);p.value='prototype';console.log(read(p),read(new C()));
            """, "true\nprototype instance\n"];
        yield return ["record-held-prototype", """
            class C{value='instance';}const holder:{item:C}={item:C.prototype};const p:any=holder.item;console.log(holder.item.value===undefined);p.value='prototype';console.log(holder.item.value);
            """, "true\nprototype\n"];
        yield return ["returned-prototype", """
            class C{value='instance';}function owner():C{return C.prototype;}const p:any=owner();console.log(owner().value===undefined);p.value='prototype';console.log(owner().value);
            """, "true\nprototype\n"];
        yield return ["inherited-prototype", """
            class B{value='base';}class C extends B{}const p:B=C.prototype,d:any=p;console.log(p.value===undefined);d.value='prototype';console.log(p.value,new C().value);
            """, "true\nprototype base\n"];
        yield return ["generic-prototype", """
            class C<T>{value:T;constructor(value:T){this.value=value;}}const p:C<number>=C.prototype,d:any=p;console.log(p.value===undefined);d.value=7;console.log(p.value,new C(3).value);
            """, "true\n7 3\n"];
        yield return ["optional-and-indexed", """
            class C{value='instance';}const p:C=C.prototype,d:any=p;console.log(p?.value===undefined,p['value']===undefined);d.value='prototype';console.log(p?.value,p['value']);
            """, "true true\nprototype prototype\n"];
        yield return ["prototype-as-method-receiver", """
            class C{value='instance';read(){return this.value;}}const p:C=C.prototype,d:any=p;console.log(p.read()===undefined);d.value='prototype';console.log(p.read(),new C().read());
            """, "true\nprototype instance\n"];
        yield return ["live-own-accessor", """
            class C{value='instance';}const p:C=C.prototype;let reads=0;Object.defineProperty(p,'value',{get(){reads++;return 'getter';},configurable:true});console.log(p.value,reads);delete (p as any).value;console.log(p.value===undefined,reads);
            """, "getter 1\ntrue 1\n"];
        yield return ["live-parent-property", """
            class C{value='instance';}const p:C=C.prototype;Object.setPrototypeOf(p,{value:'parent'});console.log(p.value,new C().value);
            """, "parent instance\n"];
        yield return ["ordinary-instance-fast-path", """
            class C{value='instance';n=3;flag=true;}const c=new C();function read(value:C){return value.value;}console.log(c.value,c.n,c.flag,read(c));
            """, "instance 3 true instance\n"];
        yield return ["structural-value-control", """
            class C{value='instance';}const value:C={value:'structural'};console.log(value.value,new C().value);
            """, "structural instance\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void TypedPrototypeFieldsObserveGuestProperties(string name, string source, string expected, ExecutionMode mode)
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
