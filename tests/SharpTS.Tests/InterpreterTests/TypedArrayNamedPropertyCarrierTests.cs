using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

public sealed class TypedArrayNamedPropertyCarrierTests
{
    // Compiler counterparts are preserved under the separately owned direct-length
    // and physical-own-key defects; the unchanged common length suite is shared.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["own-writable-length", "const a:any=new Uint8Array(2);Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});a.length=8;console.log(a.length,Reflect.get(a,'length'));", "8 8\n"];
        yield return ["own-setter-receiver", "const a:any=new Uint8Array(2);let value=3,calls=0,receiver=false;Object.defineProperty(a,'length',{get(){return value},set(v:any){calls++;receiver=this===a;value=v},configurable:true});a.length=8;console.log(value,calls,receiver,Reflect.get(a,'length'));", "8 1 true 8\n"];
        yield return ["physical-and-named-own-keys", "const a:any=new Uint8Array(2);let gets=0;Object.defineProperty(a,'probe',{get(){gets++;return 9},enumerable:true,configurable:true});Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});console.log(Object.getOwnPropertyNames(a).join(','),Reflect.ownKeys(a).join(','),Object.keys(a).join(','),gets);", "0,1,probe,length 0,1,probe,length 0,1,probe 0\n"];
        yield return ["physical-element-after-shadow", "const a:any=new Uint8Array(2);a[0]=9;Object.defineProperty(a,'length',{value:7,writable:true,configurable:true});console.log(a[0],a.length,Reflect.get(a,'length'));", "9 7 7\n"];
        yield return ["object-prototype-override", "const v:any=new Uint8Array(3);Object.setPrototypeOf(v,Object.prototype);console.log(Reflect.get(v,'length')===undefined);", "true\n"];
        yield return ["same-explicit-after-prevention", "const v:any=new Uint8Array(3),p={length:7};Object.setPrototypeOf(v,p);Object.preventExtensions(v);console.log(Object.setPrototypeOf(v,p)===v,Reflect.get(v,'length'));", "true 7\n"];
        yield return ["different-prototype-after-prevention", "const v:any=new Uint8Array(3);Object.preventExtensions(v);let caught=false;try{Object.setPrototypeOf(v,Object.prototype)}catch(e){caught=e instanceof TypeError}console.log(caught,Reflect.get(v,'length'));", "true 3\n"];
        yield return ["named-index-updates", "const a:any=new Uint8Array(2);Object.defineProperty(a,'length',{value:3,writable:true,configurable:true});console.log(a['length']+=2,a['length']++,a['length']);", "5 5 6\n"];
        yield return ["named-index-logical-short-circuit", "const a:any=new Uint8Array(2);Object.defineProperty(a,'length',{value:3,writable:true,configurable:true});let calls=0;function rhs(){calls++;return 7}a['length']||=rhs();a['length']&&=rhs();console.log(a['length'],calls);", "7 1\n"];
        yield return ["named-own-presence", "const a:any=new Uint8Array(2);console.log(Object.hasOwn(a,'length'));Object.defineProperty(a,'length',{value:7,configurable:true});console.log(Object.hasOwn(a,'length'));delete a.length;console.log(Object.hasOwn(a,'length'));", "false\ntrue\nfalse\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void NamedPropertiesRetainTheirOwnerAndPhysicalElementInventory(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
