using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CompiledDefinePropertyKeyConversionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["reported-exotic-symbol-and-surplus", """
            let trace="";const symbol=Symbol("key"),target:any={};const key:any={[Symbol.toPrimitive](hint:string){trace+="k"+hint;return symbol;}};const descriptor:any={get value(){trace+="d";return 7;},configurable:true};try{const result=Object.defineProperty(target,key,descriptor);console.log(result===target,target[symbol],trace);}catch(error){console.log("threw",error instanceof TypeError,trace);}trace="";const define:any=Object.defineProperty;const stringKey:any={toString(){trace+="k";return"x";}};try{define({},stringKey,descriptor,(()=>{trace+="e";return 9;})());console.log(trace);}catch(error){console.log("extra-threw",error instanceof TypeError,trace);}
            """, "true 7 kstringd\nekd\n"];
        yield return ["missing-arguments-call-forms", """
            let trace="";const key:any={toString(){trace+="k";return"x";}},description:any={get value(){trace+="d";return 7;}};const define:any=Object.defineProperty;function attempt(label:string,call:()=>any){trace="";try{call();console.log(label,"returned",trace);}catch(error){console.log(label,error instanceof TypeError,trace);}}attempt("direct-zero",()=>(Object.defineProperty as any)());attempt("alias-zero",()=>define());attempt("invalid-one",()=>define(1));attempt("valid-one",()=>define({}));attempt("invalid-two",()=>define(1,key));attempt("valid-two",()=>define({},key));attempt("explicit-undefined",()=>define({},key,undefined));attempt("call-zero",()=>define.call(null));attempt("call-two",()=>define.call(null,{},key));attempt("apply-zero",()=>define.apply(null,[]));attempt("apply-two",()=>define.apply(null,[{},key]));attempt("reflect-zero",()=>Reflect.apply(define,null,[]));attempt("reflect-two",()=>Reflect.apply(define,null,[{},key]));console.log("length",define.length);
            """, "direct-zero true \nalias-zero true \ninvalid-one true \nvalid-one true \ninvalid-two true \nvalid-two true k\nexplicit-undefined true k\ncall-zero true \ncall-two true k\napply-zero true \napply-two true k\nreflect-zero true \nreflect-two true k\nlength 3\n"];
        yield return ["ordinary-string-hint-symbol", """
            const events:string[]=[],symbol=Symbol('x'),target:any={};const key:any={toString(){events.push('toString');return symbol;},valueOf(){events.push('valueOf');return 1;}};Object.defineProperty(target,key,{get value(){events.push('descriptor');return 7;}});console.log(target[symbol],events.join(','));
            """, "7 toString,descriptor\n"];
        yield return ["ordinary-valueof-symbol-fallback", """
            const events:string[]=[],symbol=Symbol('x'),target:any={};const key:any={toString(){events.push('toString');return {};},valueOf(){events.push('valueOf');return symbol;}};Object.defineProperty(target,key,{get value(){events.push('descriptor');return 7;}});console.log(target[symbol],events.join(','));
            """, "7 toString,valueOf,descriptor\n"];
        yield return ["abrupt-key-before-descriptor", """
            let trace='';const marker={},key:any={get [Symbol.toPrimitive](){trace+='k';throw marker;}},descriptor:any={get value(){trace+='d';return 7;}};try{Object.defineProperty({},key,descriptor);}catch(error){console.log(error===marker,trace);}
            """, "true k\n"];
        yield return ["invalid-hooks-before-descriptor", """
            for(const hook of [1,()=>({})]){let trace='';const key:any={[Symbol.toPrimitive]:hook},descriptor:any={get value(){trace+='d';return 7;}};try{Object.defineProperty({},key,descriptor);}catch(error){console.log(error instanceof TypeError,trace.length);}}
            """, "true 0\ntrue 0\n"];
        yield return ["target-key-descriptor-order", """
            let trace='';const symbol=Symbol('x'),key:any={[Symbol.toPrimitive](hint:string){trace+='k'+hint;return symbol;}};try{Object.defineProperty(1 as any,key,{get value(){trace+='d';return 7;}});}catch(error){console.log(error instanceof TypeError,trace.length);}try{Object.defineProperty({},key,undefined as any);}catch(error){console.log(error instanceof TypeError,trace);}
            """, "true 0\ntrue kstring\n"];
        yield return ["proxy-receives-normalized-symbol", """
            let trace='';const symbol=Symbol('x'),key:any={[Symbol.toPrimitive](hint:string){trace+='k'+hint;return symbol;}};const target:any=new Proxy({},{defineProperty(target:any,property:any,descriptor:any){trace+='p';console.log(property===symbol,descriptor.value);return true;}});Object.defineProperty(target,key,{get value(){trace+='d';return 7;},configurable:true});console.log(trace);
            """, "true 7\nkstringdp\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NormalizesPropertyKeysOnceBeforeDescriptorConversion(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
