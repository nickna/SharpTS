using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NativeObjectPreventExtensionsTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["original", "const O:any=Object;Object.preventExtensions(O);const replacement=()=>7;console.log(Reflect.set(O,'keys',replacement),O.keys===replacement,Reflect.set(O,'added',3),Object.hasOwn(O,'added'));", "true true false false\n"];
        yield return ["preserves-native-flags-and-identity", "const O:any=Object,read=Object.getOwnPropertyDescriptor,names=['assign','create','defineProperties','defineProperty','entries','freeze','fromEntries','getOwnPropertyDescriptor','getOwnPropertyDescriptors','getOwnPropertyNames','getOwnPropertySymbols','getPrototypeOf','hasOwn','is','isExtensible','isFrozen','isSealed','keys','preventExtensions','seal','setPrototypeOf','values'],before=names.map(name=>O[name]);Object.preventExtensions(O);console.log(names.length,names.every((name,index)=>{const d:any=read(O,name);return d.value===before[index]&&d.writable&&!d.enumerable&&d.configurable;}),Object.isExtensible(O));", "22 true false\n"];
        yield return ["deleted-native-stays-absent", "const O:any=Object;delete O.values;Object.preventExtensions(O);O.values=()=>7;console.log(O.values,Object.hasOwn(O,'values'),delete O.keys);O.keys=()=>8;console.log(O.keys,Object.hasOwn(O,'keys'));", "undefined false true\nundefined false\n"];
        yield return ["accessors-are-not-invoked", "const O:any=Object;let reads=0,writes=0;const get=()=>{reads++;return 7;},set=(value:any)=>{writes+=value;};Object.defineProperty(O,'keys',{get,set,configurable:true});Object.preventExtensions(O);const d:any=Object.getOwnPropertyDescriptor(O,'keys');console.log(reads,d.get===get,d.set===set,d.configurable,Reflect.set(O,'keys',3),writes);", "0 true true true true 3\n"];
        yield return ["distinct-receiver", "const O:any=Object,original=O.keys,receiver:any={},replacement=()=>7;Object.preventExtensions(O);console.log(Reflect.set(O,'keys',replacement,receiver),receiver.keys===replacement,O.keys===original,Object.getOwnPropertyDescriptor(receiver,'keys')!.enumerable);", "true true true true\n"];
        yield return ["idempotent-and-ordinary-expandos", "const O:any=Object;Object.defineProperty(O,'extra',{value:1,writable:true,enumerable:true,configurable:true});Object.preventExtensions(O);O.extra=2;Object.preventExtensions(O);O.extra=3;const d:any=Object.getOwnPropertyDescriptor(O,'extra');console.log(d.value,d.writable,d.enumerable,d.configurable,delete O.extra,Object.hasOwn(O,'extra'));", "3 true true true true false\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void RetainsCurrentOwnDescriptorsBeforePreventingExtensions(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
