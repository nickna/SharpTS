using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PartialConstructorDescriptor3202RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-original", "const held=Date.now;Object.defineProperty(Date,\"now\",{configurable:false});console.log(typeof Date.now,held===Date.now);", "function true\n"];
        yield return ["preserve-overlay-value-and-flags", "const D:any=Date;D.marker=7;Object.defineProperty(D,\"marker\",{enumerable:false});const d:any=Object.getOwnPropertyDescriptor(D,\"marker\");console.log(D.marker,d.writable,d.enumerable,d.configurable);", "7 true false true\n"];
        yield return ["preserve-accessor-without-read", "const D:any=Date;let value=7,reads=0;Object.defineProperty(D,\"marker\",{get(){reads++;return value;},set(next:number){value=next;},enumerable:true,configurable:true});Object.defineProperty(D,\"marker\",{enumerable:false});console.log(reads);D.marker=8;const d:any=Object.getOwnPropertyDescriptor(D,\"marker\");console.log(D.marker,typeof d.get,typeof d.set,d.enumerable,d.configurable);", "0\n8 function function false true\n"];
        yield return ["compatible-nonconfigurable-value", "const D:any=Date;Object.defineProperty(D,\"marker\",{value:7,writable:true,configurable:false});Object.defineProperty(D,\"marker\",{writable:false});const d:any=Object.getOwnPropertyDescriptor(D,\"marker\");console.log(D.marker,d.writable,d.enumerable,d.configurable);", "7 false false false\n"];
        yield return ["incompatible-nonconfigurable", "const D:any=Date;Object.defineProperty(D,\"marker\",{value:7,writable:false,configurable:false});try{Object.defineProperty(D,\"marker\",{value:8});}catch(error){console.log(error instanceof TypeError);}console.log(D.marker);", "true\n7\n"];
        yield return ["new-partial-defaults", "const D:any=Date;Object.defineProperty(D,\"marker\",{value:7});const d:any=Object.getOwnPropertyDescriptor(D,\"marker\");console.log(D.marker,d.writable,d.enumerable,d.configurable);", "7 false false false\n"];
        yield return ["internal-merge-does-not-read-prototype", "const D:any=Date;D.marker=7;let reads=0;const descriptor:any=Object.create(null);descriptor.enumerable=false;Object.defineProperty(Object.prototype,\"get\",{get(){reads++;throw new Error(\"poison\");},configurable:true});Object.defineProperty(D,\"marker\",descriptor);console.log(D.marker,reads);", "7 0\n"];
        yield return ["ordinary-object-control", "const D:any={marker:7};Object.defineProperty(D,\"marker\",{enumerable:false});const d:any=Object.getOwnPropertyDescriptor(D,\"marker\");console.log(D.marker,d.writable,d.enumerable,d.configurable);", "7 true false true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void PartialDefinitionsPreserveActualOwnFields(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}