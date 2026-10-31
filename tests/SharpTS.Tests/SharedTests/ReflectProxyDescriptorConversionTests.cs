using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReflectProxyDescriptorConversionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", """
            const state={trace:'',traps:0},marker={};
            const target:any=new Proxy({},{defineProperty(object:any,key:any,descriptor:any){state.trace+='t';state.traps++;return true;}});
            const description:any={get value(){state.trace+='v';return 7;},configurable:true};
            console.log(Reflect.defineProperty(target,'value',description),state.trace,state.traps);
            state.trace='';state.traps=0;
            try{console.log('returned',Reflect.defineProperty(target,'invalid',{get:1} as any));}catch(error){console.log('type-error',error instanceof TypeError);}
            console.log(state.trace,state.traps);
            state.trace='';state.traps=0;
            const throws:any={get value(){state.trace+='v';throw marker;},configurable:true};
            try{console.log('returned',Reflect.defineProperty(target,'throws',throws));}catch(error){console.log('marker',error===marker);}
            console.log(state.trace,state.traps);
            """, "true vt 1\ntype-error true\n 0\nmarker true\nv 0\n"];
        yield return ["conversion-before-trap-get", """
            let trace='';const symbol=Symbol('key');
            const key:any={[Symbol.toPrimitive](hint:string){trace+='k'+hint;return symbol;}};
            const description:any={get enumerable(){trace+='e';return false;},get configurable(){trace+='c';return true;},get value(){trace+='v';return 7;},get writable(){trace+='w';return false;}};
            const handler:any={get defineProperty(){trace+='g';return function(target:any,property:any,descriptor:any){trace+='t';console.log(property===symbol,descriptor===description,Object.getPrototypeOf(descriptor)===Object.prototype,Object.keys(descriptor).join(','),descriptor.value);return true;};}};
            console.log(Reflect.defineProperty(new Proxy({},handler),key,description),trace);
            """, "true false true value,writable,enumerable,configurable 7\ntrue kstringecvwgt\n"];
        yield return ["trapless-nested-and-false", """
            let reads=0;const base:any={};const nested:any=new Proxy(new Proxy(base,{}),{});
            const description:any={get value(){reads++;return 7;},configurable:true};
            console.log(Reflect.defineProperty(nested,'x',description),base.x,reads);
            Object.preventExtensions(base);console.log(Reflect.defineProperty(nested,'y',description),reads);
            let traps=0;const refused:any=new Proxy({},{defineProperty(){traps++;return false;}});
            console.log(Reflect.defineProperty(refused,'x',description),reads,traps);
            """, "true 7 1\nfalse 2\nfalse 3 1\n"];
        yield return ["inherited-getter-and-omitted-fields", """
            let reads=0;
            const description:any=Object.create({get value(){reads++;return 7;}});
            const target:any=new Proxy({},{defineProperty(target:any,key:any,descriptor:any){console.log(Object.keys(descriptor).join(','),descriptor.value,'writable' in descriptor,'configurable' in descriptor);return true;}});
            console.log(Reflect.defineProperty(target,'x',description),reads);
            """, "value 7 false false\ntrue 1\n"];
        yield return ["revoked-and-invalid-descriptor", """
            let reads=0;const proxy=Proxy.revocable({},{});proxy.revoke();
            const description:any={get value(){reads++;return 7;},configurable:true};
            try{Reflect.defineProperty(proxy.proxy,'x',description);}catch(error){console.log(error instanceof TypeError,reads);}
            let traps=0;const target=new Proxy({},{defineProperty(){traps++;return true;}});
            for(const invalid of [null,undefined,1,'x',true]){
                try{Reflect.defineProperty(target,'x',invalid as any);console.log('accepted');}catch(error){console.log(error instanceof TypeError,traps);}
            }
            """, "true 1\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\n"];
    }

    public static IEnumerable<object[]> Modes()=>Cases().SelectMany(row=>new[]{ExecutionMode.Interpreted,ExecutionMode.Compiled}
        .Select(mode=>new object[]{row[0],row[1],row[2],mode}));

    [Theory, MemberData(nameof(Modes))]
    public void ReflectConvertsBeforeProxyDispatch(string name, string source, string expected,ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if(mode==ExecutionMode.Compiled){var(errors,output)=TestHarness.CompileVerifyAndRun(source);Assert.Empty(errors);Assert.Equal(expected,output);}
        else Assert.Equal(expected,TestHarness.Run(source,mode));
    }
}
