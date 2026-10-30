using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterpreterIssue3153RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["async-receiver-and-key", "const symbol=Symbol(\"key\"),target:any={};target[symbol]=7;const trace:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){trace.push(hint);return symbol;}};async function receiver(){trace.push(\"receiver\");return target;}async function property(){trace.push(\"key\");return key;}async function run(){console.log(delete(await receiver())[await property()],Object.hasOwn(target,symbol),trace.join(\",\"));}run();", "true false receiver,key,string\n"];
        yield return ["proxy-symbol-and-abrupt", "const symbol=Symbol(\"key\");let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return symbol;}};const target:any=new Proxy({},{deleteProperty(target:any,property:any){console.log(property===symbol);return true;}});console.log(delete target[key],calls);const marker={};try{delete target[{[Symbol.toPrimitive](){throw marker;}} as any];}catch(error){console.log(error===marker,calls);}", "string\ntrue\ntrue 1\ntrue 1\n"];
        yield return ["original", "const symbol = Symbol('extra');\nconst target: any = {};\ntarget[symbol] = 7;\nconst key: any = { [Symbol.toPrimitive]() { return symbol; } };\nconsole.log(delete target[key], Object.hasOwn(target, symbol));\n", "true false\n"];
        yield return ["abrupt-key", "let calls=0;const target:any={};const key:any={[Symbol.toPrimitive](){calls++;throw new Error('stop');}};try{delete target[key];}catch(error){console.log((error as any).message,calls);}\n", "stop 1\n"];
        yield return ["generator-ordinary", "function* values(){}let calls=0;const key=Symbol('key'),target:any=values();target[key]=7;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(delete target[property],Object.hasOwn(target,key),calls);\n", "string\ntrue false 1\n"];
        yield return ["generator-strict", "\"use strict\";function* values(){}let calls=0;const key=Symbol('key'),target:any=values();target[key]=7;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(delete target[property],Object.hasOwn(target,key),calls);\n", "string\ntrue false 1\n"];
        yield return ["record-ordinary", "function* values(){}let calls=0;const key=Symbol('key'),target:any={};target[key]=7;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(delete target[property],Object.hasOwn(target,key),calls);\n", "string\ntrue false 1\n"];
        yield return ["record-strict", "\"use strict\";function* values(){}let calls=0;const key=Symbol('key'),target:any={};target[key]=7;const property:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return key;}};console.log(delete target[property],Object.hasOwn(target,key),calls);\n", "string\ntrue false 1\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OrderedProtocolControls(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
