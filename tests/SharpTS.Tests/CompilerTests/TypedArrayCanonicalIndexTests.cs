using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

// The emitted GetIndex/SetIndex representation is independent of interpreter ingress.
[Collection("CompilationService")]
public sealed class TypedArrayCanonicalIndexTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[]
        {
            "invalid-minus-zero-converts-value",
            "const a:any=new Uint8Array([9]);let calls=0;const value:any={valueOf(){calls++;return 7}};const result=a['-0']||=value;console.log(result===value,a[0],calls,Object.hasOwn(a,'-0'));",
            "true 9 1 false\n"
        };
        yield return new object[]
        {
            "invalid-fractional-compound",
            "const a:any=new Uint8Array([9]);const result=a['0.5']+=2;console.log(Number.isNaN(result),a[0],Object.hasOwn(a,'0.5'));",
            "true 9 false\n"
        };
        yield return new object[]
        {
            "invalid-index-value-abrupt",
            "const a:any=new Uint8Array([9]),marker={};let calls=0;const value:any={valueOf(){calls++;throw marker}};try{a['NaN']||=value}catch(e){console.log(e===marker)}console.log(a[0],calls,Object.hasOwn(a,'NaN'));",
            "true\n9 1 false\n"
        };
        yield return new object[]
        {
            "valid-physical-first-last",
            "const a:any=new Uint8Array([4,8]);console.log(a['0'],a['1']+=258,a[1]);",
            "4 266 10\n"
        };
        yield return new object[]
        {
            "invalid-canonical-reads",
            "const a:any=new Uint8Array([9]);for(const k of ['-0','0.5','NaN','Infinity','-Infinity','-1','1','1e+21'])console.log(a[k]===undefined);console.log(a[0]);",
            "true\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\n9\n"
        };
        yield return new object[]
        {
            "invalid-canonical-writes-convert-once",
            "const a:any=new Uint8Array([9]);let calls=0;for(const k of ['-0','0.5','NaN','Infinity','-Infinity','-1','1','1e+21']){const v:any={valueOf(){calls++;return 258}};const result=a[k]=v;console.log(result===v,Object.hasOwn(a,k))}console.log(a[0],calls);",
            "true false\ntrue false\ntrue false\ntrue false\ntrue false\ntrue false\ntrue false\ntrue false\n9 8\n"
        };
        yield return new object[]
        {
            "strict-nonextensible-invalid-write",
            "'use strict';const a:any=new Uint8Array([9]);Object.preventExtensions(a);let calls=0;const v:any={valueOf(){calls++;return 7}};const result=a['0.5']=v;console.log(result===v,a[0],calls,Object.hasOwn(a,'0.5'));",
            "true 9 1 false\n"
        };
        yield return new object[]
        {
            "invalid-ignores-inherited-getter",
            "const a:any=new Uint8Array([9]);let hits=0;const proto:any={get '0.5'(){hits++;return 7}};Object.setPrototypeOf(a,proto);console.log(a['0.5']===undefined,hits,a[0]);",
            "true 0 9\n"
        };
        yield return new object[]
        {
            "invalid-ignores-inherited-proxy",
            "const a:any=new Uint8Array([9]);let traps=0;const proto:any=new Proxy({}, {get(){traps++;return 7},set(){traps++;return true}});Object.setPrototypeOf(a,proto);a['0.5']=1;console.log(a['0.5']===undefined,traps,a[0]);",
            "true 0 9\n"
        };
        yield return new object[]
        {
            "actual-proxy-target-keeps-traps",
            "const a:any=new Uint8Array([9]);let traps=0;const p:any=new Proxy(a,{get(){traps++;return 5},set(){traps++;return true}});const result=p['0.5'];p['0.5']=1;console.log(result,traps,a[0]);",
            "5 2 9\n"
        };
        yield return new object[]
        {
            "symbol-route-control",
            "const a:any=new Uint8Array([9]),s=Symbol('s');a[s]=3;console.log(a[s],a[0],Object.getOwnPropertySymbols(a).length);",
            "3 9 1\n"
        };
        yield return new object[]
        {
            "key-reconversion-invalid-to-valid",
            "const a:any=new Uint8Array([0]);let keys=0,values=0,log='';const key:any={[Symbol.toPrimitive](){log+='k'+(++keys);return keys===1?'-0':'0'}};const value:any={valueOf(){log+='v';values++;return 7}};const result=a[key]||=value;console.log(result===value,a[0],keys,values,log);",
            "true 7 2 1 k1k2v\n"
        };
        yield return new object[]
        {
            "key-reconversion-valid-to-invalid",
            "const a:any=new Uint8Array([0]);let keys=0,values=0,log='';const key:any={[Symbol.toPrimitive](){log+='k'+(++keys);return keys===1?'0':'-0'}};const value:any={valueOf(){log+='v';values++;return 7}};const result=a[key]||=value;console.log(result===value,a[0],keys,values,log);",
            "true 0 2 1 k1k2v\n"
        };
        yield return new object[]
        {
            "strict-key-hook-not-replayed",
            "'use strict';const a:any=new Uint8Array([0]);let keys=0,values=0;const key:any={[Symbol.toPrimitive](){keys++;return '-0'}};const value:any={valueOf(){values++;return 7}};const result=a[key]||=value;console.log(result===value,a[0],keys,values);",
            "true 0 2 1\n"
        };
        yield return new object[]
        {
            "invalid-number-array-rejects-bigint",
            "const a:any=new Uint8Array([9]);try{a['-0']=1n}catch(e){console.log(e instanceof TypeError,a[0])}",
            "true 9\n"
        };
        yield return new object[]
        {
            "invalid-bigint-array-rejects-number",
            "const a:any=new BigInt64Array([9n]);try{a['NaN']=1}catch(e){console.log(e instanceof TypeError,a[0]===9n)}",
            "true true\n"
        };
        yield return new object[]
        {
            "invalid-bigint-array-converts-value",
            "const a:any=new BigInt64Array([9n]);let calls=0;const value:any={valueOf(){calls++;return 7n}};const result=a['-0']||=value;console.log(result===value,a[0]===9n,calls,Object.hasOwn(a,'-0'));",
            "true true 1 false\n"
        };
        yield return new object[]
        {
            "classifier-has-no-primitive-hooks",
            "let hits=0;(Number.prototype as any).toString=function(){hits++;return '0'};const a:any=new Uint8Array([9]);console.log(a['0.5']===undefined,hits,a[0]);",
            "true 0 9\n"
        };
        yield return new object[]
        {
            "ordinary-array-brand-control",
            "const a:any=[9];a['-0']=2;a['0.5']=3;a['NaN']=4;console.log(a[0],a['-0'],a['0.5'],a['NaN']);",
            "9 2 3 4\n"
        };
        yield return new object[]
        {
            "physical-length-not-named-getter",
            "const a:any=new Uint8Array([9]);let gets=0;Object.defineProperty(a,'length',{get(){gets++;return 100},configurable:true});console.log(a['0'],a['1']===undefined,gets);",
            "9 true 0\n"
        };
        yield return new object[]
        {
            "literal-free-bigint-invalid-bool",
            "const a:any=new BigInt64Array(1);a['NaN']=true;console.log(String(a[0]),Object.hasOwn(a,'NaN'));",
            "0 false\n"
        };
        yield return new object[]
        {
            "literal-free-biguint-invalid-string",
            "const a:any=new BigUint64Array(1);a['-0']='7';console.log(String(a[0]),Object.hasOwn(a,'-0'));",
            "0 false\n"
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CanonicalNumericStringsKeepIntegerIndexedSemantics(string name, string source, string expected)
    {
        _ = name;
        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal(expected, output);
    }
}
