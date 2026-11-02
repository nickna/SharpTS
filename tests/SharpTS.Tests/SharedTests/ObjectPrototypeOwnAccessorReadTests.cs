using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ObjectPrototypeOwnAccessorReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["dot-before-write", "let gets=0;Object.defineProperty(Object.prototype,'custom',{get(){gets++;return 7},configurable:true});console.log((Object.prototype as any).custom,gets);delete (Object.prototype as any).custom;", "7 1\n"];
        yield return ["bracket-before-write", "let gets=0;Object.defineProperty(Object.prototype,'custom',{get(){gets++;return 7},configurable:true});console.log((Object.prototype as any)['custom'],gets);delete (Object.prototype as any).custom;", "7 1\n"];
        yield return ["receiver", "const p:any=Object.prototype;Object.defineProperty(p,'custom',{get(){return this===p?7:99},configurable:true});console.log(p.custom,p['custom'],Reflect.get(p,'custom'));delete p.custom;", "7 7 7\n"];
        yield return ["numeric-key", "const p:any=Object.prototype;let calls=0;Object.defineProperty(p,'7',{get(){calls++;return 9},configurable:true});console.log(p[7],p['7'],calls);delete p[7];", "9 9 2\n"];
        yield return ["object-key-order", "const p:any=Object.prototype;const log:string[]=[];Object.defineProperty(p,'custom',{get(){log.push('get');return 7},configurable:true});const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return 'custom'}};function target(){log.push('target');return p}console.log(target()[key],log.join(','));delete p.custom;", "7 target,string,get\n"];
        yield return ["getter-abrupt-identity", "const p:any=Object.prototype,marker={};Object.defineProperty(p,'custom',{get(){throw marker},configurable:true});for(const read of [()=>p.custom,()=>p['custom']]){try{read();console.log('missing')}catch(e){console.log(e===marker)}}delete p.custom;", "true\ntrue\n"];
        yield return ["builtin-shadow", "const p:any=Object.prototype;let calls=0;const f=()=>7;Object.defineProperty(p,'toString',{get(){calls++;return f},configurable:true});console.log(p.toString===f,calls,p['toString']===f,calls);delete p.toString;", "true 1 true 2\n"];
        yield return ["callable-data-identity", "const p:any=Object.prototype,f=()=>7;Object.defineProperty(p,'custom',{value:f,writable:true,configurable:true});console.log(p.custom===f,p['custom']===f);delete p.custom;", "true true\n"];
        yield return ["self-deletion", "const p:any=Object.prototype;let calls=0;Object.defineProperty(p,'custom',{get(){calls++;delete p.custom;return 7},configurable:true});console.log(p.custom,p.custom===undefined,calls);", "7 true 1\n"];
        yield return ["setter-only", "const p:any=Object.prototype;let calls=0;Object.defineProperty(p,'custom',{set(v:any){calls++},configurable:true});console.log(p.custom===undefined,p['custom']===undefined,calls);delete p.custom;", "true true 0\n"];
        yield return ["missing-symbol", "const p:any=Object.prototype,key=Symbol('missing');console.log(p[key]===undefined,p[Symbol.toStringTag]===undefined);", "true true\n"];
        yield return ["object-symbol-key", "const p:any=Object.prototype;let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return Symbol.toStringTag}};console.log(p[key]===undefined,calls);", "string\ntrue 1\n"];
        yield return ["descriptor-retention", "const p:any=Object.prototype;function get(){return 7}Object.defineProperty(p,'custom',{get,enumerable:false,configurable:true});console.log(p.custom,p['custom']);const d:any=Object.getOwnPropertyDescriptor(p,'custom');console.log(d.get===get,d.enumerable,d.configurable,'value' in d);delete p.custom;", "7 7\ntrue false true false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void ReadsUseActualIntrinsicOwnDescriptors(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
