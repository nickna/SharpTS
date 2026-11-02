using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class TypedArrayReflectLengthTests
{
    // Both engines preserve ordinary named descriptors, original receivers and
    // explicit prototypes; physical integer-indexed storage stays separate.
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const value:any=new Uint8Array(3);console.log(Reflect.get(value,'length'));\n", "3\n"];
        yield return ["all-element-kinds", "console.log(Reflect.get(new Int8Array(0),'length'),Reflect.get(new Uint8Array(1),'length'),Reflect.get(new Uint8ClampedArray(2),'length'),Reflect.get(new Int16Array(3),'length'),Reflect.get(new Uint16Array(4),'length'),Reflect.get(new Int32Array(5),'length'),Reflect.get(new Uint32Array(6),'length'),Reflect.get(new Float32Array(7),'length'),Reflect.get(new Float64Array(8),'length'),Reflect.get(new BigInt64Array(9),'length'),Reflect.get(new BigUint64Array(10),'length'));", "0 1 2 3 4 5 6 7 8 9 10\n"];
        yield return ["subview-elements", "const buffer=new ArrayBuffer(16);const value:any=new Uint16Array(buffer,4,3);console.log(Reflect.get(value,'length'));", "3\n"];
        yield return ["alternate-typed-receiver", "const value:any=new Uint8Array(3),receiver:any=new Int32Array(5);console.log(Reflect.get(value,'length',receiver));", "5\n"];
        yield return ["incompatible-receivers", "const value:any=new Uint8Array(3);for(const receiver of [{length:9},null,undefined,7,'x']){let caught=false;try{Reflect.get(value,'length',receiver)}catch(e){caught=e instanceof TypeError}console.log(caught)}", "true\ntrue\ntrue\ntrue\ntrue\n"];
        yield return ["own-data", "const value:any=new Uint8Array(3);Object.defineProperty(value,'length',{value:9,configurable:true});console.log(Reflect.get(value,'length'),Reflect.get(value,'length',{}));", "9 9\n"];
        yield return ["own-accessor-receiver", "const value:any=new Uint8Array(3),receiver={marker:7};let calls=0;Object.defineProperty(value,'length',{get:function(this:any){calls++;return this.marker},configurable:true});console.log(Reflect.get(value,'length',receiver),calls);", "7 1\n"];
        yield return ["own-getterless", "const value:any=new Uint8Array(3);Object.defineProperty(value,'length',{get:undefined,set(v:any){},configurable:true});console.log(Reflect.get(value,'length')===undefined);", "true\n"];
        yield return ["deleted-own-reveals-intrinsic", "const value:any=new Uint8Array(3);Object.defineProperty(value,'length',{value:9,configurable:true});console.log(Reflect.get(value,'length'));delete value.length;console.log(Reflect.get(value,'length'));", "9\n3\n"];
        yield return ["custom-prototype-data", "const value:any=new Uint8Array(3);Object.setPrototypeOf(value,{length:12});console.log(Reflect.get(value,'length'),Reflect.get(value,'length',{}));", "12 12\n"];
        yield return ["null-prototype", "const value:any=new Uint8Array(3);Object.setPrototypeOf(value,null);console.log(Reflect.get(value,'length')===undefined);", "true\n"];
        yield return ["custom-prototype-getter", "const value:any=new Uint8Array(3),receiver={marker:7};Object.setPrototypeOf(value,{get length(){return (this as any).marker}});console.log(Reflect.get(value,'length',receiver));", "7\n"];
        yield return ["proxy-trap-precedence", "const value:any=new Uint8Array(3);let calls=0;const proxy=new Proxy(value,{get(target,key,receiver){calls++;return key==='length'?17:Reflect.get(target,key,receiver)}});console.log(Reflect.get(proxy,'length'),calls);", "17 1\n"];
        yield return ["transparent-proxy-receiver", "const value:any=new Uint8Array(3),proxy=new Proxy(value,{});let caught=false;try{Reflect.get(proxy,'length')}catch(e){caught=e instanceof TypeError}console.log(caught,Reflect.get(proxy,'length',value));", "true 3\n"];
        yield return ["key-and-receiver-order", "const value:any=new Uint8Array(3),receiver:any=new Uint16Array(5),log:string[]=[];const key:any={[Symbol.toPrimitive](hint:string){log.push(hint);return 'length'}};function target(){log.push('target');return value}function propertyKey(){log.push('key');return key}function other(){log.push('receiver');return receiver}console.log(Reflect.get(target(),propertyKey(),other()),log.join(','));", "5 target,key,receiver,string\n"];
        yield return ["first-class-and-ordinary-controls", "const get:any=Reflect.get,value:any=new Uint8Array(3);console.log(get(value,'length'),Reflect.get([1,2],'length'),Reflect.get({length:9},'length',{}));", "3 2 9\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void PreservesDescriptorReceiverAndPrototypePrecedence(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }
}
