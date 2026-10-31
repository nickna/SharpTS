using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue2592RegressionTests
{
    [Fact]
    public void OriginalIssueSourceRemainsCorrect()
    {
        const string source = "const state = { trace: '', traps: 0 }, marker = {};\nconst target: any = new Proxy({}, {\n    defineProperty(object: any, key: any, descriptor: any) {\n        state.trace += 't'; state.traps++; return true;\n    }\n});\nconst description: any = { get value() { state.trace += 'v'; return 7; }, configurable: true };\nconsole.log(Reflect.defineProperty(target, 'value', description), state.trace, state.traps);\nstate.trace = ''; state.traps = 0;\ntry { console.log('returned', Reflect.defineProperty(target, 'invalid', { get: 1 } as any)); }\ncatch (error) { console.log('type-error', error instanceof TypeError); }\nconsole.log(state.trace, state.traps);\nstate.trace = ''; state.traps = 0;\nconst throws: any = { get value() { state.trace += 'v'; throw marker; }, configurable: true };\ntry { console.log('returned', Reflect.defineProperty(target, 'throws', throws)); }\ncatch (error) { console.log('marker', error === marker); }\nconsole.log(state.trace, state.traps);\n";
        Assert.Equal("true vt 1\ntype-error true\n 0\nmarker true\nv 0\n", TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    public static IEnumerable<object[]> Cases()
    {
        yield return ["conversion-before-trap-get", "let trace='';const symbol=Symbol('key');\n            const key:any={[Symbol.toPrimitive](hint:string){trace+='k'+hint;return symbol;}};\n            const description:any={get enumerable(){trace+='e';return false;},get configurable(){trace+='c';return true;},get value(){trace+='v';return 7;},get writable(){trace+='w';return false;}};\n            const handler:any={get defineProperty(){trace+='g';return function(target:any,property:any,descriptor:any){trace+='t';console.log(property===symbol,descriptor===description,Object.getPrototypeOf(descriptor)===Object.prototype,Object.keys(descriptor).join(','),descriptor.value);return true;};}};\n            console.log(Reflect.defineProperty(new Proxy({},handler),key,description),trace);\n", "true false true value,writable,enumerable,configurable 7\ntrue kstringecvwgt\n"];
        yield return ["inherited-getter-and-omitted-fields", "let reads=0;\n            const description:any=Object.create({get value(){reads++;return 7;}});\n            const target:any=new Proxy({},{defineProperty(target:any,key:any,descriptor:any){console.log(Object.keys(descriptor).join(','),descriptor.value,'writable' in descriptor,'configurable' in descriptor);return true;}});\n            console.log(Reflect.defineProperty(target,'x',description),reads);\n", "value 7 false false\ntrue 1\n"];
        yield return ["revoked-and-invalid-descriptor", "let reads=0;const proxy=Proxy.revocable({},{});proxy.revoke();\n            const description:any={get value(){reads++;return 7;},configurable:true};\n            try{Reflect.defineProperty(proxy.proxy,'x',description);}catch(error){console.log(error instanceof TypeError,reads);}\n            let traps=0;const target=new Proxy({},{defineProperty(){traps++;return true;}});\n            for(const invalid of [null,undefined,1,'x',true]){\n                try{Reflect.defineProperty(target,'x',invalid as any);console.log('accepted');}catch(error){console.log(error instanceof TypeError,traps);}\n            }\n", "true 1\ntrue 0\ntrue 0\ntrue 0\ntrue 0\ntrue 0\n"];
        yield return ["trapless-nested-and-false", "let reads=0;const base:any={};const nested:any=new Proxy(new Proxy(base,{}),{});\n            const description:any={get value(){reads++;return 7;},configurable:true};\n            console.log(Reflect.defineProperty(nested,'x',description),base.x,reads);\n            Object.preventExtensions(base);console.log(Reflect.defineProperty(nested,'y',description),reads);\n            let traps=0;const refused:any=new Proxy({},{defineProperty(){traps++;return false;}});\n            console.log(Reflect.defineProperty(refused,'x',description),reads,traps);\n", "true 7 1\nfalse 2\nfalse 3 1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OrderedProtocolControls(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
