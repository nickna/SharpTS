using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StrictBuiltinConstructorDelete3187RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-strict-date-native", "function run(){\"use strict\";Object.defineProperty(Date,\"now\",{configurable:false});try{console.log(delete (Date as any).now);}catch(error){console.log(error instanceof TypeError);}}run();", "true\n"];
        yield return ["strict-promise-overlay", "function run(){\"use strict\";const owner:any=Promise;Object.defineProperty(owner,\"marker\",{value:7,configurable:false});try{console.log(delete owner.marker);}catch(error){console.log(error instanceof TypeError);}console.log(owner.marker);}run();", "true\n7\n"];
        yield return ["sloppy-refusal", "const owner:any=Date;Object.defineProperty(owner,\"now\",{configurable:false});console.log(delete owner.now,typeof owner.now);", "false function\n"];
        yield return ["strict-reflect-returns-false", "function run(){\"use strict\";Object.defineProperty(Date,\"now\",{configurable:false});console.log(Reflect.deleteProperty(Date,\"now\"),typeof Date.now);}run();", "false function\n"];
        yield return ["strict-configurable-delete", "function run(){\"use strict\";const owner:any=Promise;owner.marker=7;console.log(delete owner.marker,owner.marker===undefined);}run();", "true true\n"];
        yield return ["strict-computed-key-once", "function run(){\"use strict\";const owner:any=Date;Object.defineProperty(owner,\"now\",{configurable:false});let calls=0;const key:any={[Symbol.toPrimitive](hint:string){calls++;console.log(hint);return \"now\";}};try{console.log(delete owner[key]);}catch(error){console.log(error instanceof TypeError,calls);}}run();", "string\ntrue 1\n"];
        yield return ["exact-reported-source", "const D:any=Date;Object.defineProperty(D,'now',{value:()=>7,configurable:false});function remove(){'use strict';try{return delete D.now;}catch(error){return error instanceof TypeError;}}console.log(remove(),D.now());", "true 7\n"];
        yield return ["native-prototype-strict", "const C:any=Date;function remove(){\"use strict\";try{return delete C.prototype;}catch(error){return error instanceof TypeError;}}console.log(remove(),Object.hasOwn(C,\"prototype\"));", "true true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void StrictFailedDeletionThrowsGuestTypeError(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}