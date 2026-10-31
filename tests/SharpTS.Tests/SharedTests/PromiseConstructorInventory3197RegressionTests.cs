using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PromiseConstructorInventory3197RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-reported", "const C:any=Promise;const names=Object.getOwnPropertyNames(C),keys=Reflect.ownKeys(C);console.log(names.includes('length'),names.includes('name'),names.includes('prototype'),keys.includes('length'),keys.includes('name'),keys.includes('prototype'));", "true true true true true true\n"];
        yield return ["standard-initial-order", "const C:any=Promise;console.log(Object.getOwnPropertyNames(C).filter(key=>key!==\"allKeyed\"&&key!==\"allSettledKeyed\").join(\",\"));console.log(Reflect.ownKeys(C).filter(key=>typeof key===\"string\"&&key!==\"allKeyed\"&&key!==\"allSettledKeyed\").join(\",\"));", "length,name,prototype,all,allSettled,any,race,resolve,reject,withResolvers\nlength,name,prototype,all,allSettled,any,race,resolve,reject,withResolvers\n"];
        yield return ["metadata-descriptors", "const C:any=Promise;for(const key of [\"length\",\"name\",\"prototype\"]){const d:any=Object.getOwnPropertyDescriptor(C,key);console.log(key,d?.value===C[key],d?.writable,d?.enumerable,d?.configurable);}console.log(C.length,C.name);", "length true false false true\nname true false false true\nprototype true false false false\n1 Promise\n"];
        yield return ["actual-standard-method-descriptors", "const C:any=Promise;for(const key of [\"all\",\"allSettled\",\"any\",\"race\",\"resolve\",\"reject\",\"withResolvers\"]){const d:any=Object.getOwnPropertyDescriptor(C,key);console.log(key,d.value===C[key],d.writable,d.enumerable,d.configurable);}", "all true true false true\nallSettled true true false true\nany true true false true\nrace true true false true\nresolve true true false true\nreject true true false true\nwithResolvers true true false true\n"];
        yield return ["delete-and-recreate", "const C:any=Promise;C.extra=1;console.log(delete C.resolve,C.resolve===undefined);C.resolve=function(value:any){return value;};console.log(Object.getOwnPropertyNames(C).filter(key=>key!==\"allKeyed\"&&key!==\"allSettledKeyed\").join(\",\"));", "true true\nlength,name,prototype,all,allSettled,any,race,reject,withResolvers,extra,resolve\n"];
        yield return ["accessor-not-read", "const C:any=Promise;let calls=0;Object.defineProperty(C,\"resolve\",{get(){calls++;return function(value:any){return value;};},configurable:true});const names=Object.getOwnPropertyNames(C),keys=Reflect.ownKeys(C);console.log(names.includes(\"resolve\"),keys.includes(\"resolve\"),calls);", "true true 0\n"];
        yield return ["real-symbol-separation", "const C:any=Promise,symbol=Symbol(\"extra\");C[symbol]=1;const names=Object.getOwnPropertyNames(C),keys=Reflect.ownKeys(C);console.log(names.every(key=>typeof key===\"string\"),keys.includes(Symbol.species),keys[keys.length-1]===symbol,Object.getOwnPropertySymbols(C)[0]===Symbol.species);", "true true true true\n"];
        yield return ["integer-prefix-and-expando", "const C:any=Promise;C[10]=10;C[2]=2;C.extra=1;console.log(Object.getOwnPropertyNames(C).filter(key=>key!==\"allKeyed\"&&key!==\"allSettledKeyed\").join(\",\"));", "2,10,length,name,prototype,all,allSettled,any,race,resolve,reject,withResolvers,extra\n"];
        yield return ["freeze-new-length-record", "const C:any=Promise;Object.freeze(C);const d:any=Object.getOwnPropertyDescriptor(C,\"length\");console.log(C.length,d.writable,d.configurable,Reflect.deleteProperty(C,\"length\"));", "1 false false false\n"];
        yield return ["readonly-length", "const C:any=Promise;C.length=2;console.log(C.length,Object.hasOwn(C,\"length\"));", "1 true\n"];
        yield return ["deleted-length-and-redefinition", "const C:any=Promise;console.log(delete C.length,C.length,Object.hasOwn(C,\"length\"));Object.defineProperty(C,\"length\",{value:9,configurable:true});console.log(C.length,Object.getOwnPropertyNames(C).filter(key=>key!==\"allKeyed\"&&key!==\"allSettledKeyed\").join(\",\"));", "true 0 false\n9 name,prototype,all,allSettled,any,race,resolve,reject,withResolvers,length\n"];
        yield return ["same-named-source-class", "const C:any=class Promise{static own=1;};console.log(Object.getOwnPropertyNames(C).includes(\"resolve\"),Object.getOwnPropertyNames(C).includes(\"all\"),Object.hasOwn(C,\"own\"));", "false false true\n"];
        yield return ["foreign-retained-realm-owner", "import * as vm from \"node:vm\";const box:any=vm.runInNewContext(\"({P:Promise})\");const C:any=box.P;console.log(Object.getOwnPropertyNames(C).includes(\"resolve\"),C.length===1,Reflect.ownKeys(C).includes(Symbol.species));", "true true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void RetainedIntrinsicOwnMetadataAndLiveOverlayKeys(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var output = source.Contains("node:vm", StringComparison.Ordinal)
            ? TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source },
                "main.ts", ExecutionMode.Interpreted)
            : TestHarness.RunInterpreted(source);
        Assert.Equal(expected, output);
    }

    [Fact]
    public void SameNamedUnownedBuiltinDoesNotSupplyTheInventory()
    {
        using var interpreter = new SharpTS.Execution.Interpreter();
        var unowned = new SharpTS.Runtime.Types.SharpTSBuiltInConstructor("Promise", _ => null);
        Assert.False(interpreter.TryGetBuiltInConstructorOwnStringKeys(unowned, out var names));
        Assert.Empty(names);
    }
}