using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterDescriptorOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-data-order", "const events:string[]=[];const descriptor:any={get enumerable(){events.push(\"enumerable\");return false;},get configurable(){events.push(\"configurable\");return true;},get value(){events.push(\"value\");return 7;},get writable(){events.push(\"writable\");return false;}};const target:any={};Object.defineProperty(target,\"value\",descriptor);console.log(events.join(\",\"),target.value);", "enumerable,configurable,value,writable 7\n"];
        yield return ["accessor-field-order", "const events:string[]=[],getter=()=>7;const descriptor:any={get enumerable(){events.push(\"e\");return false;},get configurable(){events.push(\"c\");return true;},get get(){events.push(\"g\");return getter;},get set(){events.push(\"s\");return undefined;}};const target:any={};Object.defineProperty(target,\"x\",descriptor);console.log(events.join(\",\"),target.x);", "e,c,g,s 7\n"];
        yield return ["invalid-getter-stops-set", "let trace=\"\";const descriptor:any={get enumerable(){trace+=\"e\";return false;},get configurable(){trace+=\"c\";return true;},get get(){trace+=\"g\";return 1;},get set(){trace+=\"s\";return undefined;}};try{Object.defineProperty({},\"x\",descriptor);}catch(error){console.log(error instanceof TypeError,trace);}", "true ecg\n"];
        yield return ["abrupt-stops-later-fields", "let trace=\"\";const marker={},target:any={};const descriptor:any={get enumerable(){trace+=\"e\";return true;},get configurable(){trace+=\"c\";throw marker;},get value(){trace+=\"v\";return 7;},get writable(){trace+=\"w\";return false;}};try{Reflect.defineProperty(target,\"x\",descriptor);}catch(error){console.log(error===marker,trace,Object.hasOwn(target,\"x\"));}", "true ec false\n"];
        yield return ["proxy-descriptor-has-get", "const trace:string[]=[];const descriptor:any=new Proxy({enumerable:false,configurable:true,value:7,writable:false},{has(target:any,key:any){trace.push(\"h\"+key);return Reflect.has(target,key);},get(target:any,key:any,receiver:any){trace.push(\"g\"+key);return Reflect.get(target,key,receiver);}});const target:any={};Object.defineProperty(target,\"x\",descriptor);console.log(trace.join(\",\"),target.x);", "henumerable,genumerable,hconfigurable,gconfigurable,hvalue,gvalue,hwritable,gwritable,hget,hset 7\n"];
        yield return ["earlier-getter-removes-future-field", "const descriptor:any={enumerable:true,value:7,writable:true};Object.defineProperty(descriptor,\"enumerable\",{get(){delete descriptor.value;return true;}});const target:any={};Object.defineProperty(target,\"x\",descriptor);const result=Object.getOwnPropertyDescriptor(target,\"x\")!;console.log(result.value===undefined,result.writable,result.enumerable);", "true true true\n"];
        yield return ["mixed-kind-after-all-fields", "let trace=\"\";const getter=()=>7;const descriptor:any={get enumerable(){trace+=\"e\";return false;},get configurable(){trace+=\"c\";return true;},get value(){trace+=\"v\";return 7;},get writable(){trace+=\"w\";return true;},get get(){trace+=\"g\";return getter;},get set(){trace+=\"s\";return undefined;}};try{Object.defineProperty({},\"x\",descriptor);}catch(error){console.log(error instanceof TypeError,trace);}", "true ecvwgs\n"];
        yield return ["inherited-fields-and-create", "const trace:string[]=[];const inherited:any={get enumerable(){trace.push(\"e\");return true;},get configurable(){trace.push(\"c\");return true;},get value(){trace.push(\"v\");return 7;},get writable(){trace.push(\"w\");return false;}};const descriptor=Object.create(inherited);const target:any=Object.create(null,{x:descriptor});console.log(trace.join(\",\"),target.x,Object.keys(target).join(\",\"));", "e,c,v,w 7 x\n"];
        yield return ["async-context", "async function run(){await Promise.resolve();let trace=\"\";const descriptor:any={get enumerable(){trace+=\"e\";return false;},get configurable(){trace+=\"c\";return true;},get value(){trace+=\"v\";return 7;},get writable(){trace+=\"w\";return false;}};const target:any={};Object.defineProperties(target,{x:descriptor});console.log(trace,target.x);}run();", "ecvw 7\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void ObservesEachDescriptorFieldInOrder(string name, string source, string expected)
    {
        // The compiled counterpart is tracked independently by #3155.
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
