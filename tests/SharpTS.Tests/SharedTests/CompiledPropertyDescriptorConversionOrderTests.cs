using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CompiledPropertyDescriptorConversionOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-data-order", """
            const events:string[]=[];const descriptor:any={get enumerable(){events.push("enumerable");return false;},get configurable(){events.push("configurable");return true;},get value(){events.push("value");return 7;},get writable(){events.push("writable");return false;}};const target:any={};Object.defineProperty(target,"value",descriptor);console.log(events.join(","),target.value);
            """, "enumerable,configurable,value,writable 7\n"];
        yield return ["accessor-field-order", """
            const events:string[]=[],getter=()=>7;const descriptor:any={get enumerable(){events.push("e");return false;},get configurable(){events.push("c");return true;},get get(){events.push("g");return getter;},get set(){events.push("s");return undefined;}};const target:any={};Object.defineProperty(target,"x",descriptor);console.log(events.join(","),target.x);
            """, "e,c,g,s 7\n"];
        yield return ["invalid-getter-stops-set", """
            let trace="";const descriptor:any={get enumerable(){trace+="e";return false;},get configurable(){trace+="c";return true;},get get(){trace+="g";return 1;},get set(){trace+="s";return undefined;}};try{Object.defineProperty({},"x",descriptor);}catch(error){console.log(error instanceof TypeError,trace);}
            """, "true ecg\n"];
        yield return ["abrupt-stops-later-fields", """
            let trace="";const marker={},target:any={};const descriptor:any={get enumerable(){trace+="e";return true;},get configurable(){trace+="c";throw marker;},get value(){trace+="v";return 7;},get writable(){trace+="w";return false;}};try{Object.defineProperty(target,"x",descriptor);}catch(error){console.log(error===marker,trace,Object.hasOwn(target,"x"));}
            """, "true ec false\n"];
        yield return ["proxy-descriptor-has-get", """
            const trace:string[]=[];const descriptor:any=new Proxy({enumerable:false,configurable:true,value:7,writable:false},{has(target:any,key:any){trace.push("h"+key);return Reflect.has(target,key);},get(target:any,key:any,receiver:any){trace.push("g"+key);return Reflect.get(target,key,receiver);}});const target:any={};Object.defineProperty(target,"x",descriptor);console.log(trace.join(","),target.x);
            """, "henumerable,genumerable,hconfigurable,gconfigurable,hvalue,gvalue,hwritable,gwritable,hget,hset 7\n"];
        yield return ["earlier-getter-removes-future-field", """
            const descriptor:any={enumerable:true,value:7,writable:true};Object.defineProperty(descriptor,"enumerable",{get(){delete descriptor.value;return true;}});const target:any={};Object.defineProperty(target,"x",descriptor);const result=Object.getOwnPropertyDescriptor(target,"x")!;console.log(result.value===undefined,result.writable,result.enumerable);
            """, "true true true\n"];
        yield return ["mixed-kind-after-all-fields", """
            let trace="";const getter=()=>7;const descriptor:any={get enumerable(){trace+="e";return false;},get configurable(){trace+="c";return true;},get value(){trace+="v";return 7;},get writable(){trace+="w";return true;},get get(){trace+="g";return getter;},get set(){trace+="s";return undefined;}};try{Object.defineProperty({},"x",descriptor);}catch(error){console.log(error instanceof TypeError,trace);}
            """, "true ecvwgs\n"];
        yield return ["inherited-fields-and-create", """
            const trace:string[]=[];const inherited:any={get enumerable(){trace.push("e");return true;},get configurable(){trace.push("c");return true;},get value(){trace.push("v");return 7;},get writable(){trace.push("w");return false;}};const descriptor=Object.create(inherited);const target:any=Object.create(null);Object.defineProperty(target,"x",descriptor);console.log(trace.join(","),target.x,Object.keys(target).join(","));
            """, "e,c,v,w 7 x\n"];
        yield return ["async-context", """
            async function run(){await Promise.resolve();let trace="";const descriptor:any={get enumerable(){trace+="e";return false;},get configurable(){trace+="c";return true;},get value(){trace+="v";return 7;},get writable(){trace+="w";return false;}};const target:any={};Object.defineProperty(target,"x",descriptor);console.log(trace,target.x);}run();
            """, "ecvw 7\n"];
        yield return ["target-before-key-and-descriptor", """
            const events:string[]=[];const key:any={toString(){events.push('key');return 'x';}};const descriptor:any={get value(){events.push('value');return 7;}};for(const target of [null,undefined,1,true,'x',Symbol('s'),1n]){try{Object.defineProperty(target as any,key,descriptor);}catch(error){console.log(error instanceof TypeError,events.length);}}
            """, "true 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\n"];
        yield return ["key-before-descriptor", """
            const events:string[]=[];const key:any={toString(){events.push('key');return 'x';}};const descriptor:any={get enumerable(){events.push('e');return true;},get value(){events.push('v');return 7;}};const target:any={};Object.defineProperty(target,key,descriptor);console.log(events.join(','),target.x);
            """, "key,e,v 7\n"];
        yield return ["array-length-after-conversion", """
            const events:string[]=[];const value:any={valueOf(){events.push('number');return 1;}};const descriptor:any={get configurable(){events.push('c');return false;},get value(){events.push('v');return value;},get writable(){events.push('w');return true;}};const target:any[]=[1,2];Object.defineProperty(target,'length',descriptor);console.log(events.join(','),target.length);
            """, "c,v,w,number,number 1\n"];
        yield return ["symbol-after-conversion", """
            const events:string[]=[],key=Symbol('x');const descriptor:any={get configurable(){events.push('c');return true;},get value(){events.push('v');return 7;},get writable(){events.push('w');return false;}};const target:any={};Object.defineProperty(target,key,descriptor);console.log(events.join(','),target[key]);
            """, "c,v,w 7\n"];
        yield return ["proxy-trap-after-conversion", """
            const events:string[]=[];const target:any=new Proxy({},{get(_target:any,key:any){events.push('trap-get:'+String(key));return undefined;},defineProperty(_target:any,key:any,record:any){events.push('trap:'+String(key)+':'+Object.keys(record).join(','));return true;}});const descriptor:any={get configurable(){events.push('c');return true;},get value(){events.push('v');return 7;}};Object.defineProperty(target,'x',descriptor);console.log(events.join(','));
            """, "c,v,trap:x:value,configurable\n"];
        yield return ["poisoned-prototype-omissions", """
            const events:string[]=[];const getter=()=>7;const descriptor:any=Object.create(null);Object.defineProperty(descriptor,'get',{get(){Object.defineProperty(Object.prototype,'value',{get(){events.push('poison');return 9;},configurable:true});return getter;},configurable:true});const target:any={};let value:any,count:any,present:any;try{Object.defineProperty(target,'x',descriptor);value=target.x;count=events.length;present=Object.hasOwn(Object.getOwnPropertyDescriptor(target,'x')!,'value');}finally{delete (Object.prototype as any).value;}console.log(value,count,present);
            """, "7 0 false\n"];
        yield return ["callable-accessor-identity", """
            class Getter{}const values:any[]=[Getter,Function.prototype,(()=>7).bind(null)];for(const value of values){const target:any={};Object.defineProperty(target,'x',{get:value,set:undefined});const descriptor:any=Object.getOwnPropertyDescriptor(target,'x');console.log(descriptor.get===value,descriptor.set===undefined);}
            """, "true true\ntrue true\ntrue true\n"];
        yield return ["absent-fields-never-get", """
            const events:string[]=[];const descriptor:any=new Proxy({value:7},{has(_target:any,key:any){events.push('h'+String(key));return key==='value';},get(target:any,key:any){events.push('g'+String(key));return target[key];}});const target:any={};Object.defineProperty(target,'x',descriptor);console.log(events.join(','),target.x);
            """, "henumerable,hconfigurable,hvalue,gvalue,hwritable,hget,hset 7\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ObservesEachDescriptorFieldBeforeDefiningTheTarget(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
