using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DateConstructorInventory3196RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-reported", "const C:any=Date;const names=Object.getOwnPropertyNames(C),keys=Reflect.ownKeys(C);console.log(names.includes('length'),names.includes('name'),names.includes('prototype'),keys.includes('length'),keys.includes('name'),keys.includes('prototype'));", "true true true true true true\n"];
        yield return ["initial-order", "console.log(Object.getOwnPropertyNames(Date).join(\",\"));console.log(Reflect.ownKeys(Date).join(\",\"));", "length,name,prototype,now,parse,UTC\nlength,name,prototype,now,parse,UTC\n"];
        yield return ["metadata-descriptors", "const C:any=Date;for(const key of [\"length\",\"name\",\"prototype\"]){const d:any=Object.getOwnPropertyDescriptor(C,key);console.log(key,d?.value===C[key],d?.writable,d?.enumerable,d?.configurable);}console.log(C.length,C.name);", "length true false false true\nname true false false true\nprototype true false false false\n7 Date\n"];
        yield return ["method-descriptors", "const C:any=Date;for(const key of [\"now\",\"parse\",\"UTC\"]){const d:any=Object.getOwnPropertyDescriptor(C,key);console.log(key,d.value===C[key],d.writable,d.enumerable,d.configurable);}", "now true true false true\nparse true true false true\nUTC true true false true\n"];
        yield return ["delete-and-recreate", "const C:any=Date,held=C.now;C.extra=1;console.log(delete C.now,C.now===undefined);C.now=()=>7;console.log(Object.getOwnPropertyNames(C).join(\",\"),typeof held,C.now());", "true true\nlength,name,prototype,parse,UTC,extra,now function 7\n"];
        yield return ["accessor-not-read", "const C:any=Date;let calls=0;Object.defineProperty(C,\"now\",{get(){calls++;return ()=>7;},configurable:true});const names=Object.getOwnPropertyNames(C),keys=Reflect.ownKeys(C);console.log(names.includes(\"now\"),keys.includes(\"now\"),calls);", "true true 0\n"];
        yield return ["protected-prototype", "const C:any=Date;console.log(Reflect.deleteProperty(C,\"prototype\"),Object.hasOwn(C,\"prototype\"));", "false true\n"];
        yield return ["symbols-stay-distinct", "const names=Object.getOwnPropertyNames(Date),keys=Reflect.ownKeys(Date);console.log(names.every(key=>typeof key===\"string\"),keys.every(key=>typeof key===\"string\"),Object.getOwnPropertySymbols(Date).length);", "true true 0\n"];
        yield return ["integer-prefix-and-expando", "const C:any=Date;C[10]=10;C[2]=2;C.extra=1;console.log(Object.getOwnPropertyNames(C).join(\",\"));", "2,10,length,name,prototype,now,parse,UTC,extra\n"];
        yield return ["readonly-length-sloppy", "const C:any=Date;C.length=8;console.log(C.length,Object.hasOwn(C,\"length\"));", "7 true\n"];
        yield return ["readonly-length-strict", "const C:any=Date;function write(){\"use strict\";try{C.length=8;}catch(error){console.log(error instanceof TypeError);}}write();console.log(C.length);", "true\n7\n"];
        yield return ["deleted-length-and-redefinition", "const C:any=Date;console.log(delete C.length,C.length,Object.hasOwn(C,\"length\"));Object.defineProperty(C,\"length\",{value:9,configurable:true});console.log(C.length,Object.getOwnPropertyNames(C).join(\",\"));", "true 0 false\n9 name,prototype,now,parse,UTC,length\n"];
        yield return ["same-named-source-class", "const C:any=class Date{static own=1;};console.log(Object.getOwnPropertyNames(C).includes(\"now\"),Object.getOwnPropertyNames(C).includes(\"UTC\"),Object.hasOwn(C,\"own\"));", "false false true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void InitialIntrinsicMetadataAndLiveOverlayKeys(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void SameNamedUnownedBuiltinDoesNotSupplyTheInventory()
    {
        using var interpreter = new SharpTS.Execution.Interpreter();
        var unowned = new SharpTS.Runtime.Types.SharpTSBuiltInConstructor("Date", _ => null);
        Assert.False(interpreter.TryGetBuiltInConstructorOwnStringKeys(unowned, out var names));
        Assert.Empty(names);
    }
}